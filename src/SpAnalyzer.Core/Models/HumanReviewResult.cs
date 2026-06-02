namespace SpAnalyzer.Core.Models
{
    public enum UserDecision
    {
        Approve,          // 승인 및 최종 저장 (Approve)
        ProvideFeedback,   // 추가 보완 요청 피드백 입력 (Feedback)
        Cancel            // 저장 없이 이탈 (Cancel)
    }

    public class HumanReviewResult
    {
        public UserDecision Decision { get; set; }
        public string? UserFeedback { get; set; }
    }
}
