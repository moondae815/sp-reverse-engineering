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

        public async Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions)
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
    }
}
