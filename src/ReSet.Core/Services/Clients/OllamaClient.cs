using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ReSet.Core.Models;

namespace ReSet.Core.Services.Clients
{
    public class OllamaClient : IAiClient
    {
        private readonly OpenAiClient _openAiClient;

        public string ProviderName => "Ollama";
        public string ModelName => _openAiClient.ModelName;

        public OllamaClient(HttpClient httpClient, string endpoint, string modelName)
        {
            var ep = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint.Trim();
            // Ollama does not require an API key by default
            _openAiClient = new OpenAiClient(httpClient, string.Empty, ep, modelName);
        }

        public Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, string? effort = null, CancellationToken cancellationToken = default)
        {
            return _openAiClient.ChatAsync(systemPrompt, userPrompt, temperature, effort, cancellationToken);
        }

        public IAsyncEnumerable<StreamingChunk> StreamChatAsync(string systemPrompt, string userPrompt, float temperature, string? effort = null, CancellationToken cancellationToken = default)
        {
            return _openAiClient.StreamChatAsync(systemPrompt, userPrompt, temperature, effort, cancellationToken);
        }
    }
}
