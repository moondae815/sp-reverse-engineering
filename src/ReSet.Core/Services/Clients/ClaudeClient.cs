using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReSet.Core.Models;
using Serilog;

namespace ReSet.Core.Services.Clients
{
    public class ClaudeClient : IAiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;

        public ClaudeClient(HttpClient httpClient, string apiKey, string endpoint, string modelName)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
            _modelName = modelName;

            var ep = string.IsNullOrWhiteSpace(endpoint) ? "https://api.anthropic.com" : endpoint.Trim();
            if (ep.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
            {
                ep = ep.Substring(0, ep.Length - "/v1/messages".Length).TrimEnd('/');
            }
            _endpoint = ep;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, string? effort = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new ArgumentException("Claude API 키가 설정되지 않았습니다.");
            }

            // Claude 모델별 최대 출력 토큰(max_tokens) 및 Thinking 설정 대응
            var lowerModel = _modelName.ToLowerInvariant();
            int maxTokens = 4096; // 기본 안전 한도 (Claude 3 Opus 등)

            if (lowerModel.Contains("4-") || lowerModel.Contains("4."))
            {
                if (lowerModel.Contains("haiku-4-5"))
                {
                    maxTokens = 64000; // Claude Haiku 4.5 한도
                }
                else
                {
                    maxTokens = 128000; // Claude Opus 4.8, Sonnet 4.6 한도 (128k)
                }
            }
            else if (lowerModel.Contains("sonnet") || lowerModel.Contains("haiku") || lowerModel.Contains("3-5") || lowerModel.Contains("3-7") || lowerModel.Contains("3.5") || lowerModel.Contains("3.7"))
            {
                maxTokens = 8192; // Claude 3.5 / 3.7 Sonnet 및 Haiku 등 최신 모델 한도
            }

            bool is4thGen = lowerModel.Contains("4-") || lowerModel.Contains("4.");
            bool is37Gen = lowerModel.Contains("3-7") || lowerModel.Contains("3.7");
            bool enableThinking = (is4thGen || is37Gen) && (!string.IsNullOrWhiteSpace(effort) || lowerModel.Contains("opus-4-8") || lowerModel.Contains("sonnet-4-6"));
            object requestBody;

            if (enableThinking)
            {
                if (is4thGen)
                {
                    string? apiEffort = null;
                    if (!string.IsNullOrWhiteSpace(effort))
                    {
                        apiEffort = effort.ToLowerInvariant() switch
                        {
                            "low" => "low",
                            "medium" => "medium",
                            "high" => "high",
                            "xhigh" => "max",
                            _ => "medium"
                        };
                    }

                    var requestMap = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "model", _modelName },
                        { "system", systemPrompt },
                        { "messages", new[] { new { role = "user", content = userPrompt } } },
                        { "max_tokens", maxTokens },
                        { "temperature", 1.0 },
                        { "thinking", new { type = "adaptive" } }
                    };

                    if (apiEffort != null)
                    {
                        requestMap.Add("output_config", new { effort = apiEffort });
                    }

