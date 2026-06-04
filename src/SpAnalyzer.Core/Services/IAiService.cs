using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IAiService
    {
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions, string? feedbackLog = null);
        Task<ReviewResult> ReviewSpecificationAsync(SpDefinition spDef, string specMarkdown);
        Task<string> GenerateBatchMigrationPlanAsync(SpDefinition spDef, string targetLanguage);
        Task<string> GenerateConsolidatedBatchPlanAsync(System.Collections.Generic.List<(string FileName, string Content)> specs, string targetLanguage, string jobName);
        Task<ReviewResult> ReviewConsolidatedPlanAsync(System.Collections.Generic.List<(string FileName, string Content)> specs, string planMarkdown, string jobName);
    }

    public class ReviewResult
    {
        public bool HasDefects { get; set; }
        public string? FeedbackComment { get; set; }
    }
}
