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
