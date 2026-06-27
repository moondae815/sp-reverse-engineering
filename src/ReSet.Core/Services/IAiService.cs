using System.Threading;
using System.Threading.Tasks;
using ReSet.Core.Models;

namespace ReSet.Core.Services
{
    public interface IAiService
    {
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions, string? feedbackLog = null, string? effort = null, CancellationToken cancellationToken = default);
        Task<ReviewResult> ReviewSpecificationAsync(SpDefinition spDef, string specMarkdown, string? effort = null, CancellationToken cancellationToken = default);
        Task<string> GenerateBatchMigrationPlanAsync(SpDefinition spDef, string targetLanguage, CancellationToken cancellationToken = default);
        Task<string> GenerateConsolidatedBatchPlanAsync(System.Collections.Generic.List<(string FileName, string Content)> specs, string targetLanguage, string jobName, string? effort = null, CancellationToken cancellationToken = default);
        Task<ReviewResult> ReviewConsolidatedPlanAsync(System.Collections.Generic.List<(string FileName, string Content)> specs, string planMarkdown, string jobName, string? effort = null, CancellationToken cancellationToken = default);
        Task<string> GenerateSettlementPolicyRulebookAsync(System.Collections.Generic.List<SpDefinition> spDefs, string profilingDataJson, CancellationToken cancellationToken = default);
    }

    public class ReviewResult
    {
        public bool HasDefects { get; set; }
        public string? FeedbackComment { get; set; }
        
        // 4대 기준별 정량적 평가 점수 (각 0~10점)
        public int ScoreAccuracy { get; set; }     // 비즈니스 정합성
        public int ScoreCrud { get; set; }         // CRUD 및 데이터 매핑
        public int ScoreReadability { get; set; }  // 다이어그램 가독성
        public int ScoreException { get; set; }    // 예외 및 트랜잭션

        // 종합 점수 계산 (40점 만점)
        public int TotalScore => ScoreAccuracy + ScoreCrud + ScoreReadability + ScoreException;

        // 100점 만점 환산 점수
        public int NormalizedScore => (int)System.Math.Round((TotalScore * 100.0) / 40.0);
    }
}
