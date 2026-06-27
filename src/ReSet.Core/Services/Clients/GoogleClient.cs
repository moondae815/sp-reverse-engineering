using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ReSet.Core.Services.Clients
{
    public class GoogleClient : IAiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;

        public GoogleClient(HttpClient httpClient, string apiKey, string endpoint, string modelName)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
            _modelName = modelName;

            var ep = string.IsNullOrWhiteSpace(endpoint) ? "https://generativelanguage.googleapis.com" : endpoint.Trim();
            _endpoint = ep;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, string? effort = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new ArgumentException("Google API 키가 설정되지 않았습니다.");
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = userPrompt }
                        }
                    }
                },
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                },
                generationConfig = new
                {
                    temperature = temperature
                }
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            
            // Assembly Google/Gemini Endpoint URI
            var url = $"{_endpoint.TrimEnd('/')}/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
            
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;

                // promptFeedback 차단 여부 확인
                if (root.TryGetProperty("promptFeedback", out var promptFeedback))
                {
                    if (promptFeedback.TryGetProperty("blockReason", out var blockReason))
                    {
                        throw new InvalidOperationException($"Google Gemini API 요청이 안전 필터에 의해 차단되었습니다. (원인: {blockReason.GetString()})");
                    }
                }

                // candidates 존재 및 빈 값 여부 확인
                if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException("Google Gemini API 응답에서 생성된 후보군(candidates)을 찾을 수 없습니다.");
                }

                var firstCandidate = candidates[0];

                // finishReason에 따른 오류 처리
                if (firstCandidate.TryGetProperty("finishReason", out var finishReason))
                {
                    var reason = finishReason.GetString();
                    if (!string.Equals(reason, "STOP", StringComparison.OrdinalIgnoreCase) && 
                        !string.Equals(reason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Google Gemini API 생성이 정상 완료되지 않았습니다. (이유: {reason})");
                    }
                }

                // content 및 parts, text 탐색 및 반환
                if (!firstCandidate.TryGetProperty("content", out var contentElement))
                {
                    throw new InvalidOperationException("Google Gemini API 응답 후보군 내에 content 속성이 존재하지 않습니다. (안전 필터 등에 의한 차단 가능성)");
                }

                if (!contentElement.TryGetProperty("parts", out var partsElement) || partsElement.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException("Google Gemini API 응답 content 내에 parts 속성이 존재하지 않거나 비어 있습니다.");
                }

                if (!partsElement[0].TryGetProperty("text", out var textElement))
                {
                    throw new InvalidOperationException("Google Gemini API 응답 parts 내에 text 속성이 존재하지 않습니다.");
                }

                return textElement.GetString() ?? string.Empty;
            }
        }
    }
}
