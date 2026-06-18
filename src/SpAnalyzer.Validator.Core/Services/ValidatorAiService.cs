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
  ""OverallStatus"": ""MATCH"" | ""MISMATCH"" | ""PARTIAL"" (반드시 세 값 중 하나로만 지정하며, 다른 텍스트 추가 금지),
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

        public async Task<string> GenerateTestParametersAsync(string specContent, string procedureName, CancellationToken cancellationToken = default)
        {
            var systemPrompt = @"당신은 데이터베이스 Stored Procedure의 비즈니스 기능 명세서(*_Spec.md)를 분석하여 데이터 정합성 검증용 입력 테스트 케이스 파라미터 세트를 설계하는 전문 QA 테스트 엔지니어입니다.

명세서에 선언된 입력 파라미터명과 데이터 타입을 분석하여 다음 조건을 충족하는 테스트 케이스 세트를 JSON 형식으로 생성해 주세요:
1. 정상 시나리오 (유효한 값 주입)
2. 경계값 시나리오 (최소/최대 길이, 특이 경계값)
3. 예외/오류 시나리오 (빈 문자열, Null, 잘못된 형식 등)

반드시 다음 JSON 규격으로만 응답해 주십시오. 다른 부가적인 텍스트나 코드 블록 기호는 포함하지 마십시오.

{
  ""ProcedureName"": ""[프로시저 이름]"",
  ""TestCases"": [
    {
      ""CaseId"": ""TC001_Normal_설명"",
      ""Parameters"": {
        ""[파라미터명1]"": ""값1"",
        ""[파라미터명2]"": ""값2""
      }
    }
  ]
}";

            var userPrompt = $@"대상 프로시저: {procedureName}

[비즈니스 기능 명세서]
{specContent}

위 명세서를 토대로 테스트 케이스 입력값 JSON을 작성해 주세요.";

            try
            {
                var response = await _aiClient.ChatAsync(systemPrompt, userPrompt, 0.2f, cancellationToken);
                
                // markdown json 블록 정제
                var cleanJson = response.Trim();
                if (cleanJson.StartsWith("```"))
                {
                    var match = Regex.Match(cleanJson, @"```(?:json)?\s*([\s\S]+?)\s*```");
                    if (match.Success)
                    {
                        cleanJson = match.Groups[1].Value.Trim();
                    }
                }
                return cleanJson;
            }
            catch (Exception ex)
            {
                return $"{{\"ProcedureName\":\"{procedureName}\", \"TestCases\":[], \"Error\":\"AI를 통한 테스트 파라미터 생성 실패: {ex.Message}\"}}";
            }
        }
    }
}
