using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class AiService : IAiService
    {
        private readonly IAiClient _aiClient;
        private readonly float _temperature;

        public AiService(IAiClient aiClient, float temperature)
        {
            _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
            _temperature = temperature;
        }

        private string FormatTableSchemaToMarkdown(DependencyInfo dep)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"### 테이블: {dep.Schema}.{dep.Name} ({dep.Type}) - 발견 깊이: {dep.DiscoveryDepth}단계");
            if (!string.IsNullOrEmpty(dep.Description))
            {
                sb.AppendLine($"* 테이블 설명: {dep.Description}");
            }
            sb.AppendLine("| 컬럼명 | 데이터 타입 | Null 허용 | 제약 조건 | 설명 |");
            sb.AppendLine("| :--- | :--- | :---: | :--- | :--- |");
            
            foreach (var col in dep.Columns)
            {
                var constraints = new System.Collections.Generic.List<string>();
                if (col.IsPrimaryKey) constraints.Add("PRIMARY KEY");
                if (col.IsForeignKey) constraints.Add("FOREIGN KEY");
                
                var constraintStr = string.Join(", ", constraints);
                var nullableStr = col.IsNullable ? "Yes" : "No";
                
                sb.AppendLine($"| {col.ColumnName} | {col.DataType} | {nullableStr} | {constraintStr} | {col.Description} |");
            }
            
            return sb.ToString();
        }

        public async Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions, string? feedbackLog = null, CancellationToken cancellationToken = default)
        {
            // 프롬프트 조립
            var systemPrompt = $@"당신은 SQL Server Stored Procedure 분석 전문가입니다. 다음 규칙을 준수하여 마크다운 기능 명세서를 작성하십시오.

[분석 추가 규칙]
1. 분석 대상 SP 뿐만 아니라 제공된 참조 테이블 스키마 컬럼 정보 및 참조 UDF/SP 소스코드를 모두 참고하여 분석 보고서를 한글로 성실히 작성하십시오.
2. SP 내부에서 참조 테이블의 어떤 컬럼 값을 제어/수정하고 조건식에 쓰는지 파라미터 구조와 매핑하여 작성하십시오.
3. SP에서 호출하는 사용자 정의 함수(UDF)의 연산 알고리즘을 소스코드를 보고 분석하여 비즈니스 로직 요약에 포함시키십시오.
4. 비즈니스 흐름을 직관적으로 이해할 수 있는 Mermaid Flowchart 다이어그램을 필수로 포함해 마크다운으로 구성해 주십시오. 노드 텍스트에 특수문자나 괄호가 들어가 에러가 발생하지 않도록 큰따옴표("")로 감싸 문법을 엄격히 준수하십시오.
5. SP 내에 동적 SQL(예: EXEC, EXECUTE, sp_executesql을 통한 문자열 쿼리 실행)이 존재하는 경우, 동적으로 구성되어 실행되는 SQL의 목적과 대상 테이블을 코드 흐름 상에서 최대한 식별하여 CRUD 분석 및 비즈니스 로직 요약에 누락 없이 반영하십시오.
6. SP 내에서 Linked Server를 통한 원격 참조(4파트 식별자: Server.Database.Schema.Table 형식을 사용하는 참조)가 발견되면, 해당 외부 DB/테이블 의존성과 데이터 연동 목적을 명확히 분석하여 포함하십시오.
7. 문서 작성이 완료되면 추가 지원 제안, 인사말, 또는 향후 추가 분석 가능성에 대한 설명 등 본문 요건과 관련 없는 사족이나 안내 문구를 문서 끝에 절대 작성하지 마십시오. 문서의 정해진 필수 섹션 작성이 끝나는 즉시 깔끔하게 출력을 마쳐야 합니다.

[사용자 지침]
{userInstructions}";
            
            // 단순 의존 객체 목록
            var dependenciesText = new StringBuilder();
            // 테이블 상세 스키마 마크다운
            var tableSchemasText = new StringBuilder();
            // UDF/SP 참조 코드 블록
            var referenceDdlsText = new StringBuilder();

            foreach (var dep in spDef.Dependencies)
            {
                dependenciesText.AppendLine($"- Schema: {dep.Schema}, Name: {dep.Name}, Type: {dep.Type} (발견 깊이: {dep.DiscoveryDepth}단계)");
                
                if (dep.Columns.Count > 0)
                {
                    tableSchemasText.AppendLine(FormatTableSchemaToMarkdown(dep));
                    tableSchemasText.AppendLine();
                }

                if (!string.IsNullOrEmpty(dep.ReferencedDdlText))
                {
                    referenceDdlsText.AppendLine($"### 객체: {dep.Schema}.{dep.Name} ({dep.Type}) - 발견 깊이: {dep.DiscoveryDepth}단계");
                    referenceDdlsText.AppendLine("```sql");
                    referenceDdlsText.AppendLine(dep.ReferencedDdlText);
                    referenceDdlsText.AppendLine("```");
                    referenceDdlsText.AppendLine();
                }
            }

            var userPrompt = $@"
분석 대상 Stored Procedure 정보:
- Schema: {spDef.Schema}
- Name: {spDef.Name}

[DB에서 추출된 기계적 의존 관계 목록]
{dependenciesText}

[의존하는 참조 테이블 상세 스키마 정보 (Markdown Tables)]
{tableSchemasText}

[의존하는 참조 함수 및 Stored Procedure 정의 DDL 코드 목록]
{referenceDdlsText}

[Stored Procedure DDL SQL 원본]
```sql
{spDef.DdlText}
```

위의 모든 참조 정보와 원본 코드를 자세히 리버스 엔지니어링하여 지침에 맞게 마크다운 형식의 기능 명세서를 완성하십시오.
";

            if (!string.IsNullOrEmpty(feedbackLog))
            {
                userPrompt += $"\n\n[이전 시도에 대한 검증 오류/수정 피드백 로그]:\n{feedbackLog}\n위 검토 및 수정 의견을 전적으로 수용하여 명세서 내용을 정교하게 수정하고 오류를 바로잡아 다시 작성해 주십시오.";
            }

            return await _aiClient.ChatAsync(systemPrompt, userPrompt, _temperature, cancellationToken);
        }

        public async Task<ReviewResult> ReviewSpecificationAsync(SpDefinition spDef, string specMarkdown, CancellationToken cancellationToken = default)
        {
            var systemPrompt = @"당신은 SQL Server Stored Procedure 기능 명세서의 완성도를 검증하는 수석 아키텍트이자 리뷰어 에이전트입니다.
제시된 기능 명세서(Markdown)가 제공된 Stored Procedure 원본 및 메타데이터 정보를 충실히 반영하여 왜곡 없이 잘 작성되었는지 엄격하게 검증하십시오.

[검토 기준]
1. 명세서에 필수 헤더(개요, 파라미터 목록, CRUD 분석, 로직 흐름 요약, 비즈니스 흐름 시각화)가 다 존재하며 알맞은 내용을 담고 있는가?
2. 원본 DDL 소스코드와 명세서의 비즈니스 로직(특히 중요 제어 흐름이나 트랜잭션 처리) 사이에 사실과 다르거나 심각한 환각(왜곡)이 존재하지 않는가?
3. 참조 UDF 및 하위 프로시저 연산 알고리즘 분석이 정상 반영되었는가?

[답변 작성 형식]
반드시 아래 JSON 형식으로만 최종 답변을 출력해야 합니다. JSON 코드 블록 없이 순수 JSON만 반환해야 합니다:
{
  ""HasDefects"": true 또는 false,
  ""FeedbackComment"": ""결함이 있는 경우 무엇이 누락되었거나 어떻게 수정해야 하는지 구체적인 피드백 내용 기술 (HasDefects가 false인 경우 빈 문자열 '' 반환)""
}";

            var userPrompt = $@"
[원본 Stored Procedure DDL SQL]
```sql
{spDef.DdlText}
```

[작성된 기능 명세서 마크다운]
{specMarkdown}

위 마크다운 명세서의 완결성 및 정확성을 검토 기준에 맞게 성실히 분석한 뒤 JSON 포맷으로 답해주십시오.
";

            var responseContent = await _aiClient.ChatAsync(systemPrompt, userPrompt, 0.1f, cancellationToken);
            try
            {
                var jsonString = ExtractJson(responseContent);

                using (var resultDoc = JsonDocument.Parse(jsonString))
                {
                    var resultRoot = resultDoc.RootElement;
                    var hasDefects = resultRoot.GetProperty("HasDefects").GetBoolean();
                    var feedbackComment = resultRoot.TryGetProperty("FeedbackComment", out var commentProp) ? commentProp.GetString() : null;

                    return new ReviewResult
                    {
                        HasDefects = hasDefects,
                        FeedbackComment = feedbackComment
                    };
                }
            }
            catch (Exception ex)
            {
                return new ReviewResult
                {
                    HasDefects = true,
                    FeedbackComment = $"JSON 검토 보고서 파싱 실패: {ex.Message}"
                };
            }
        }

        public async Task<string> GenerateBatchMigrationPlanAsync(SpDefinition spDef, string targetLanguage, CancellationToken cancellationToken = default)
        {
            var systemPrompt = $@"당신은 SQL Server Agent의 스케줄러 배치 작업을 현대적인 애플리케이션 기반 배치 프레임워크로 전환하는 최적화 설계 전문가입니다.
대상 Stored Procedure 소스 코드와 의존 테이블/UDF 구조를 분석하여, {targetLanguage} 기반의 현대적인 백그라운드 배치 컴포넌트로 포팅하기 위한 '배치 전환 계획 설계서'를 작성해 주십시오.

[설계서 작성 규칙 및 내용 필수 조건]
1. 문서는 한글 마크다운 양식으로 작성하십시오.
2. **배치 전환 아키텍처 개요**: SQL Server Agent Job 역할을 대체할 신규 스케줄러 프레임워크 제안 (예: C#인 경우 Quartz.NET / Hangfire 기반 Worker Service, Java인 경우 Spring Batch + Quartz).
3. **대량 데이터 청크(Chunk) 처리 전략**: OOM 방지를 위한 Paging Reader 패턴 및 벌크 연산(Bulk Write) 가이드라인 제안.
4. **비즈니스 전환 설계 및 의사코드(Pseudocode)**: SP 내부의 주요 비즈니스 로직(분기, 루프, 데이터 처리 등)을 {targetLanguage}의 OOP 문법 및 ORM(EF Core / JPA)으로 전환하는 구체적 의사코드(코드 구조 예시) 제공.
   - 특히 SP 내에 동적 SQL(EXEC/sp_executesql 등)이 사용된 경우, 이를 컴파일 타임에 검증 가능하며 SQL 인젝션 위험이 없는 {targetLanguage}의 안전한 쿼리 빌더나 파라미터화된 ORM 쿼리 또는 조건부 분기 로직으로 안전하게 포팅하기 위한 가이드를 제시하십시오.
   - Linked Server 참조(4파트 식별자)가 사용된 경우, 멀티 데이터소스 구성, 분산 트랜잭션 처리(필요 시), 또는 API/DB Link 대체 인터페이스 설계 등 {targetLanguage} 환경에 맞춘 구체적인 연동 설계 방향을 제시하십시오.
5. **로깅 및 실패 조치 계획**: 기존 TRY...CATCH 에러 로깅을 구조화된 로그(Serilog 등)로 전환하고 알림(Slack 등) 발송 방안 매핑.
6. **데이터 정합성 검증 SQL 세트**: 신규 배치 코드가 레거시 SP와 동일한 데이터를 생성/수정했는지 검증하기 위한 실행 전후 카운트, 해시 검증용 SQL 쿼리 템플릿 포함.
7. **문서 작성이 완료되면 추가 지원 제안, 인사말, 또는 향후 추가 분석 가능성에 대한 설명 등 본문 요건과 관련 없는 사족이나 안내 문구를 문서 끝에 절대 작성하지 마십시오. 문서의 정해진 필수 섹션 작성이 끝나는 즉시 깔끔하게 출력을 마쳐야 합니다.";

            var dependenciesText = new StringBuilder();
            var tableSchemasText = new StringBuilder();
            var referenceDdlsText = new StringBuilder();

            foreach (var dep in spDef.Dependencies)
            {
                dependenciesText.AppendLine($"- Schema: {dep.Schema}, Name: {dep.Name}, Type: {dep.Type} (발견 깊이: {dep.DiscoveryDepth}단계)");
                
                if (dep.Columns.Count > 0)
                {
                    tableSchemasText.AppendLine(FormatTableSchemaToMarkdown(dep));
                    tableSchemasText.AppendLine();
                }

                if (!string.IsNullOrEmpty(dep.ReferencedDdlText))
                {
                    referenceDdlsText.AppendLine($"### 객체: {dep.Schema}.{dep.Name} ({dep.Type})");
                    referenceDdlsText.AppendLine("```sql");
                    referenceDdlsText.AppendLine(dep.ReferencedDdlText);
                    referenceDdlsText.AppendLine("```");
                    referenceDdlsText.AppendLine();
                }
            }

            var userPrompt = $@"
분석 대상 Stored Procedure 정보:
- Schema: {spDef.Schema}
- Name: {spDef.Name}

[DB에서 추출된 기계적 의존 관계 목록]
{dependenciesText}

[의존하는 참조 테이블 상세 스키마 정보 (테이블 및 컬럼 주석 설명 포함)]
{tableSchemasText}

[의존하는 참조 함수 및 Stored Procedure 정의 DDL 코드 목록]
{referenceDdlsText}

[Stored Procedure DDL SQL 원본]
```sql
{spDef.DdlText}
```

위 레거시 배치 SP 정보를 바탕으로 {targetLanguage} 기준의 '배치 전환 계획 설계서'를 작성해 주십시오.
";

            return await _aiClient.ChatAsync(systemPrompt, userPrompt, _temperature, cancellationToken);
        }

        public async Task<string> GenerateConsolidatedBatchPlanAsync(System.Collections.Generic.List<(string FileName, string Content)> specs, string targetLanguage, string jobName, CancellationToken cancellationToken = default)
        {
            var systemPrompt = $@"당신은 여러 개의 레거시 Stored Procedure 분석 명세서(마크다운)를 바탕으로, 이를 최신 {targetLanguage} 기반의 단일 배치 애플리케이션 및 스케줄러 전환 설계도(Consolidated Batch Modernization Plan)로 작성하는 전문 수석 배치 아키텍트입니다.
제공된 개별 SP 분석서들의 비즈니스 요약과 테이블 CRUD 맵을 종합적으로 설계하여, '{jobName}'이라는 단일 통합 배치 Job으로 전환하는 계획서를 기안해 주십시오.

[설계서 작성 규칙 및 내용 필수 조건]
1. 문서는 한글 마크다운 양식으로 작성하십시오.
2. 아래 4가지 필수 대헤더(##) 구조를 반드시 준수하여 문서를 구성해야 하며, 그 외의 다른 대헤더는 추가하지 마십시오.
   - ## 통합 배치 아키텍처 개요: 제공된 여러 분석서 파일들이 어떤 순서(순차 체인, 조건 분기, 병렬 처리 등)로 구성되어 하나의 배치 Job 내의 Step들로 설계되는지 기술하십시오.
   - ## Mermaid 기반 통합 흐름도: 전체 배치 Job의 데이터 파이프라인 및 수행 단계를 묘사하는 Mermaid Flowchart 다이어그램을 작성하십시오.
   - ## 단계별 이행 상세 및 의사코드: 각 단계를 처리하는 {targetLanguage} 클래스/컴포넌트 설계, 대용량 청크(Chunk) 페이징 의사코드, 그리고 공통 의존성에 대한 락/트랜잭션 설계 및 실패 시 재시작(Restartability)/복구 계획을 이 섹션 하위에 포함하여 제시하십시오.
   - ## 통합 데이터 정합성 검증 SQL 세트: 배치 실행 전후 데이터 무결성을 검증할 수 있는 통합 SQL 쿼리 세트를 포함하십시오.
3. 문서 작성이 완료되면 추가 지원 제안, 인사말, 또는 향후 추가 분석 가능성에 대한 설명 등 본문 요건과 관련 없는 사족이나 안내 문구를 문서 끝에 절대 작성하지 마십시오. 문서의 정해진 필수 섹션 작성이 끝나는 즉시 깔끔하게 출력을 마쳐야 합니다.";

            var userPrompt = new StringBuilder();
            userPrompt.AppendLine($"통합 배치 Job 명칭: {jobName}");
            userPrompt.AppendLine($"대상 기술 스택: {targetLanguage}");
            userPrompt.AppendLine();
            userPrompt.AppendLine("[제공된 개별 Stored Procedure 분석 명세서 목록]");

            foreach (var spec in specs)
            {
                userPrompt.AppendLine($"---");
                userPrompt.AppendLine($"파일명: {spec.FileName}");
                userPrompt.AppendLine($"[본문 시작]");
                userPrompt.AppendLine(spec.Content);
                userPrompt.AppendLine($"[본문 끝]");
                userPrompt.AppendLine();
            }

            userPrompt.AppendLine("위 개별 명세서들의 정보를 완벽히 분석하여, 지침에 맞추어 단일 통합 배치 전환 계획서를 구성해 주십시오.");

            return await _aiClient.ChatAsync(systemPrompt, userPrompt.ToString(), _temperature, cancellationToken);
        }

        public async Task<ReviewResult> ReviewConsolidatedPlanAsync(System.Collections.Generic.List<(string FileName, string Content)> specs, string planMarkdown, string jobName, CancellationToken cancellationToken = default)
        {
            var systemPrompt = @"당신은 여러 레거시 SP 분석 명세서들을 종합하여 설계된 통합 배치 전환 계획서(Markdown)의 완성도를 검증하는 수석 배치 아키텍트이자 리뷰어 에이전트입니다.
제시된 통합 계획서가 제공된 레거시 명세서들의 기능 설명 및 요구사항을 왜곡 없이 잘 반영하였는지, 배치 아키텍처로서의 기술적 타당성을 갖추었는지 엄격하게 검증하십시오.

[검토 기준]
1. 계획서에 필수 4대 헤더(## 통합 배치 아키텍처 개요, ## Mermaid 기반 통합 흐름도, ## 단계별 이행 상세 및 의사코드, ## 통합 데이터 정합성 검증 SQL 세트)가 다 존재하며 알맞은 내용을 담고 있는가?
2. 각 개별 SP 분석서에 적힌 비즈니스 정합성이 신규 통합 배치 흐름 내에서 훼손되거나 환각(왜곡)이 존재하지 않는가?
3. 단계별 이행 의사코드, 대량 데이터 청크 페이징 전략, 실패 재시작 가이드가 누락 없이 올바르게 서술되어 있는가?

[답변 작성 형식]
반드시 아래 JSON 형식으로만 최종 답변을 출력해야 합니다. JSON 코드 블록 없이 순수 JSON만 반환해야 합니다:
{
  ""HasDefects"": true 또는 false,
  ""FeedbackComment"": ""결함이 있는 경우 무엇이 누락되었거나 어떻게 수정해야 하는지 구체적인 피드백 내용 기술 (HasDefects가 false인 경우 빈 문자열 '' 반환)""
}";

            var userPrompt = new StringBuilder();
            userPrompt.AppendLine($"통합 배치 Job 명칭: {jobName}");
            userPrompt.AppendLine();
            userPrompt.AppendLine("[제공된 개별 Stored Procedure 분석 명세서 목록]");

            foreach (var spec in specs)
            {
                userPrompt.AppendLine($"---");
                userPrompt.AppendLine($"파일명: {spec.FileName}");
                userPrompt.AppendLine(spec.Content);
                userPrompt.AppendLine();
            }

            userPrompt.AppendLine("[작성된 통합 배치 전환 계획서 마크다운]");
            userPrompt.AppendLine(planMarkdown);
            userPrompt.AppendLine();
            userPrompt.AppendLine("위 계획서의 완결성 및 정확성을 검토 기준에 맞게 성실히 분석한 뒤 JSON 포맷으로 답해주십시오.");

            var responseContent = await _aiClient.ChatAsync(systemPrompt, userPrompt.ToString(), 0.1f, cancellationToken);
            try
            {
                var jsonString = ExtractJson(responseContent);

                using (var resultDoc = JsonDocument.Parse(jsonString))
                {
                    var resultRoot = resultDoc.RootElement;
                    var hasDefects = resultRoot.GetProperty("HasDefects").GetBoolean();
                    var feedbackComment = resultRoot.TryGetProperty("FeedbackComment", out var commentProp) ? commentProp.GetString() : null;

                    return new ReviewResult
                    {
                        HasDefects = hasDefects,
                        FeedbackComment = feedbackComment
                    };
                }
            }
            catch (Exception ex)
            {
                return new ReviewResult
                {
                    HasDefects = true,
                    FeedbackComment = $"JSON 검토 보고서 파싱 실패: {ex.Message}"
                };
            }
        }

        private static string ExtractJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            content = content.Trim();

            // ```json ... ``` 블록 추출 시도
            int jsonStartIndex = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (jsonStartIndex != -1)
            {
                int start = jsonStartIndex + 7;
                int end = content.IndexOf("```", start);
                if (end != -1)
                {
                    return content.Substring(start, end - start).Trim();
                }
            }

            // ``` ... ``` 블록 추출 시도 (json 키워드가 없는 경우)
            int blockStartIndex = content.IndexOf("```");
            if (blockStartIndex != -1)
            {
                int start = blockStartIndex + 3;
                int end = content.IndexOf("```", start);
                if (end != -1)
                {
                    return content.Substring(start, end - start).Trim();
                }
            }

            // 가장 바깥쪽의 { } 짝 추출 시도
            int firstBrace = content.IndexOf('{');
            int lastBrace = content.LastIndexOf('}');
            if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
            {
                return content.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return content;
        }
    }
}
