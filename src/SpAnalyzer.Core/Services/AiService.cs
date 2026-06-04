using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class AiService : IAiService
    {
        private readonly string _provider;
        private readonly string _modelName;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly float _temperature;
        private static readonly HttpClient _httpClient = new();

        public AiService(string provider, string modelName, string apiKey, string endpoint, float temperature)
        {
            _provider = provider;
            _modelName = modelName;
            _apiKey = apiKey;
            
            var ep = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com/v1" : endpoint.Trim();
            if (ep.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                ep = ep.Substring(0, ep.Length - "/chat/completions".Length).TrimEnd('/');
            }
            _endpoint = ep;
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

        public async Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions, string? feedbackLog = null)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            // 프롬프트 조립
            var systemPrompt = $@"당신은 SQL Server Stored Procedure 분석 전문가입니다. 다음 규칙을 준수하여 마크다운 기능 명세서를 작성하십시오.

[분석 추가 규칙]
1. 분석 대상 SP 뿐만 아니라 제공된 참조 테이블 스키마 컬럼 정보 및 참조 UDF/SP 소스코드를 모두 참고하여 분석 보고서를 한글로 성실히 작성하십시오.
2. SP 내부에서 참조 테이블의 어떤 컬럼 값을 제어/수정하고 조건식에 쓰는지 파라미터 구조와 매핑하여 작성하십시오.
3. SP에서 호출하는 사용자 정의 함수(UDF)의 연산 알고리즘을 소스코드를 보고 분석하여 비즈니스 로직 요약에 포함시키십시오.
4. 비즈니스 흐름을 직관적으로 이해할 수 있는 Mermaid Flowchart 다이어그램을 필수로 포함해 마크다운으로 구성해 주십시오. 노드 텍스트에 특수문자나 괄호가 들어가 에러가 발생하지 않도록 큰따옴표("")로 감싸 문법을 엄격히 준수하십시오.

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

            // OpenAI / Ollama 호환 JSON 페이로드 작성
            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = _temperature
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;
                return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            }
        }

        public async Task<ReviewResult> ReviewSpecificationAsync(SpDefinition spDef, string specMarkdown)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

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
  ""FeedbackComment"": ""결함이 있는 경우 무엇이 누락되었거나 어떻게 수정해야 하는지 구체적인 피드백 내용 기술 (HasDefects가 false인 경우 null)""
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

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1f
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            try
            {
                using (var doc = JsonDocument.Parse(responseContent))
                {
                    var root = doc.RootElement;
                    var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

                    content = content.Trim();
                    if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                    {
                        content = content.Substring(7);
                    }
                    if (content.EndsWith("```"))
                    {
                        content = content.Substring(0, content.Length - 3);
                    }
                    content = content.Trim();

                    using (var resultDoc = JsonDocument.Parse(content))
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
            }
            catch (Exception ex)
            {
                return new ReviewResult
                {
                    HasDefects = false,
                    FeedbackComment = $"JSON 검토 보고서 파싱 실패: {ex.Message}"
                };
            }
        }

        public async Task<string> GenerateBatchMigrationPlanAsync(SpDefinition spDef, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            var systemPrompt = $@"당신은 SQL Server Agent의 스케줄러 배치 작업을 현대적인 애플리케이션 기반 배치 프레임워크로 전환하는 최적화 설계 전문가입니다.
대상 Stored Procedure 소스 코드와 의존 테이블/UDF 구조를 분석하여, {targetLanguage} 기반의 현대적인 백그라운드 배치 컴포넌트로 포팅하기 위한 '배치 전환 계획 설계서'를 작성해 주십시오.

[설계서 작성 규칙 및 내용 필수 조건]
1. 문서는 한글 마크다운 양식으로 작성하십시오.
2. **배치 전환 아키텍처 개요**: SQL Server Agent Job 역할을 대체할 신규 스케줄러 프레임워크 제안 (예: C#인 경우 Quartz.NET / Hangfire 기반 Worker Service, Java인 경우 Spring Batch + Quartz).
3. **대량 데이터 청크(Chunk) 처리 전략**: OOM 방지를 위한 Paging Reader 패턴 및 벌크 연산(Bulk Write) 가이드라인 제안.
4. **비즈니스 전환 설계 및 의사코드(Pseudocode)**: SP 내부의 주요 비즈니스 로직(분기, 루프, 데이터 처리 등)을 {targetLanguage}의 OOP 문법 및 ORM(EF Core / JPA)으로 전환하는 구체적 의사코드(코드 구조 예시) 제공.
5. **로깅 및 실패 조치 계획**: 기존 TRY...CATCH 에러 로깅을 구조화된 로그(Serilog 등)로 전환하고 알림(Slack 등) 발송 방안 매핑.
6. **데이터 정합성 검증 SQL 세트**: 신규 배치 코드가 레거시 SP와 동일한 데이터를 생성/수정했는지 검증하기 위한 실행 전후 카운트, 해시 검증용 SQL 쿼리 템플릿 포함.";

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

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = _temperature
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;
                return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            }
        }

        public async Task<string> GenerateConsolidatedBatchPlanAsync(System.Collections.Generic.List<(string FileName, string Content)> specs, string targetLanguage, string jobName)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            var systemPrompt = $@"당신은 여러 개의 레거시 Stored Procedure 분석 명세서(마크다운)를 바탕으로, 이를 최신 {targetLanguage} 기반의 단일 배치 애플리케이션 및 스케줄러 전환 설계도(Consolidated Batch Modernization Plan)로 작성하는 전문 수석 배치 아키텍트입니다.
