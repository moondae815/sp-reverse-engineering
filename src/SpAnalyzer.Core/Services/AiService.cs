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
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? "https://api.openai.com/v1" : endpoint;
            _temperature = temperature;
        }

        private string FormatTableSchemaToMarkdown(DependencyInfo dep)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"### 테이블: {dep.Schema}.{dep.Name} ({dep.Type}) - 발견 깊이: {dep.DiscoveryDepth}단계");
            sb.AppendLine("| 컬럼명 | 데이터 타입 | Null 허용 | 제약 조건 |");
            sb.AppendLine("| :--- | :--- | :---: | :--- |");
            
            foreach (var col in dep.Columns)
            {
                var constraints = new System.Collections.Generic.List<string>();
                if (col.IsPrimaryKey) constraints.Add("PRIMARY KEY");
                if (col.IsForeignKey) constraints.Add("FOREIGN KEY");
                
                var constraintStr = string.Join(", ", constraints);
                var nullableStr = col.IsNullable ? "Yes" : "No";
                
                sb.AppendLine($"| {col.ColumnName} | {col.DataType} | {nullableStr} | {constraintStr} |");
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
    }
}
