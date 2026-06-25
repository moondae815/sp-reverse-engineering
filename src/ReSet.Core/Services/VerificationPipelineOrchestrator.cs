using System;
using System.Threading;
using System.Threading.Tasks;
using ReSet.Core.Models;

namespace ReSet.Core.Services
{
    public class VerificationPipelineOrchestrator
    {
        private readonly IDbMetadataService _dbService;
        private readonly IAiService _aiService;
        private readonly MechanicalValidator _validator;
        private readonly IVerificationUserInteraction _userInteraction;
        private readonly int _maxL2Attempts;
        private readonly int _maxAttempts;
        private readonly string _modelName;
        private readonly ICacheManager _cacheManager;

        public VerificationPipelineOrchestrator(
            IDbMetadataService dbService,
            IAiService aiService,
            MechanicalValidator validator,
            IVerificationUserInteraction userInteraction,
            string maxL2Attempts = "1",
            string modelName = "",
            ICacheManager? cacheManager = null)
        {
            _dbService = dbService;
            _aiService = aiService;
            _validator = validator;
            _userInteraction = userInteraction;
            _modelName = modelName;
            _cacheManager = cacheManager ?? new CacheManager();

            if (string.Equals(maxL2Attempts, "unlimited", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(maxL2Attempts, "검증 완료까지", StringComparison.OrdinalIgnoreCase) ||
                maxL2Attempts == "-1")
            {
                _maxL2Attempts = -1;
            }
            else if (int.TryParse(maxL2Attempts, out int parsed))
            {
                _maxL2Attempts = parsed;
            }
            else
            {
                _maxL2Attempts = 1; // 기본값
            }

            _maxAttempts = _maxL2Attempts == -1 ? -1 : 1 + _maxL2Attempts;
        }

        public async Task<(string? SpecMarkdown, SpDefinition? SpDef)> RunPipelineAsync(
            string connectionString,
            string schema,
            string name,
            int maxDepth,
            string provider,
            string instructions,
            bool isBatchMode,
            string outputDirectory = "./output",
            bool enableCache = false,
            CancellationToken cancellationToken = default)
        {
            var selectedOption = $"{schema}.{name}";
            SpDefinition? spDef = null;

            _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - DB 메타데이터 및 의존성 분석 중 (최대 깊이: {maxDepth}단계)...");
            try
            {
                spDef = await _dbService.GetSpDetailsAsync(connectionString, schema, name, maxDepth, cancellationToken);
            }
            catch (Exception ex)
            {
                _userInteraction.NotifyError($"{selectedOption} - DB 조회 실패: {ex.Message}");
            }

            if (spDef == null)
            {
                return (null, null);
            }

            if (spDef.Warnings.Count > 0)
            {
                _userInteraction.NotifyWarnings(selectedOption, spDef.Warnings);
            }

            // 캐시 유효성 확인
            string? compositeHash = null;
            if (enableCache)
            {
                try
                {
                    compositeHash = _cacheManager.ComputeCompositeHash(spDef);
                    if (_cacheManager.IsCacheValid(selectedOption, compositeHash, outputDirectory))
                    {
                        _userInteraction.NotifyStatus($"[green]{selectedOption}[/] - 캐시가 유효합니다. AI 분석을 건너뛰고 기존 보고서를 사용합니다. (Cache Hit)");
                        var specFilePath = System.IO.Path.Combine(outputDirectory, $"{selectedOption}_Spec.md");
                        if (System.IO.File.Exists(specFilePath))
                        {
                            var cachedSpec = await System.IO.File.ReadAllTextAsync(specFilePath, cancellationToken);
                            return (cachedSpec, spDef);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyStatus($"[yellow]경고: 캐시 확인 중 오류가 발생하여 무시하고 분석을 진행합니다. ({ex.Message})[/]");
                }
            }

            var feedbackHistory = new System.Collections.Generic.List<string>();
            string? feedbackLog = null;
            string specificationMarkdown = string.Empty;

            // 설정에 따른 최대 시도 횟수 적용 (N회 또는 검증 완료까지)
            int attempt = 1;
            while (true)
            {
                var attemptText = attempt == 1 ? "1차 분석" : $"자가 수정 보완 ({attempt}회째)";
                bool genSuccess = false;

                _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - AI 리버스 엔지니어링 수행 중 ({provider} - {_modelName}) [[{attemptText}]]...");
                try
                {
                    specificationMarkdown = await _aiService.GenerateSpecificationAsync(spDef, instructions, feedbackLog, cancellationToken);
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
                    _userInteraction.NotifyL1Errors(selectedOption, attempt, _maxAttempts, l1Result.Errors);

                    bool canRetry = _maxAttempts == -1 || attempt < _maxAttempts;
                    if (canRetry)
                    {
                        feedbackLog = l1Result.SuggestedPromptFix;
                        attempt++;
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

                _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - AI 교차 리뷰 분석 중 ({provider} - {_modelName})...");
                try
                {
                    l2Result = await _aiService.ReviewSpecificationAsync(spDef, specificationMarkdown, cancellationToken);
                    reviewSuccess = true;
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyError($"{selectedOption} - AI 교차 리뷰 실패 (시도 {attempt}): {ex.Message}");
                }

                if (reviewSuccess && l2Result != null && l2Result.HasDefects)
                {
                    _userInteraction.NotifyL2Defects(selectedOption, attempt, _maxAttempts, l2Result.FeedbackComment ?? string.Empty);

                    bool canRetry = _maxAttempts == -1 || attempt < _maxAttempts;
                    if (canRetry)
                    {
                        feedbackHistory.Add($"[시도 {attempt} L2 피드백]:\n{l2Result.FeedbackComment}");
                        feedbackLog = "[L2 AI 리뷰 피드백 히스토리]: 다음 누적 결함/누락사항이 지적되었습니다. 이전의 실수를 반복하지 말고 지적 사항을 전면 반영해서 수정해 주십시오.\n\n" + 
                                      string.Join("\n\n", feedbackHistory);
                        attempt++;
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

            // 배치 모드 성공 완료 시 캐시 업데이트
            if (isBatchMode && enableCache && !string.IsNullOrEmpty(compositeHash))
            {
                _cacheManager.UpdateCache(selectedOption, spDef, compositeHash, outputDirectory);
            }

            // L3: 인간 개입형 승인 (TUI 모드 한정)
            if (!isBatchMode)
            {
                while (true)
                {
                    var reviewResult = await _userInteraction.RequestHumanReviewAsync(selectedOption, specificationMarkdown);

                    if (reviewResult.Decision == UserDecision.Approve)
                    {
                        // 최종 승인 시 캐시 업데이트
                        if (enableCache && !string.IsNullOrEmpty(compositeHash))
                        {
                            _cacheManager.UpdateCache(selectedOption, spDef, compositeHash, outputDirectory);
                        }
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
                            reSpec = await _aiService.GenerateSpecificationAsync(spDef, instructions, humanFeedbackLog, cancellationToken);
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
                                reSpec = await _aiService.GenerateSpecificationAsync(spDef, instructions, l1Re.SuggestedPromptFix, cancellationToken);
                            }
                            catch { }
                        }

                        specificationMarkdown = reSpec;
                    }
                }
            }

            return (specificationMarkdown, spDef);
        }

        public async Task<string?> RunConsolidatedPipelineAsync(
            System.Collections.Generic.List<(string FileName, string Content)> specs,
            string targetLanguage,
            string jobName,
            string provider,
            bool isBatchMode = false,
            CancellationToken cancellationToken = default)
        {
            string? feedbackLog = null;
            string consolidatedPlan = string.Empty;

            // 설정에 따른 최대 시도 횟수 적용 (N회 또는 검증 완료까지)
            int attempt = 1;
            while (true)
            {
                var attemptText = attempt == 1 ? "1차 분석" : $"자가 수정 보완 ({attempt}회째)";
                bool genSuccess = false;

                _userInteraction.NotifyStatus($"[yellow]{jobName}[/] - AI 통합 배치 전환 계획 수립 중 ({provider} - {_modelName}) [[{attemptText}]]...");
                try
                {
                    var specsCopy = new System.Collections.Generic.List<(string FileName, string Content)>(specs);
                    if (!string.IsNullOrEmpty(feedbackLog))
                    {
                        specsCopy.Add(("Feedback_Log.txt", $"[이전 시도에 대한 검토 피드백]:\n{feedbackLog}\n위 에러/피드백 사항을 전적으로 수용하여 통합 설계서를 완성해 주세요."));
                    }

                    consolidatedPlan = await _aiService.GenerateConsolidatedBatchPlanAsync(specsCopy, targetLanguage, jobName, cancellationToken);
                    genSuccess = true;
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyError($"{jobName} - AI 통합 계획 생성 실패 (시도 {attempt}): {ex.Message}");
                }

                if (!genSuccess || string.IsNullOrEmpty(consolidatedPlan))
                {
                    return null;
                }

                // L1: 기계적 무결성 검사
                var l1Result = _validator.ValidateConsolidated(consolidatedPlan);
                if (!l1Result.IsValid)
                {
                    _userInteraction.NotifyL1Errors(jobName, attempt, _maxAttempts, l1Result.Errors);

                    bool canRetry = _maxAttempts == -1 || attempt < _maxAttempts;
                    if (canRetry)
                    {
                        feedbackLog = l1Result.SuggestedPromptFix;
                        attempt++;
                        continue;
                    }
                    else
                    {
                        _userInteraction.NotifyError($"{jobName} - [[L1 기계 검증]] 최종 보완 실패. 마지막 작성 버전을 사용합니다.");
                        break;
                    }
                }

                // L2: AI 교차 리뷰
                ReviewResult? l2Result = null;
                bool reviewSuccess = false;

                _userInteraction.NotifyStatus($"[yellow]{jobName}[/] - AI 통합 계획 교차 리뷰 분석 중 ({provider} - {_modelName})...");
                try
                {
                    l2Result = await _aiService.ReviewConsolidatedPlanAsync(specs, consolidatedPlan, jobName, cancellationToken);
                    reviewSuccess = true;
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyError($"{jobName} - AI 교차 리뷰 실패 (시도 {attempt}): {ex.Message}");
                }

                if (reviewSuccess && l2Result != null && l2Result.HasDefects)
                {
                    _userInteraction.NotifyL2Defects(jobName, attempt, _maxAttempts, l2Result.FeedbackComment ?? string.Empty);

                    bool canRetry = _maxAttempts == -1 || attempt < _maxAttempts;
                    if (canRetry)
                    {
                        feedbackLog = $"[L2 AI 리뷰 피드백]: 다음 결함/누락사항이 지적되었습니다. 전면 반영해서 수정해 주십시오.\n{l2Result.FeedbackComment}";
                        attempt++;
                        continue;
                    }
                    else
                    {
                        _userInteraction.NotifyError($"{jobName} - [[L2 AI 리뷰]] 최종 보완 실패. 마지막 리뷰 반영 버전을 사용합니다.");
                        break;
                    }
                }

                // 검증을 통과한 경우 루프 탈출
                if (l1Result.IsValid && (l2Result == null || !l2Result.HasDefects))
                {
                    _userInteraction.NotifyValidationSuccess(jobName);
                    break;
                }
            }

            // L3: 인간 개입형 승인 (TUI 모드 전용, 배치 모드 시 즉시 승인 및 반환)
            if (isBatchMode)
            {
                _userInteraction.NotifyStatus($"[green]{jobName}[/] - 배치 모드로 인해 통합 계획서가 자동으로 최종 승인되었습니다.");
                return consolidatedPlan;
            }

            while (true)
            {
                var reviewResult = await _userInteraction.RequestHumanReviewAsync(jobName, consolidatedPlan);

                if (reviewResult.Decision == UserDecision.Approve)
                {
                    return consolidatedPlan;
                }
                else if (reviewResult.Decision == UserDecision.Cancel)
                {
                    return null;
                }
                else if (reviewResult.Decision == UserDecision.ProvideFeedback)
                {
                    if (string.IsNullOrWhiteSpace(reviewResult.UserFeedback))
                    {
                        continue;
                    }

                    _userInteraction.NotifyStatus($"[yellow]{jobName}[/] - 피드백 반영 재생성 중...");
                    var specsCopy = new System.Collections.Generic.List<(string FileName, string Content)>(specs);
                    specsCopy.Add(("User_Feedback_Log.txt", $"[L3 사용자 보완 피드백 로그]:\n{reviewResult.UserFeedback}\n사용자 의견을 수용하여 설계 내용을 수정 및 보완해 주십시오."));

                    string rePlan = string.Empty;
                    try
                    {
                        rePlan = await _aiService.GenerateConsolidatedBatchPlanAsync(specsCopy, targetLanguage, jobName, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _userInteraction.NotifyError($"피드백 반영 재생성 실패: {ex.Message}");
                    }

                    if (string.IsNullOrEmpty(rePlan))
                    {
                        continue;
                    }

                    // 피드백 반영본에 대한 L1 정적 검사 1회 수행
                    var l1Re = _validator.ValidateConsolidated(rePlan);
                    if (!l1Re.IsValid)
                    {
                        _userInteraction.NotifyStatus("피드백 적용본에서 정적 에러가 검출되어 AI 자가 수정 1회 더 진행합니다.");
                        try
                        {
                            var specsRe = new System.Collections.Generic.List<(string FileName, string Content)>(specsCopy);
                            specsRe.Add(("L1_Re_Fix.txt", l1Re.SuggestedPromptFix ?? string.Empty));
                            rePlan = await _aiService.GenerateConsolidatedBatchPlanAsync(specsRe, targetLanguage, jobName, cancellationToken);
                        }
                        catch { }
                    }

                    consolidatedPlan = rePlan;
                }
            }
        }
    }
}
