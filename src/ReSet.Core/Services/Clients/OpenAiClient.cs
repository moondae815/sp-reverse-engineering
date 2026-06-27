using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ReSet.Core.Services.Clients
{
    public class OpenAiClient : IAiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;

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
            if (string.IsNullOrWhiteSpace(_apiKey) && _endpoint.Contains("openai.com"))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            var lowerModel = _modelName.ToLowerInvariant();
            float targetTemp = temperature;

            // gpt-5.x 모델 중 x가 5 이상인 모델 및 o1, o3 모델은 temperature = 1.0f 필수 제약 적용
            var versionMatch = System.Text.RegularExpressions.Regex.Match(lowerModel, @"gpt-?5\.(\d+)");
            bool isGpt55OrHigher = false;
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out int minorVersion))
            {
                if (minorVersion >= 5)
                {
                    isGpt55OrHigher = true;
                }
            }

            bool isReasoningEnforcedModel = 
                lowerModel.StartsWith("o1") || 
                lowerModel.StartsWith("o3") ||
                isGpt55OrHigher;

            if (isReasoningEnforcedModel)
            {
                targetTemp = 1.0f; // 최신 추론 모델 및 GPT-5.5+ 계열 API 제약 대응 (1.0만 허용)
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
                // o1/o3 추론 모델 계열은 temperature 파라미터를 보내는 것 자체가 에러가 발생할 수 있으므로 
                // reasoning 모델인 경우 temperature 필드를 완전히 제외합니다.
            }
            else
            {
                requestBody.Add("temperature", targetTemp);
            }

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/chat/completions")
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
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).\n상세 에러 내용: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;
                return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            }
        }
    }
}
