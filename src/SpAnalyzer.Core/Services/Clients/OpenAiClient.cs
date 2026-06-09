using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpAnalyzer.Core.Services.Clients
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

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _endpoint.Contains("openai.com"))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = temperature
            };

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
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;
                return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            }
        }
    }
}