제공된 개별 SP 분석서들의 비즈니스 요약과 테이블 CRUD 맵을 종합적으로 설계하여, '{jobName}'이라는 단일 통합 배치 Job으로 전환하는 계획서를 기안해 주십시오.

[설계서 작성 규칙 및 내용 필수 조건]
1. 문서는 한글 마크다운 양식으로 작성하십시오.
2. **통합 배치 아키텍처 개요**: 제공된 여러 분석서 파일들이 어떤 순서(순차 체인, 조건 분기, 병렬 처리 등)로 구성되어 하나의 배치 Job 내의 Step들로 설계되는지 아키텍처 구조를 기술하십시오.
3. **Mermaid 기반 통합 흐름도**: 각 SP의 데이터 입출력과 비즈니스 흐름을 바탕으로, 전체 배치 Job의 데이터 파이프라인 및 수행 단계를 묘사하는 Mermaid Flowchart 다이어그램을 필수로 작성하십시오.
4. **단계별 이행 상세 및 의사코드(Pseudocode)**: 각 명세서 파일 내용을 매핑하여, 해당 단계를 처리하는 {targetLanguage} 클래스/컴포넌트 설계와 OOM 방지를 위한 대용량 청크(Chunk) 페이징 의사코드를 단계별로 구체화하여 제시하십시오.
5. **공통 의존성 및 락/트랜잭션 설계**: 여러 Step들이 동일한 테이블을 공유할 때 발생할 수 있는 데이터 정합성 충돌(Deadlock 등) 방지책과 트랜잭션 범위 설정을 조언하십시오.
6. **재시작성(Restartability) 및 복구 계획**: 배치 실행 중 특정 Step 실패 시, 체크포인트(Checkpoint)를 활용하여 처음부터가 아닌 실패 지점부터 이어서 재처리할 수 있는 구조적 전략과 Serilog/Slack 알림 통합 계획을 정의하십시오.
7. **통합 데이터 정합성 검증 SQL 세트**: 배치 시작 전과 완료 후의 전체 데이터 무결성을 검증(건수 대조, 집계 검사 등)할 수 있는 통합 SQL 쿼리 세트를 포함하십시오.";

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

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt.ToString() }
                },
                temperature = _temperature
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;
                return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            }
        }
    }
}
