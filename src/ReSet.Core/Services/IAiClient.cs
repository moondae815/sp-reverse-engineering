using System.Threading;
using System.Threading.Tasks;

namespace ReSet.Core.Services
{
    public interface IAiClient
    {
        Task<string> ChatAsync(string systemPrompt, string userPrompt, float temperature, CancellationToken cancellationToken = default);
    }
}
