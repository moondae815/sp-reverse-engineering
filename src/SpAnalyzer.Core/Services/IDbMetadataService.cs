using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IDbMetadataService
    {
        Task<List<string>> GetStoredProcedureNamesAsync(string connectionString, CancellationToken cancellationToken = default);
        Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName, int maxDepth, CancellationToken cancellationToken = default);
    }
}
