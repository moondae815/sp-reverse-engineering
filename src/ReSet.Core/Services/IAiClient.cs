using ReSet.Core.Models;

namespace ReSet.Core.Services
{
    public interface IAiClient
    {
        string ProviderName { get; }
        string ModelName { get; }
        Task<AiResult> ChatAsync(string systemPrompt, string userPrompt, float temperature, string? effort = null, CancellationToken cancellationToken = default);
    }
}
