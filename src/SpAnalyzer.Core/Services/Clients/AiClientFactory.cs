using System;
using System.Net.Http;

namespace SpAnalyzer.Core.Services.Clients
{
    public static class AiClientFactory
    {
        public static IAiClient CreateClient(string provider, string modelName, string apiKey, string endpoint, HttpClient? httpClient = null)
        {
            var client = httpClient ?? new HttpClient();

            if (string.IsNullOrWhiteSpace(provider))
            {
                throw new ArgumentException("AI Provider가 지정되지 않았습니다.", nameof(provider));
            }

            return provider.ToLowerInvariant() switch
            {
                "openai" => new OpenAiClient(client, apiKey, endpoint, modelName),
                "ollama" => new OllamaClient(client, endpoint, modelName),
                "anthropic" => new AnthropicClient(client, apiKey, endpoint, modelName),
                "google" => new GoogleClient(client, apiKey, endpoint, modelName),
                _ => throw new NotSupportedException($"지원되지 않는 AI Provider입니다: {provider}")
            };
        }
    }
}
