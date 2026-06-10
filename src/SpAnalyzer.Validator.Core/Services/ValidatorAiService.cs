using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpAnalyzer.Core.Services;
using SpAnalyzer.Validator.Core.Models;

namespace SpAnalyzer.Validator.Core.Services
{
    public class ValidatorAiService
    {
        private readonly IAiClient _aiClient;

        public ValidatorAiService(IAiClient aiClient)
        {
            _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        }

        public async Task<GapReport> VerifyCodeAsync(string specContent, string sourceCodeContent, string targetLanguage, CancellationToken cancellationToken = default)
        {
            var systemPrompt = @"당신은 데이터베이스 Stored Procedure 역공학 명세서(*_Spec.md)와 이를 마이그레이션하여 구현한 프로그램 코드(C# 또는 Java)를 일대일 비교하여 기능적으로 완벽히 동일하게 구현되었는지 정밀 검증하는 전문 QA 에이전트입니다.

비교 검증 시 다음 항목들에 주목하십시오:
1. 입력 파라미터 매핑: 설계서에 명시된 파라미터들이 코드의 입력 인자나 객체 필드로 정확히 전달되는가?
2. 출력 데이터셋/반환값: 쿼리 조회 결과나 DTO 반환 필드가 누락 없이 매핑되는가?
3. 핵심 비즈니스 로직: 조건문 분기, 연산 로직, 주요 쿼리 실행 등이 설계서와 의미론적으로 완벽히 동일한가?
4. 예외 처리 및 트랜잭션: 오류 제어 구조 및 트랜잭션 제어 여부가 설계 사양서와 부합하는가?

당신의 분석 결과는 반드시 다음 JSON 형식으로만 응답해야 합니다. 다른 텍스트나 서론, 결론은 절대 포함하지 마십시오.

{
  ""OverallStatus"": ""MATCH"" | ""MISMATCH"" | ""PARTIAL"",
  ""InputParametersGap"": ""입력 인자 불일치 내용 기술 (없으면 빈 문자열)"",
  ""OutputResultSetsGap"": ""출력 컬럼/DTO 필드 불일치 내용 기술 (없으면 빈 문자열)"",
  ""BusinessLogicGap"": ""비즈니스 로직 및 쿼리 조건 불일치 내용 기술 (없으면 빈 문자열)"",
  ""ExceptionHandlingGap"": ""예외 및 트랜잭션 처리 불일치 내용 기술 (없으면 빈 문자열)"",
  ""Suggestions"": ""불일치 해결을 위한 구체적인 코드 수정 가이드라인""
}";

            var userPrompt = $@"검증 대상 언어: {targetLanguage}

[비즈니스 기능 명세서]
{specContent}

[마이그레이션된 소스 코드]
{sourceCodeContent}

위 명세서와 소스 코드를 면밀히 비교하여 JSON 결과를 작성해 주세요.";

            try
            {
                var response = await _aiClient.ChatAsync(systemPrompt, userPrompt, 0.1f, cancellationToken);
                return ParseGapReport(response);
            }
            catch (Exception ex)
            {
                return new GapReport
                {
                    OverallStatus = "MISMATCH",
                    Suggestions = $"AI 분석 수행 중 예외가 발생했습니다: {ex.Message}"
                };
            }
        }

        private GapReport ParseGapReport(string rawResponse)
        {
            try
            {
                // markdown json 블록 정제 (예: ```json ... ```)
                var cleanJson = rawResponse.Trim();
                if (cleanJson.StartsWith("```"))
                {
                    var match = Regex.Match(cleanJson, @"```(?:json)?\s*([\s\S]+?)\s*```");
                    if (match.Success)
                    {
                        cleanJson = match.Groups[1].Value.Trim();
                    }
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var report = JsonSerializer.Deserialize<GapReport>(cleanJson, options);
                return report ?? new GapReport
                {
                    OverallStatus = "MISMATCH",
                    Suggestions = "AI의 응답 역직렬화 결과가 null입니다."
                };
            }
            catch (Exception ex)
            {
                return new GapReport
                {
                    OverallStatus = "MISMATCH",
                    Suggestions = $"AI 응답 파싱 실패. 원본 응답:\n{rawResponse}\n오류: {ex.Message}"
                };
            }
        }
    }
}