                    requestBody = requestMap;
                }
                else
                {
                    int budgetTokens = 4000; // 기본값
                    if (!string.IsNullOrWhiteSpace(effort))
                    {
                        budgetTokens = effort.ToLowerInvariant() switch
                        {
                            "low" => 2000,
                            "medium" => 4000,
                            "high" => 16000,
                            "xhigh" => 32000,
                            _ => 4000
                        };
                    }

                    if (budgetTokens >= maxTokens)
                    {
                        budgetTokens = maxTokens - 1000;
                        if (budgetTokens < 1000) budgetTokens = 1000;
                    }

                    requestBody = new
                    {
                        model = _modelName,
                        system = systemPrompt,
                        messages = new[]
                        {
                            new { role = "user", content = userPrompt }
                        },
                        max_tokens = maxTokens,
                        temperature = 1.0,
                        thinking = new
                        {
                            type = "enabled",
                            budget_tokens = budgetTokens
                        }
                    };
                }
            }
            else
            {
                requestBody = new
                {
                    model = _modelName,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userPrompt }
                    },
                    max_tokens = maxTokens,
                    temperature = temperature
                };
            }

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var requestUri = $"{_endpoint.TrimEnd('/')}/v1/messages";
            Log.Debug("Claude API 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("Claude API HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Debug("Claude API HTTP 응답 수신 완료 - StatusCode: {StatusCode}\n[Response Content]:\n{ResponseContent}", (int)response.StatusCode, responseContent);

            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;

                // 에러 응답 확인
                if (root.TryGetProperty("error", out var errorElement))
                {
                    var errMsg = errorElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "알 수 없는 API 오류";
                    Log.Error("Claude API 응답 내 error 감지 - Message: {Message}", errMsg);
                    throw new InvalidOperationException($"Claude API 에러 응답 수신: {errMsg}");
                }

                if (!root.TryGetProperty("content", out var contentElement) || contentElement.GetArrayLength() == 0)
                {
                    Log.Error("Claude API 응답 content 속성 누락 또는 빈 배열");
                    throw new InvalidOperationException("Claude API 응답 데이터 내에 content 속성이 존재하지 않거나 비어 있습니다.");
                }

                // Thinking 모드에서는 content 배열에 { "type": "thinking", ... } 블록이
                // 먼저 오고, 실제 텍스트는 { "type": "text", ... } 블록에 위치합니다.
                // 배열을 순회하여 type == "text"인 항목을 찾아 반환합니다.
                string? resultText = null;
                string? thinkingText = null;
                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var typeElem))
                    {
                        var typeStr = typeElem.GetString();
                        if (typeStr == "thinking" && contentItem.TryGetProperty("thinking", out var thinkElem))
                        {
                            thinkingText = thinkElem.GetString();
                        }
                        else if (typeStr == "text" && contentItem.TryGetProperty("text", out var textElem))
                        {
                            resultText = textElem.GetString();
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(thinkingText))
                {
                    Log.Information("[Claude Thinking Process]:\n{Thinking}", thinkingText);
                }

                if (resultText == null)
                {
                    // 디버깅을 위해 content 배열의 type 목록을 로그로 기록
                    var types = new System.Collections.Generic.List<string>();
                    foreach (var item in contentElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var t))
                            types.Add(t.GetString() ?? "unknown");
                    }
                    Log.Error("Claude API 응답 content 배열에 type=text 항목 없음 - 수신된 type 목록: [{Types}]", string.Join(", ", types));
                    throw new InvalidOperationException("Claude API 응답 content 내에 text 속성이 존재하지 않습니다.");
                }

                return resultText ?? string.Empty;
            }
        }

        public async IAsyncEnumerable<StreamingChunk> StreamChatAsync(
            string systemPrompt, 
            string userPrompt, 
            float temperature, 
            string? effort = null, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new ArgumentException("Claude API 키가 설정되지 않았습니다.");
            }

            var lowerModel = _modelName.ToLowerInvariant();
            int maxTokens = 4096;

            if (lowerModel.Contains("4-") || lowerModel.Contains("4."))
            {
                if (lowerModel.Contains("haiku-4-5"))
                {
                    maxTokens = 64000;
                }
                else
                {
                    maxTokens = 128000;
                }
            }
            else if (lowerModel.Contains("sonnet") || lowerModel.Contains("haiku") || lowerModel.Contains("3-5") || lowerModel.Contains("3-7") || lowerModel.Contains("3.5") || lowerModel.Contains("3.7"))
            {
                maxTokens = 8192;
            }

            bool is4thGen = lowerModel.Contains("4-") || lowerModel.Contains("4.");
            bool is37Gen = lowerModel.Contains("3-7") || lowerModel.Contains("3.7");
            bool enableThinking = (is4thGen || is37Gen) && (!string.IsNullOrWhiteSpace(effort) || lowerModel.Contains("opus-4-8") || lowerModel.Contains("sonnet-4-6"));
            object requestBody;

            if (enableThinking)
            {
                if (is4thGen)
                {
                    string? apiEffort = null;
                    if (!string.IsNullOrWhiteSpace(effort))
                    {
                        apiEffort = effort.ToLowerInvariant() switch
                        {
                            "low" => "low",
                            "medium" => "medium",
                            "high" => "high",
                            "xhigh" => "max",
                            _ => "medium"
                        };
                    }

                    var requestMap = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "model", _modelName },
                        { "system", systemPrompt },
                        { "messages", new[] { new { role = "user", content = userPrompt } } },
                        { "max_tokens", maxTokens },
                        { "temperature", 1.0 },
                        { "thinking", new { type = "adaptive" } },
                        { "stream", true }
                    };

                    if (apiEffort != null)
                    {
                        requestMap.Add("output_config", new { effort = apiEffort });
                    }

                    requestBody = requestMap;
                }
                else
                {
                    int budgetTokens = 4000;
                    if (!string.IsNullOrWhiteSpace(effort))
                    {
                        budgetTokens = effort.ToLowerInvariant() switch
                        {
                            "low" => 2000,
                            "medium" => 4000,
                            "high" => 16000,
                            "xhigh" => 32000,
                            _ => 4000
                        };
                    }

                    if (budgetTokens >= maxTokens)
                    {
                        budgetTokens = maxTokens - 1000;
                        if (budgetTokens < 1000) budgetTokens = 1000;
                    }

                    requestBody = new
                    {
                        model = _modelName,
                        system = systemPrompt,
                        messages = new[]
                        {
                            new { role = "user", content = userPrompt }
                        },
                        max_tokens = maxTokens,
                        temperature = 1.0,
                        thinking = new
                        {
                            type = "enabled",
                            budget_tokens = budgetTokens
                        },
                        stream = true
                    };
                }
            }
            else
            {
                requestBody = new
                {
                    model = _modelName,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userPrompt }
                    },
                    max_tokens = maxTokens,
                    temperature = temperature,
                    stream = true
                };
            }

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var requestUri = $"{_endpoint.TrimEnd('/')}/v1/messages";

            Log.Debug("Claude API 스트리밍 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("Claude API 스트리밍 HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                    throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
                }

                Log.Debug("Claude API 스트리밍 응답 수신 시작 - StatusCode: {StatusCode}", (int)response.StatusCode);

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        Log.Debug("Claude Streaming Line: {Line}", line);

                        if (line.StartsWith("data: "))
                        {
                            var data = line.Substring("data: ".Length).Trim();
                            if (data == "[DONE]")
                            {
                                break;
                            }

                            string? thinking = null;
                            string? text = null;
                            bool parsedSuccessfully = false;

                            try
                            {
                                using (var doc = JsonDocument.Parse(data))
                                {
                                    var root = doc.RootElement;
                                    if (root.TryGetProperty("type", out var typeElem))
                                    {
                                        var typeStr = typeElem.GetString();
                                        if (typeStr == "content_block_delta" && root.TryGetProperty("delta", out var delta))
                                        {
                                            if (delta.TryGetProperty("type", out var deltaTypeElem))
                                            {
                                                var deltaType = deltaTypeElem.GetString();
                                                if (deltaType == "thinking_delta" && delta.TryGetProperty("thinking", out var thinkingElem))
                                                {
                                                    thinking = thinkingElem.GetString();
                                                }
                                                else if (deltaType == "text_delta" && delta.TryGetProperty("text", out var textElem))
                                                {
                                                    text = textElem.GetString();
                                                }
                                            }
                                        }
                                    }
                                }
                                parsedSuccessfully = true;
                            }
                            catch (JsonException)
                            {
                                // JSON 파싱 에러 무시
                            }

                            if (parsedSuccessfully)
                            {
                                if (!string.IsNullOrEmpty(thinking))
                                {
                                    yield return new StreamingChunk(ChunkType.Thinking, thinking);
                                }
                                if (!string.IsNullOrEmpty(text))
                                {
                                    yield return new StreamingChunk(ChunkType.Text, text);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                response?.Dispose();
            }
        }
    }
}
