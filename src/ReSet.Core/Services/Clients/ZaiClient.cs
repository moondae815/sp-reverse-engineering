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
    public class ZaiClient : IAiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;

        public string ProviderName => "Z.ai";
        public string ModelName => _modelName;

        public ZaiClient(HttpClient httpClient, string apiKey, string endpoint, string modelName)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
            _modelName = modelName;

            var ep = string.IsNullOrWhiteSpace(endpoint) ? "https://api.z.ai/api" : endpoint.Trim();
            if (ep.EndsWith("/paas/v4/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                ep = ep.Substring(0, ep.Length - "/paas/v4/chat/completions".Length).TrimEnd('/');
            }
            _endpoint = ep;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, string? effort = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _endpoint.Contains("z.ai"))
            {
                throw new ArgumentException("Z.ai API 키가 설정되지 않았습니다.");
            }

            var requestBody = new Dictionary<string, object>
            {
                { "model", _modelName },
                { "messages", new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(effort))
            {
                var apiEffort = effort.ToLowerInvariant() switch
                {
                    "low" => "minimal",
                    "medium" => "high",
                    "high" => "max",
                    _ => "high"
                };
                requestBody.Add("reasoning_effort", apiEffort);
                requestBody.Add("thinking", new { type = "enabled" });
            }
            else
            {
                requestBody.Add("temperature", temperature);
            }

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var requestUri = $"{_endpoint.TrimEnd('/')}/paas/v4/chat/completions";
            Log.Debug("Z.ai API 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

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
                Log.Error("Z.ai API HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Debug("Z.ai API HTTP 응답 수신 완료 - StatusCode: {StatusCode}\n[Response Content]:\n{ResponseContent}", (int)response.StatusCode, responseContent);

            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errorElement))
                {
                    var errMsg = errorElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "알 수 없는 API 오류";
                    Log.Error("Z.ai API 응답 내 error 감지 - Message: {Message}", errMsg);
                    throw new InvalidOperationException($"Z.ai API 에러 응답 수신: {errMsg}");
                }

                if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.GetArrayLength() == 0)
                {
                    Log.Error("Z.ai API 응답 choices 속성 누락 또는 빈 배열");
                    throw new InvalidOperationException("Z.ai API 응답 데이터 내에 choices 속성이 존재하지 않거나 비어 있습니다.");
                }

                var firstChoice = choicesElement[0];
                if (!firstChoice.TryGetProperty("message", out var messageElement))
                {
                    Log.Error("Z.ai API 응답 choices[0] 내 message 속성 누락");
                    throw new InvalidOperationException("Z.ai API 응답 choices 내에 message 속성이 존재하지 않습니다.");
                }

                if (messageElement.TryGetProperty("reasoning_content", out var reasoningElement))
                {
                    var reasoningContent = reasoningElement.GetString();
                    if (!string.IsNullOrWhiteSpace(reasoningContent))
                    {
                        Log.Information("[Z.ai Reasoning Process]:\n{Reasoning}", reasoningContent);
                    }
                }

                if (!messageElement.TryGetProperty("content", out var contentElement))
                {
                    Log.Error("Z.ai API 응답 message 내 content 속성 누락");
                    throw new InvalidOperationException("Z.ai API 응답 message 내에 content 속성이 존재하지 않습니다.");
                }

                return contentElement.GetString() ?? string.Empty;
            }
        }

        public async IAsyncEnumerable<StreamingChunk> StreamChatAsync(
            string systemPrompt, 
            string userPrompt, 
            float temperature, 
            string? effort = null, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _endpoint.Contains("z.ai"))
            {
                throw new ArgumentException("Z.ai API 키가 설정되지 않았습니다.");
            }

            var requestBody = new Dictionary<string, object>
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

            if (!string.IsNullOrWhiteSpace(effort))
            {
                var apiEffort = effort.ToLowerInvariant() switch
                {
                    "low" => "minimal",
                    "medium" => "high",
                    "high" => "max",
                    _ => "high"
                };
                requestBody.Add("reasoning_effort", apiEffort);
                requestBody.Add("thinking", new { type = "enabled" });
            }
            else
            {
                requestBody.Add("temperature", temperature);
            }

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var requestUri = $"{_endpoint.TrimEnd('/')}/paas/v4/chat/completions";

            Log.Debug("Z.ai API 스트리밍 요청 전송 준비 - URI: {Uri}\n[Payload JSON]:\n{Payload}", requestUri, jsonPayload);

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
                    Log.Error("Z.ai API 스트리밍 HTTP 요청 실패 - StatusCode: {StatusCode} ({ReasonPhrase})\n[Error Response Content]:\n{ErrorContent}", (int)response.StatusCode, response.ReasonPhrase, errorContent);
                    throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
                }

                Log.Debug("Z.ai API 스트리밍 응답 수신 시작 - StatusCode: {StatusCode}", (int)response.StatusCode);

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

                        Log.Debug("Z.ai Streaming Line: {Line}", line);

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
