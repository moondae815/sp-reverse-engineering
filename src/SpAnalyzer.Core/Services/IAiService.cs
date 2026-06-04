using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IAiService
    {
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions, string? feedbackLog = null);
        Task<ReviewResult> ReviewSpecificationAsync(SpDefinition spDef, string specMarkdown);
        Task<string> GenerateBatchMigrationPlanAsync(SpDefinition spDef, string targetLanguage);
    }

    public class ReviewResult
    {
        public bool HasDefects { get; set; }
        public string? FeedbackComment { get; set; }
    }
}
