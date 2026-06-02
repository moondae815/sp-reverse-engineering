using System;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class VerificationPipelineOrchestrator
    {
        private readonly IDbMetadataService _dbService;
        private readonly IAiService _aiService;
        private readonly MechanicalValidator _validator;
        private readonly IVerificationUserInteraction _userInteraction;

        public VerificationPipelineOrchestrator(
            IDbMetadataService dbService,
            IAiService aiService,
            MechanicalValidator validator,
            IVerificationUserInteraction userInteraction)
        {
            _dbService = dbService;
            _aiService = aiService;
            _validator = validator;
            _userInteraction = userInteraction;
        }

        public async Task<(string? SpecMarkdown, SpDefinition? SpDef)> RunPipelineAsync(
            string connectionString,
            string schema,
            string name,
            int maxDepth,
            string provider,
            string instructions,
            bool isBatchMode)
        {
            var selectedOption = $"{schema}.{name}";
            SpDefinition? spDef = null;

            _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - DB 메타데이터 및 의존성 분석 중 (최대 깊이: {maxDepth}단계)...");
            try
            {
                spDef = await _dbService.GetSpDetailsAsync(connectionString, schema, name, maxDepth);
            }
            catch (Exception ex)
            {
                _userInteraction.NotifyError($"{selectedOption} - DB 조회 실패: {ex.Message}");
            }

            if (spDef == null)
            {
                return (null, null);
            }

            string? feedbackLog = null;
            string specificationMarkdown = string.Empty;

            // 최대 2회 시도 (1차 생성 + L1/L2 오류 시 1회 자가 보완)
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var attemptText = attempt == 1 ? "1차 분석" : "자가 수정 보완";
                bool genSuccess = false;

                _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - AI 리버스 엔지니어링 수행 중 ({provider}) [[{attemptText}]]...");
                try
                {
                    specificationMarkdown = await _aiService.GenerateSpecificationAsync(spDef, instructions, feedbackLog);
                    genSuccess = true;
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyError($"{selectedOption} - AI 분석 실패 (시도 {attempt}): {ex.Message}");
                }

                if (!genSuccess || string.IsNullOrEmpty(specificationMarkdown))
                {
                    return (null, spDef);
                }

                // L1: 기계적 무결성 검사
                var l1Result = _validator.Validate(specificationMarkdown);
                if (!l1Result.IsValid)
                {
                    _userInteraction.NotifyL1Errors(selectedOption, attempt, l1Result.Errors);

                    if (attempt < 2)
                    {
                        feedbackLog = l1Result.SuggestedPromptFix;
                        continue;
                    }
                    else
                    {
                        _userInteraction.NotifyError($"{selectedOption} - [[L1 기계 검증]] 최종 보완 실패. 마지막 작성 버전을 사용합니다.");
                        break;
                    }
                }

                // L2: AI 교차 리뷰
                ReviewResult? l2Result = null;
                bool reviewSuccess = false;

                _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - AI 교차 리뷰 분석 중 ({provider})...");
                try
                {
                    l2Result = await _aiService.ReviewSpecificationAsync(spDef, specificationMarkdown);
                    reviewSuccess = true;
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyError($"{selectedOption} - AI 교차 리뷰 실패 (시도 {attempt}): {ex.Message}");
                }

                if (reviewSuccess && l2Result != null && l2Result.HasDefects)
                {
                    _userInteraction.NotifyL2Defects(selectedOption, attempt, l2Result.FeedbackComment ?? string.Empty);

                    if (attempt < 2)
                    {
                        feedbackLog = $"[L2 AI 리뷰 피드백]: 다음 결함/누락사항이 지적되었습니다. 전면 반영해서 수정해 주십시오.\n{l2Result.FeedbackComment}";
                        continue;
                    }
                    else
                    {
                        _userInteraction.NotifyError($"{selectedOption} - [[L2 AI 리뷰]] 최종 보완 실패. 마지막 리뷰 반영 버전을 사용합니다.");
                        break;
                    }
                }

                // 검증을 통과한 경우 루프 탈출
                if (l1Result.IsValid && (l2Result == null || !l2Result.HasDefects))
                {
                    _userInteraction.NotifyValidationSuccess(selectedOption);
                    break;
                }
            }

            // L3: 인간 개입형 승인 (TUI 모드 한정)
            if (!isBatchMode)
            {
                while (true)
                {
                    var reviewResult = await _userInteraction.RequestHumanReviewAsync(selectedOption, specificationMarkdown);

                    if (reviewResult.Decision == UserDecision.Approve)
                    {
                        return (specificationMarkdown, spDef);
                    }
                    else if (reviewResult.Decision == UserDecision.Cancel)
                    {
                        return (null, spDef);
                    }
                    else if (reviewResult.Decision == UserDecision.ProvideFeedback)
                    {
                        if (string.IsNullOrWhiteSpace(reviewResult.UserFeedback))
                        {
                            continue;
                        }

                        _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - 피드백 반영 재생성 중...");
                        var humanFeedbackLog = $"[L3 사용자 보완 피드백 로그]:\n{reviewResult.UserFeedback}";

                        string reSpec = string.Empty;
                        try
                        {
                            reSpec = await _aiService.GenerateSpecificationAsync(spDef, instructions, humanFeedbackLog);
                        }
                        catch (Exception ex)
                        {
                            _userInteraction.NotifyError($"피드백 반영 재생성 실패: {ex.Message}");
                        }

                        if (string.IsNullOrEmpty(reSpec))
                        {
                            continue;
                        }

                        // 피드백 반영본에 대한 L1 정적 검사 1회 수행
                        var l1Re = _validator.Validate(reSpec);
                        if (!l1Re.IsValid)
                        {
                            _userInteraction.NotifyStatus("피드백 적용본에서 정적 에러가 검출되어 AI 자가 수정 1회 더 진행합니다.");
                            try
                            {
                                reSpec = await _aiService.GenerateSpecificationAsync(spDef, instructions, l1Re.SuggestedPromptFix);
                            }
                            catch { }
                        }

                        specificationMarkdown = reSpec;
                    }
                }
            }

            return (specificationMarkdown, spDef);
        }
    }
}
