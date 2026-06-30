using System;
using System.Net.Http;
using System.Net.Http.Headers;
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
    public class OpenAiClient : IAiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;
        private readonly AsyncLocal<string?> _lastThinkingText = new AsyncLocal<string?>();

        public string ProviderName => "OpenAI";
        public string ModelName => _modelName;
        public string? LastThinkingText => _lastThinkingText.Value;

        public OpenAiClient(HttpClient httpClient, string apiKey, string endpoint, string modelName)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
            _modelName = modelName;

            var ep = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com/v1" : endpoint.Trim();
            if (ep.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                ep = ep.Substring(0, ep.Length - "/chat/completions".Length).TrimEnd('/');
            }
            _endpoint = ep;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, string? effort = null, CancellationToken cancellationToken = default)
        {
            _lastThinkingText.Value = null;
            if (string.IsNullOrWhiteSpace(_apiKey) && _endpoint.Contains("openai.com"))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            var lowerModel = _modelName.ToLowerInvariant();
            bool isResponsesApi = lowerModel.Contains("gpt-5");

            if (isResponsesApi)
            {
                var requestBody = new Dictionary<string, object>
                {
                    { "model", _modelName },
                    { "input", new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    },
                    { "reasoning", new { effort = effort?.ToLowerInvariant() switch
                        {
                            "low" => "low",
                            "medium" => "medium",
                            "high" => "high",
                            "xhigh" => "high",
                            _ => "medium"
                        },
                        summary = "auto"
                      }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(requestBody);
                var requestUri = $"{_endpoint.TrimEnd('/')}/responses";
                Log.Debug("OpenAI Responses API 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

                var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("OpenAI Responses API HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                    throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                Log.Debug("OpenAI Responses API HTTP 응답 수신 완료 - StatusCode: {StatusCode}\n[Response Content]:\n{ResponseContent}", (int)response.StatusCode, responseContent);

                using (var doc = JsonDocument.Parse(responseContent))
                {
                    var root = doc.RootElement;

                    // 에러 응답 먼저 확인 (error가 null이 아니거나 존재할 때)
                    if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                    {
                        var errMsg = errorElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "알 수 없는 API 오류";
                        throw new InvalidOperationException($"OpenAI Responses API 에러 응답 수신: {errMsg}");
                    }

                    // root 자체는 Object이고, 실제 결과 목록은 "output" 프로퍼티(Array)에 들어있음
                    if (root.TryGetProperty("output", out var outputElem) && outputElem.ValueKind == JsonValueKind.Array)
                    {
                        string? resultText = null;
                        string? reasoningText = null;

                        foreach (var item in outputElem.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var typeElem))
                            {
                                var typeStr = typeElem.GetString();
                                if (typeStr == "reasoning" && item.TryGetProperty("summary", out var summaryElem) && summaryElem.ValueKind == JsonValueKind.Array)
                                {
                                    var sb = new StringBuilder();
                                    foreach (var sumItem in summaryElem.EnumerateArray())
                                    {
                                        if (sumItem.TryGetProperty("type", out var sumType) && sumType.GetString() == "summary_text" && sumItem.TryGetProperty("text", out var textElem))
                                        {
                                            sb.Append(textElem.GetString());
                                        }
                                    }
                                    reasoningText = sb.ToString();
                                }
                                else if (typeStr == "message" && item.TryGetProperty("content", out var contentElem) && contentElem.ValueKind == JsonValueKind.Array)
                                {
                                    var sb = new StringBuilder();
                                    foreach (var conItem in contentElem.EnumerateArray())
                                    {
                                        if (conItem.TryGetProperty("type", out var conType) && conType.GetString() == "output_text" && conItem.TryGetProperty("text", out var textElem))
                                        {
                                            sb.Append(textElem.GetString());
                                        }
                                    }
                                    resultText = sb.ToString();
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(reasoningText))
                        {
                            Log.Information("[OpenAI Responses API Reasoning Summary]:\n{Reasoning}", reasoningText);
                            _lastThinkingText.Value = reasoningText;
                        }

                        return resultText ?? string.Empty;
                    }
                    else
                    {
                        throw new InvalidOperationException("OpenAI Responses API 응답 내에 output 배열 속성이 존재하지 않습니다.");
                    }
                }
            }
            else
            {
                float targetTemp = temperature;

                // o1, o3 모델은 temperature = 1.0f 필수 제약 적용
                bool isReasoningEnforcedModel = 
                    lowerModel.StartsWith("o1") || 
                    lowerModel.StartsWith("o3");

                if (isReasoningEnforcedModel)
                {
                    targetTemp = 1.0f;
                }

                var requestBody = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "model", _modelName },
                    { "messages", new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    }
                };

                if (isReasoningEnforcedModel)
                {
                    if (!string.IsNullOrWhiteSpace(effort))
                    {
                        var apiEffort = effort.ToLowerInvariant() switch
                        {
                            "low" => "low",
                            "medium" => "medium",
                            "high" => "high",
                            "xhigh" => "high",
                            _ => "medium"
                        };
                        requestBody.Add("reasoning_effort", apiEffort);
                    }
                }
                else
                {
                    requestBody.Add("temperature", targetTemp);
                }

                var jsonPayload = JsonSerializer.Serialize(requestBody);
                var requestUri = $"{_endpoint.TrimEnd('/')}/chat/completions";
                Log.Debug("OpenAI API 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

                var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("OpenAI API HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                    throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                Log.Debug("OpenAI API HTTP 응답 수신 완료 - StatusCode: {StatusCode}\n[Response Content]:\n{ResponseContent}", (int)response.StatusCode, responseContent);

                using (var doc = JsonDocument.Parse(responseContent))
                {
                    var root = doc.RootElement;

                    // 에러 응답 확인
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        var errMsg = errorElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "알 수 없는 API 오류";
                        Log.Error("OpenAI API 응답 내 error 감지 - Message: {Message}", errMsg);
                        throw new InvalidOperationException($"OpenAI API 에러 응답 수신: {errMsg}");
                    }

                    if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.GetArrayLength() == 0)
                    {
                        Log.Error("OpenAI API 응답 choices 속성 누락 또는 빈 배열");
                        throw new InvalidOperationException("OpenAI API 응답 데이터 내에 choices 속성이 존재하지 않거나 비어 있습니다.");
                    }

                    var firstChoice = choicesElement[0];
                    if (!firstChoice.TryGetProperty("message", out var messageElement))
                    {
                        Log.Error("OpenAI API 응답 choices[0] 내 message 속성 누락");
                        throw new InvalidOperationException("OpenAI API 응답 choices 내에 message 속성이 존재하지 않습니다.");
                    }

                    string? reasoningContent = null;
                    if (messageElement.TryGetProperty("reasoning_content", out var reasoningElement))
                    {
                        reasoningContent = reasoningElement.GetString();
                    }
                    else if (messageElement.TryGetProperty("reasoning", out var reasoningAltElement))
                    {
                        reasoningContent = reasoningAltElement.GetString();
                    }
                    else if (messageElement.TryGetProperty("thinking", out var thinkingAltElement))
                    {
                        reasoningContent = thinkingAltElement.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(reasoningContent))
                    {
                        Log.Information("[OpenAI Reasoning Process]:\n{Reasoning}", reasoningContent);
                        _lastThinkingText.Value = reasoningContent;
                    }

                    if (!messageElement.TryGetProperty("content", out var contentElement))
                    {
                        Log.Error("OpenAI API 응답 message 내 content 속성 누락");
                        throw new InvalidOperationException("OpenAI API 응답 message 내에 content 속성이 존재하지 않습니다.");
                    }

                    return contentElement.GetString() ?? string.Empty;
                }
            }
        }

        public async IAsyncEnumerable<StreamingChunk> StreamChatAsync(
            string systemPrompt, 
            string userPrompt, 
            float temperature, 
            string? effort = null, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _endpoint.Contains("openai.com"))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            var lowerModel = _modelName.ToLowerInvariant();
            bool isResponsesApi = lowerModel.Contains("gpt-5");

            if (isResponsesApi)
            {
                var requestBody = new Dictionary<string, object>
                {
                    { "model", _modelName },
                    { "input", new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    },
                    { "reasoning", new { effort = effort?.ToLowerInvariant() switch
                        {
                            "low" => "low",
                            "medium" => "medium",
                            "high" => "high",
                            "xhigh" => "high",
                            _ => "medium"
                        },
                        summary = "auto"
                      }
                    },
                    { "stream", true }
                };

                var jsonPayload = JsonSerializer.Serialize(requestBody);
                var requestUri = $"{_endpoint.TrimEnd('/')}/responses";

                Log.Debug("OpenAI Responses API 스트리밍 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

                var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }

                HttpResponseMessage? response = null;
                try
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Log.Error("OpenAI Responses API 스트리밍 HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                        throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
                    }

                    Log.Debug("OpenAI Responses API 스트리밍 응답 수신 시작 - StatusCode: {StatusCode}", (int)response.StatusCode);

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

                            Log.Verbose("OpenAI Responses Streaming Line: {Line}", line);

                            if (line.StartsWith("data: "))
                            {
                                var data = line.Substring("data: ".Length).Trim();
                                if (data == "[DONE]")
                                {
                                    break;
                                }

                                string? reasoning = null;
                                string? content = null;
                                bool parsedSuccessfully = false;

                                try
                                {
                                    using (var doc = JsonDocument.Parse(data))
                                    {
                                        var root = doc.RootElement;
                                        if (root.TryGetProperty("type", out var typeElem))
                                        {
                                            var typeStr = typeElem.GetString() ?? string.Empty;

                                            // 1) 추론 델타/요약 파싱
                                            // 일반적인 스트리밍 델타 형태 (예: response.reasoning.delta, response.reasoning_text.delta 등)
                                            if (typeStr.Contains("reasoning") && typeStr.Contains("delta") && root.TryGetProperty("delta", out var reasoningDeltaElem))
                                            {
                                                reasoning = reasoningDeltaElem.GetString();
                                            }
                                            // 혹시 다른 이름의 추론 델타가 오는 경우를 위한 폴백
                                            else if (typeStr.Contains("thinking") && typeStr.Contains("delta") && root.TryGetProperty("delta", out var thinkingDeltaElem))
                                            {
                                                reasoning = thinkingDeltaElem.GetString();
                                            }
                                            // 기존 완결형 summary 배열이 오는 경우 대응
                                            else if (typeStr == "reasoning" && root.TryGetProperty("summary", out var summaryElem) && summaryElem.ValueKind == JsonValueKind.Array)
                                            {
                                                var sb = new StringBuilder();
                                                foreach (var sumItem in summaryElem.EnumerateArray())
                                                {
                                                    if (sumItem.TryGetProperty("type", out var sumType) && sumType.GetString() == "summary_text" && sumItem.TryGetProperty("text", out var textElem))
                                                    {
                                                        sb.Append(textElem.GetString());
                                                    }
                                                }
                                                reasoning = sb.ToString();
                                            }

                                            // 2) 최종 명세서 출력 텍스트 파싱
                                            if (typeStr.Contains("output_text.delta") && root.TryGetProperty("delta", out var deltaElem))
                                            {
                                                content = deltaElem.GetString();
                                            }
                                        }
                                    }
                                    parsedSuccessfully = true;
                                }
                                catch (JsonException)
                                {
                                    // 일부 API 게이트웨이 파싱 실패 무시
                                }

                                if (parsedSuccessfully)
                                {
                                    if (!string.IsNullOrEmpty(reasoning))
                                    {
                                        yield return new StreamingChunk(ChunkType.Thinking, reasoning);
                                    }
                                    if (!string.IsNullOrEmpty(content))
                                    {
                                        yield return new StreamingChunk(ChunkType.Text, content);
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
            else
            {
                float targetTemp = temperature;

                // o1, o3 모델은 temperature = 1.0f 필수 제약 적용
                bool isReasoningEnforcedModel = 
                    lowerModel.StartsWith("o1") || 
                    lowerModel.StartsWith("o3");

                if (isReasoningEnforcedModel)
                {
                    targetTemp = 1.0f;
                }

                var requestBody = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "model", _modelName },
                    { "messages", new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    },
                    { "stream", true }
                };

                if (isReasoningEnforcedModel)
                {
                    if (!string.IsNullOrWhiteSpace(effort))
                    {
                        var apiEffort = effort.ToLowerInvariant() switch
                        {
                            "low" => "low",
                            "medium" => "medium",
                            "high" => "high",
                            "xhigh" => "high",
                            _ => "medium"
                        };
                        requestBody.Add("reasoning_effort", apiEffort);
                    }
                }
                else
                {
                    requestBody.Add("temperature", targetTemp);
                }

                var jsonPayload = JsonSerializer.Serialize(requestBody);
                var requestUri = $"{_endpoint.TrimEnd('/')}/chat/completions";

                Log.Debug("OpenAI API 스트리밍 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

                var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }

                HttpResponseMessage? response = null;
                try
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Log.Error("OpenAI API 스트리밍 HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                        throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
                    }

                    Log.Debug("OpenAI API 스트리밍 응답 수신 시작 - StatusCode: {StatusCode}", (int)response.StatusCode);

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

                            Log.Verbose("OpenAI Streaming Line: {Line}", line);

                            if (line.StartsWith("data: "))
                            {
                                var data = line.Substring("data: ".Length).Trim();
                                if (data == "[DONE]")
                                {
                                    break;
                                }

                                string? reasoning = null;
                                string? content = null;
                                bool parsedSuccessfully = false;

                                try
                                {
                                    using (var doc = JsonDocument.Parse(data))
                                    {
                                        var root = doc.RootElement;
                                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                                        {
                                            var firstChoice = choices[0];
                                            if (firstChoice.TryGetProperty("delta", out var delta))
                                            {
                                                if (delta.TryGetProperty("reasoning_content", out var reasoningElem))
                                                {
                                                    reasoning = reasoningElem.GetString();
                                                }
                                                else if (delta.TryGetProperty("reasoning", out var reasoningAltElem))
                                                {
                                                    reasoning = reasoningAltElem.GetString();
                                                }
                                                else if (delta.TryGetProperty("thinking", out var thinkingAltElem))
                                                {
                                                    reasoning = thinkingAltElem.GetString();
                                                }

                                                if (delta.TryGetProperty("content", out var contentElem))
                                                {
                                                    content = contentElem.GetString();
                                                }
                                            }
                                        }
                                    }
                                    parsedSuccessfully = true;
                                }
                                catch (JsonException)
                                {
                                    // 일부 API 게이트웨이 파싱 실패 무시
                                }

                                if (parsedSuccessfully)
                                {
                                    if (!string.IsNullOrEmpty(reasoning))
                                    {
                                        yield return new StreamingChunk(ChunkType.Thinking, reasoning);
                                    }
                                    if (!string.IsNullOrEmpty(content))
                                    {
                                        yield return new StreamingChunk(ChunkType.Text, content);
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
}
