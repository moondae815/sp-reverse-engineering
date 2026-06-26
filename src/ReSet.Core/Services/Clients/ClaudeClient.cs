using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, CancellationToken cancellationToken = default)
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

            object requestBody;
            if (lowerModel.Contains("opus-4-8") || lowerModel.Contains("sonnet-4-6"))
            {
                // Adaptive Thinking 필수/지원 모델 처리 (thinking 활성화 시 temperature는 반드시 1.0이어야 함)
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
                        type = "adaptive"
                    }
                };
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
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/v1/messages")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

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
                return root.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
            }
        }
    }
}
