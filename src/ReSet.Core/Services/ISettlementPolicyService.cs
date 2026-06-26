using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReSet.Core.Services
{
    public interface ISettlementPolicyService
    {
        Task<string> GenerateSettlementPolicyRulebookAsync(
            string connectionString, 
            List<string> spFullNames, 
            int maxDepth, 
            CancellationToken cancellationToken = default);
    }
}
