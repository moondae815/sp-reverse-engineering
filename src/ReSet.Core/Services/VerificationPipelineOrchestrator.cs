using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
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
        private readonly IAiService _criticService;
        private readonly IAiService _consolidatorService;
        private readonly string? _actorEffort;
        private readonly string? _criticEffort;
        private readonly string? _consolidatorEffort;

        public VerificationPipelineOrchestrator(
            IDbMetadataService dbService,
            IAiService aiService,
            MechanicalValidator validator,
            IVerificationUserInteraction userInteraction,
            string maxL2Attempts = "1",
            string modelName = "",
            ICacheManager? cacheManager = null,
            IAiService? criticService = null,
            IAiService? consolidatorService = null,
            string? actorEffort = null,
            string? criticEffort = null,
            string? consolidatorEffort = null)
        {
            _dbService = dbService;
            _aiService = aiService;
            _validator = validator;
            _userInteraction = userInteraction;
            _modelName = modelName;
            _cacheManager = cacheManager ?? new CacheManager();
            _criticService = criticService ?? aiService;
            _consolidatorService = consolidatorService ?? aiService;
            _actorEffort = actorEffort;
            _criticEffort = criticEffort;
            _consolidatorEffort = consolidatorEffort;

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

            if (string.Equals(_actorEffort, "dynamic", StringComparison.OrdinalIgnoreCase))
            {
                _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - 하이브리드 다중 후보군(Low/Medium/High Effort) 병렬 생성 중...");
                
                var tasks = new System.Collections.Generic.List<Task<string>>();
                tasks.Add(_aiService.GenerateSpecificationAsync(spDef, instructions, feedbackLog, "low", cancellationToken));
                tasks.Add(_aiService.GenerateSpecificationAsync(spDef, instructions, feedbackLog, "medium", cancellationToken));
                tasks.Add(_aiService.GenerateSpecificationAsync(spDef, instructions, feedbackLog, "high", cancellationToken));

                string[] candidates;
                try
                {
                    candidates = await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyError($"{selectedOption} - 하이브리드 후보 생성 중 실패: {ex.Message}");
                    return (null, spDef);
                }

                // 각 후보에 대한 L2 검증 및 채점 수행
                var reviewTasks = new System.Collections.Generic.List<Task<ReviewResult>>();
                for (int i = 0; i < candidates.Length; i++)
                {
                    reviewTasks.Add(_criticService.ReviewSpecificationAsync(spDef, candidates[i], _criticEffort, cancellationToken));
                }

                ReviewResult[] reviews;
                try
                {
                    reviews = await Task.WhenAll(reviewTasks);
                }
                catch (Exception ex)
                {
                    _userInteraction.NotifyError($"{selectedOption} - Critic 검토 중 실패: {ex.Message}");
                    return (null, spDef);
                }

                // 완벽한 후보(L1 & L2 무결) 발견 시 Fast-pass 즉시 채택
                bool fastPassTriggered = false;
                for (int i = 0; i < candidates.Length; i++)
                {
                    var l1Check = _validator.Validate(candidates[i]);
                    if (l1Check.IsValid && !reviews[i].HasDefects)
                    {
                        _userInteraction.NotifyStatus($"[green]{selectedOption}[/] - 완벽한 후보군(후보 {i+1})이 발견되어 즉시 채택합니다.");
                        specificationMarkdown = candidates[i];
                        fastPassTriggered = true;
                        break;
                    }
                }

                if (!fastPassTriggered)
                {
                    // 영역별 합성 가이드 및 프롬프트 조립
                    var sbConsolidation = new StringBuilder();
                    sbConsolidation.AppendLine("당신은 제공된 여러 개의 Stored Procedure 분석 명세서 후보를 종합하여, 각 후보의 우수 영역을 취합하고 결점을 개선하여 단일한 완벽한 명세서로 합성(Consolidation)하는 전문 조립 아키텍트입니다.");
                    sbConsolidation.AppendLine();
                    sbConsolidation.AppendLine("[제공된 명세서 후보 목록]");
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        sbConsolidation.AppendLine($"--- [후보 {i+1}] ---");
                        sbConsolidation.AppendLine(candidates[i]);
                        sbConsolidation.AppendLine($"[후보 {i+1}에 대한 Critic 결함 피드백]: {reviews[i].FeedbackComment ?? "결함 없음"}");
                        sbConsolidation.AppendLine();
                    }
                    sbConsolidation.AppendLine();
                    sbConsolidation.AppendLine("[합성 및 병합 지침]");
                    sbConsolidation.AppendLine("1. 각 후보에서 가장 잘 작성된 섹션(Mermaid 다이어그램, CRUD 표, 매핑 정보 등)을 엄선하여 조화롭게 병합하십시오.");
                    sbConsolidation.AppendLine("2. Critic 피드백으로 지적된 모든 결함 사항을 최종 명세서에서 완전히 수정하십시오.");
                    sbConsolidation.AppendLine("3. 5대 필수 대분류 헤더 명칭(## 개요, ## 파라미터 목록, ## CRUD 분석, ## 로직 흐름 요약, ## 비즈니스 흐름 시각화)을 그대로 사용하여 문서를 구성하십시오.");
                    sbConsolidation.AppendLine("4. 최종 결과물만 다듬어 마크다운으로 직접 출력하십시오. 추가적인 사족이나 인사말은 절대 포함하지 마십시오.");

                    _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - 이종 모델 합성 에이전트(Consolidator) 구동 중 ({_consolidatorEffort ?? "medium"} effort)...");
                    try
                    {
                        specificationMarkdown = await _consolidatorService.GenerateSpecificationAsync(spDef, sbConsolidation.ToString(), null, _consolidatorEffort ?? "medium", cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _userInteraction.NotifyError($"{selectedOption} - 최종 합성 생성 실패: {ex.Message}");
                        return (null, spDef);
                    }

                    // 합성본 기계적 검증 (L1) 1회 수행
                    var finalL1 = _validator.Validate(specificationMarkdown);
                    if (!finalL1.IsValid)
                    {
                        _userInteraction.NotifyStatus("합성본에서 정적 에러가 검출되어 AI 자가 수정 1회 진행합니다.");
                        try
                        {
                            specificationMarkdown = await _consolidatorService.GenerateSpecificationAsync(spDef, sbConsolidation.ToString(), finalL1.SuggestedPromptFix, _consolidatorEffort ?? "medium", cancellationToken);
                        }
                        catch { }
                    }

                    _userInteraction.NotifyValidationSuccess(selectedOption);
                }
            }
            else
            {
                // 기존 단일 생성 루프
                int attempt = 1;
                while (true)
                {
                    var attemptText = attempt == 1 ? "1차 분석" : $"자가 수정 보완 ({attempt}회째)";
                    bool genSuccess = false;

                    _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - AI 리버스 엔지니어링 수행 중 ({provider} - {_modelName}) [[{attemptText}]]...");
                    try
                    {
                        specificationMarkdown = await _aiService.GenerateSpecificationAsync(spDef, instructions, feedbackLog, effort: null, cancellationToken: cancellationToken);
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
                        l2Result = await _criticService.ReviewSpecificationAsync(spDef, specificationMarkdown, _criticEffort, cancellationToken);
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
            }

            // 배치 모드 성공 완료 시 캐시 업데이트
            if (isBatchMode && enableCache && !string.IsNullOrEmpty(compositeHash))
            {
                _cacheManager.UpdateCache(selectedOption, spDef, compositeHash, outputDirectory);
            }

            // DB 역반영 여부 선택과 관계없이 항상 파일로 스크립트 저장
            ExportMetadataCleansingSql(specificationMarkdown, selectedOption, outputDirectory);

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

                        // 사용자가 승인하면 DB 역반영 동기화 수행
                        var syncApproved = await _userInteraction.ConfirmMetadataSyncAsync(selectedOption);
                        if (syncApproved)
                        {
                            await ApplyMetadataCleansingSqlAsync(connectionString, selectedOption, outputDirectory, cancellationToken);
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
                            reSpec = await _aiService.GenerateSpecificationAsync(spDef, instructions, humanFeedbackLog, effort: null, cancellationToken: cancellationToken);
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
                                reSpec = await _aiService.GenerateSpecificationAsync(spDef, instructions, l1Re.SuggestedPromptFix, effort: null, cancellationToken: cancellationToken);
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

                    consolidatedPlan = await _consolidatorService.GenerateConsolidatedBatchPlanAsync(specsCopy, targetLanguage, jobName, _consolidatorEffort, cancellationToken);
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
                    l2Result = await _criticService.ReviewConsolidatedPlanAsync(specs, consolidatedPlan, jobName, _criticEffort, cancellationToken);
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
                        rePlan = await _consolidatorService.GenerateConsolidatedBatchPlanAsync(specsCopy, targetLanguage, jobName, _consolidatorEffort, cancellationToken);
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
                            rePlan = await _consolidatorService.GenerateConsolidatedBatchPlanAsync(specsRe, targetLanguage, jobName, _consolidatorEffort, cancellationToken);
                        }
                        catch { }
                    }

                    consolidatedPlan = rePlan;
                }
            }
        }

        private void ExportMetadataCleansingSql(string specificationMarkdown, string selectedOption, string outputDirectory)
        {
            if (string.IsNullOrEmpty(specificationMarkdown)) return;

            // 정규식을 사용하여 [AI 추론 보완: Schema.Table.Column - 설명] 패턴 추출
            var regex = new System.Text.RegularExpressions.Regex(@"\[AI 추론 보완:\s*([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)\s*-\s*([^\]]+)\]");
            var matches = regex.Matches(specificationMarkdown);

            if (matches.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("-- ==========================================================================");
            sb.AppendLine($"-- AI Generated Metadata Cleansing Script for {selectedOption}");
            sb.AppendLine($"-- Created At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("-- ==========================================================================");
            sb.AppendLine();

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var schema = match.Groups[1].Value;
                var table = match.Groups[2].Value;
                var column = match.Groups[3].Value;
                var value = match.Groups[4].Value.Trim();

                sb.AppendLine($"-- Column: {schema}.{table}.{column}");
                sb.AppendLine($"IF NOT EXISTS (");
                sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
                sb.AppendLine($"    INNER JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id");
                sb.AppendLine($"    INNER JOIN sys.objects o ON c.object_id = o.object_id");
                sb.AppendLine($"    INNER JOIN sys.schemas s ON o.schema_id = s.schema_id");
                sb.AppendLine($"    WHERE s.name = '{schema}' AND o.name = '{table}' AND c.name = '{column}' AND ep.name = 'MS_Description'");
                sb.AppendLine($")");
                sb.AppendLine($"BEGIN");
                sb.AppendLine($"    EXEC sp_addextendedproperty ");
                sb.AppendLine($"         @name = N'MS_Description', @value = N'{value.Replace("'", "''")}',");
                sb.AppendLine($"         @level0type = N'SCHEMA', @level0name = '{schema}',");
                sb.AppendLine($"         @level1type = N'TABLE',  @level1name = '{table}',");
                sb.AppendLine($"         @level2type = N'COLUMN', @level2name = '{column}';");
                sb.AppendLine($"END");
                sb.AppendLine($"ELSE");
                sb.AppendLine($"BEGIN");
                sb.AppendLine($"    EXEC sp_updateextendedproperty ");
                sb.AppendLine($"         @name = N'MS_Description', @value = N'{value.Replace("'", "''")}',");
                sb.AppendLine($"         @level0type = N'SCHEMA', @level0name = '{schema}',");
                sb.AppendLine($"         @level1type = N'TABLE',  @level1name = '{table}',");
                sb.AppendLine($"         @level2type = N'COLUMN', @level2name = '{column}';");
                sb.AppendLine($"END");
                sb.AppendLine($"GO");
                sb.AppendLine();
            }

            try
            {
                var cleansingDir = System.IO.Path.Combine(outputDirectory, "cleansing");
                if (!System.IO.Directory.Exists(cleansingDir))
                {
                    System.IO.Directory.CreateDirectory(cleansingDir);
                }

                var sqlPath = System.IO.Path.Combine(cleansingDir, $"{selectedOption}_MetadataCleansing.sql");
                System.IO.File.WriteAllText(sqlPath, sb.ToString(), System.Text.Encoding.UTF8);
                _userInteraction.NotifyStatus($"[green]{selectedOption}[/] - 메타데이터 보완 SQL 스크립트가 저장되었습니다: [blue]{sqlPath}[/]");
            }
            catch (Exception ex)
            {
                _userInteraction.NotifyError($"메타데이터 보완 스크립트 저장 중 오류 발생: {ex.Message}");
            }
        }

        private async Task ApplyMetadataCleansingSqlAsync(string connectionString, string selectedOption, string outputDirectory, CancellationToken cancellationToken)
        {
            var sqlPath = System.IO.Path.Combine(outputDirectory, "cleansing", $"{selectedOption}_MetadataCleansing.sql");
            if (!System.IO.File.Exists(sqlPath)) return;

            try
            {
                var sqlText = await System.IO.File.ReadAllTextAsync(sqlPath, cancellationToken);
                if (string.IsNullOrWhiteSpace(sqlText)) return;

                _userInteraction.NotifyStatus($"[yellow]{selectedOption}[/] - DB 메타데이터 설명 역반영 중...");

                var batches = sqlText.Split(new[] { "GO\r\n", "GO\n", "go\r\n", "go\n" }, StringSplitOptions.RemoveEmptyEntries);

                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync(cancellationToken);
                    foreach (var batch in batches)
                    {
                        var cleanBatch = batch.Trim();
                        if (string.IsNullOrEmpty(cleanBatch)) continue;

                        using (var cmd = new SqlCommand(cleanBatch, conn))
                        {
                            await cmd.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                }
                _userInteraction.NotifyStatus($"[green]{selectedOption}[/] - DB 메타데이터 설명 역반영 완료!");
            }
            catch (Exception ex)
            {
                _userInteraction.NotifyError($"DB 메타데이터 설명 역반영 중 오류 발생: {ex.Message}");
            }
        }
    }
}
