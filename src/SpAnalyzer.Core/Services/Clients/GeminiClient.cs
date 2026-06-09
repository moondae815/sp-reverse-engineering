using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpAnalyzer.Core.Services.Clients
{
    public class GeminiClient : IAiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _modelName;

        public GeminiClient(HttpClient httpClient, string apiKey, string endpoint, string modelName)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey;
            _modelName = modelName;

            var ep = string.IsNullOrWhiteSpace(endpoint) ? "https://generativelanguage.googleapis.com" : endpoint.Trim();
            _endpoint = ep;
        }

        public async Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new ArgumentException("Gemini API 키가 설정되지 않았습니다.");
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
            
            // Assembly Gemini Endpoint URI
            var url = $"{_endpoint.TrimEnd('/')}/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
            
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;
                return root.GetProperty("candidates")[0]
                           .GetProperty("content")
                           .GetProperty("parts")[0]
                           .GetProperty("text")
                           .GetString() ?? string.Empty;
            }
        }
    }
}
