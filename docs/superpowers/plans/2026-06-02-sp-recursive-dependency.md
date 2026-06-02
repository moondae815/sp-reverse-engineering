# Stored Procedure Recursive Dependency Analyzer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stored Procedure 분석 시, 참조하는 테이블들의 상세 스키마(PK/FK 포함) 및 참조 UDF/SP들의 본문 소스코드를 지정된 최대 깊이까지 재귀적으로 수집하여 AI 프롬프트에 Markdown 표 형태로 포함시키는 고도화 기능을 구현합니다.

**Architecture:** `DbMetadataService`에서 DFS 탐색과 방문 중복 제어를 통해 계층형 의존관계를 재귀적으로 순회 쿼리하고, `AiService`에서 수집한 메타데이터와 DDL 코드를 가독성 높은 마크다운 형식으로 가공하여 프롬프트로 전송합니다.

**Tech Stack:** .NET 8.0 / C#, Microsoft.Data.SqlClient, xUnit, NSubstitute

---

### Task 1: SpDefinition, DependencyInfo 데이터 모델 확장 및 ColumnInfo 추가

**Files:**
- Create: `src/SpAnalyzer.Core/Models/ColumnInfo.cs`
- Modify: `src/SpAnalyzer.Core/Models/DependencyInfo.cs`
- Modify: `src/SpAnalyzer.Core/Models/SpDefinition.cs`
- Modify: `tests/SpAnalyzer.Core.Tests/ModelsTest.cs`

- [x] **Step 1: ColumnInfo 및 확장된 모델 정보를 빌드 검증하는 실패하는 테스트 작성**

파일 수정: `tests/SpAnalyzer.Core.Tests/ModelsTest.cs`
```csharp
using Xunit;
using SpAnalyzer.Core.Models;
using System.Collections.Generic;

namespace SpAnalyzer.Core.Tests
{
    public class ModelsTest
    {
        [Fact]
        public void SpDefinition_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "USP_GetUsers",
                DdlText = "CREATE PROCEDURE USP_GetUsers AS SELECT * FROM Users;",
                Dependencies = new List<DependencyInfo>
                {
                    new DependencyInfo 
                    { 
                        Schema = "dbo", 
                        Name = "Users", 
                        Type = "USER_TABLE",
                        DiscoveryDepth = 1,
                        ReferencedDdlText = null,
                        Columns = new List<ColumnInfo>
                        {
                            new ColumnInfo { ColumnName = "UserId", DataType = "INT", IsNullable = false, IsPrimaryKey = true, IsForeignKey = false }
                        }
                    }
                }
            };

            // Assert
            Assert.Equal("dbo", spDef.Schema);
            Assert.Equal("USP_GetUsers", spDef.Name);
            Assert.Single(spDef.Dependencies);
            var dep = spDef.Dependencies[0];
            Assert.Equal("Users", dep.Name);
            Assert.Equal("USER_TABLE", dep.Type);
            Assert.Equal(1, dep.DiscoveryDepth);
            Assert.Null(dep.ReferencedDdlText);
            Assert.Single(dep.Columns);
            Assert.Equal("UserId", dep.Columns[0].ColumnName);
            Assert.Equal("INT", dep.Columns[0].DataType);
            Assert.False(dep.Columns[0].IsNullable);
            Assert.True(dep.Columns[0].IsPrimaryKey);
            Assert.False(dep.Columns[0].IsForeignKey);
        }
    }
}
```

- [x] **Step 2: 테스트를 빌드하여 컴파일 실패(Red) 확인**

Run: `dotnet test`
Expected: `ColumnInfo` 클래스 부재, `DiscoveryDepth`, `Columns`, `ReferencedDdlText` 필드 부재로 컴파일 오류 발생.

- [x] **Step 3: 데이터 모델 구현 추가**

파일 생성: `src/SpAnalyzer.Core/Models/ColumnInfo.cs`
```csharp
namespace SpAnalyzer.Core.Models
{
    public class ColumnInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
    }
}
```

파일 수정: `src/SpAnalyzer.Core/Models/DependencyInfo.cs`
```csharp
using System.Collections.Generic;

namespace SpAnalyzer.Core.Models
{
    public class DependencyInfo
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int DiscoveryDepth { get; set; }
        public List<ColumnInfo> Columns { get; set; } = new();
        public string? ReferencedDdlText { get; set; }
    }
}
```

- [x] **Step 4: 테스트를 재실행하여 빌드 및 패스 검증**

Run: `dotnet test`
Expected: PASS

- [x] **Step 5: Git Commit**

```bash
git add src/SpAnalyzer.Core/Models/ColumnInfo.cs src/SpAnalyzer.Core/Models/DependencyInfo.cs tests/SpAnalyzer.Core.Tests/ModelsTest.cs
git commit -m "feat: extend data models for ColumnInfo and recursive discovery details"
```

---

### Task 2: 테이블 스키마 상세 쿼리 구현 (`DbMetadataService.GetTableColumnsAsync`)

**Files:**
- Modify: `src/SpAnalyzer.Core/Services/DbMetadataService.cs`
- Modify: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceTests.cs`

- [x] **Step 1: 잘못된 연결 정보 제공 시 스키마 조회가 실패하여 예외를 발생시키는지 단위 테스트 작성**

파일 수정: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceTests.cs` 에 아래 테스트 메서드 추가
```csharp
        [Fact]
        public async Task GetTableColumnsAsync_WithInvalidConn_ShouldThrowException()
        {
            // Arrange
            var invalidConnString = "Server=invalid_server;Database=invalid_db;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=1;";
            var service = new DbMetadataService();

            // Act & Assert
            await Assert.ThrowsAnyAsync<System.Exception>(() => service.GetTableColumnsAsync(invalidConnString, "dbo", "NonExistentTable"));
        }
```

- [x] **Step 2: 테스트를 빌드하여 컴파일 실패 확인**

Run: `dotnet test`
Expected: `DbMetadataService` 내에 `GetTableColumnsAsync`가 정의되지 않아 컴파일 에러 발생.

- [x] **Step 3: DbMetadataService 내에 GetTableColumnsAsync 실물 비즈니스 메서드 추가**

파일 수정: `src/SpAnalyzer.Core/Services/DbMetadataService.cs`에 아래 메서드를 추가 정의합니다. (클래스 내부 헬퍼 메서드로 활용)
```csharp
        public async Task<List<ColumnInfo>> GetTableColumnsAsync(string connectionString, string schema, string tableName)
        {
            var columns = new List<ColumnInfo>();
            var query = @"
                SELECT 
                    c.COLUMN_NAME,
                    c.DATA_TYPE + 
                        CASE 
                            WHEN c.CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN 
                                '(' + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR(10)) END + ')'
                            WHEN c.NUMERIC_PRECISION IS NOT NULL AND c.NUMERIC_SCALE IS NOT NULL AND c.DATA_TYPE IN ('decimal', 'numeric') THEN 
                                '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR(10)) + ',' + CAST(c.NUMERIC_SCALE AS VARCHAR(10)) + ')'
                            ELSE ''
                        END AS DataType,
                    CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                    ISNULL((SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' 
                              AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA 
                              AND tc.TABLE_NAME = c.TABLE_NAME 
                              AND kcu.COLUMN_NAME = c.COLUMN_NAME), 0) AS IsPrimaryKey,
                    ISNULL((SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                            WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY' 
                              AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA 
                              AND tc.TABLE_NAME = c.TABLE_NAME 
                              AND kcu.COLUMN_NAME = c.COLUMN_NAME), 0) AS IsForeignKey
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @TableName
                ORDER BY c.ORDINAL_POSITION;";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Schema", schema);
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            columns.Add(new ColumnInfo
                            {
                                ColumnName = reader.GetString(0),
                                DataType = reader.GetString(1),
                                IsNullable = reader.GetInt32(2) == 1,
                                IsPrimaryKey = reader.GetInt32(3) == 1,
                                IsForeignKey = reader.GetInt32(4) == 1
                            });
                        }
                    }
                }
            }
            return columns;
        }
```

- [x] **Step 4: 테스트를 재실행하여 정상 동작 검증**

Run: `dotnet test`
Expected: PASS

- [x] **Step 5: Git Commit**

```bash
git add src/SpAnalyzer.Core/Services/DbMetadataService.cs tests/SpAnalyzer.Core.Tests/DbMetadataServiceTests.cs
git commit -m "feat: implement GetTableColumnsAsync in DbMetadataService"
```

---

### Task 3: 재귀적 의존성 탐색 알고리즘 구현 (`DbMetadataService.GetSpDetailsAsync` 고도화)

**Files:**
- Modify: `src/SpAnalyzer.Core/Services/IDbMetadataService.cs`
- Modify: `src/SpAnalyzer.Core/Services/DbMetadataService.cs`
- Modify: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceDetailsTests.cs`

- [ ] **Step 5.1: maxDepth 매개변수를 지원하도록 수정한 상세 조회 예외 단위 테스트 작성**

파일 수정: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceDetailsTests.cs`
```csharp
using System;
using System.Threading.Tasks;
using Xunit;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class DbMetadataServiceDetailsTests
    {
        [Fact]
        public async Task GetSpDetailsAsync_WithInvalidConn_ShouldThrowException()
        {
            // Arrange
            var invalidConnString = "Server=invalid_server;Database=invalid_db;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=1;";
            IDbMetadataService service = new DbMetadataService();

            // Act & Assert
            // maxDepth=3 인자를 전달하여 호출 시그니처 변경에 따른 오류 유발 및 1차 예외 통과 확인
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetSpDetailsAsync(invalidConnString, "dbo", "USP_NonExistent", 3));
        }
    }
}
```

- [ ] **Step 5.2: 테스트를 빌드하여 컴파일 실패 확인**

Run: `dotnet test`
Expected: `IDbMetadataService` 및 `DbMetadataService` 의 `GetSpDetailsAsync` 시그니처 불일치로 컴파일 에러 발생.

- [ ] **Step 5.3: IDbMetadataService 인터페이스 및 DbMetadataService 구현 고도화**

파일 수정: `src/SpAnalyzer.Core/Services/IDbMetadataService.cs`
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IDbMetadataService
    {
        Task<List<string>> GetStoredProcedureNamesAsync(string connectionString);
        Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName, int maxDepth);
    }
}
```

파일 수정: `src/SpAnalyzer.Core/Services/DbMetadataService.cs` 의 `GetSpDetailsAsync` 부분을 다음과 같이 전면 교체 및 헬퍼 메서드 구현
```csharp
        // 헬퍼 메서드: 특정 객체의 DDL 원본 텍스트 조회
        private async Task<string> GetObjectDdlAsync(string connectionString, string schema, string objectName)
        {
            var fullName = $"{schema}.{objectName}";
            var query = @"
                SELECT definition 
                FROM sys.sql_modules 
                WHERE object_id = OBJECT_ID(@FullName);";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", fullName);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString() ?? string.Empty;
                    }
                }
            }
            throw new InvalidOperationException($"'{fullName}'의 DDL 코드를 찾을 수 없습니다.");
        }

        // 헬퍼 메서드: 특정 객체의 1차 의존 정보 목록 수집
        private async Task<List<DependencyInfo>> GetRawDependenciesAsync(string connectionString, string schema, string objectName)
        {
            var rawDeps = new List<DependencyInfo>();
            var fullName = $"{schema}.{objectName}";
            var query = @"
                SELECT 
                    COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), 'dbo') AS ReferencedSchema,
                    d.referenced_entity_name AS ReferencedEntityName,
                    o.type_desc AS ReferencedType
                FROM sys.sql_expression_dependencies d
                INNER JOIN sys.objects o ON d.referenced_id = o.object_id
                WHERE d.referencing_id = OBJECT_ID(@FullName);";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", fullName);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            rawDeps.Add(new DependencyInfo
                            {
                                Schema = reader.GetString(0),
                                Name = reader.GetString(1),
                                Type = reader.GetString(2)
                            });
                        }
                    }
                }
            }
            return rawDeps;
        }

        // 메인 재귀 탐색 진입점
        public async Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName, int maxDepth)
        {
            var spDef = new SpDefinition { Schema = schema, Name = spName };
            var spFullName = $"{schema}.{spName}";

            // 1. 메인 SP의 DDL 조회
            spDef.DdlText = await GetObjectDdlAsync(connectionString, schema, spName);

            // 2. 중복 방지 방문 해시셋 및 재귀 리스트 생성
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { spFullName };
            
            // 3. 재귀 수집 시작
            await GatherDependenciesRecursiveAsync(connectionString, schema, spName, 1, maxDepth, visited, spDef.Dependencies);

            return spDef;
        }

        // 재귀 호출 메서드 (DFS)
        private async Task GatherDependenciesRecursiveAsync(
            string connectionString, string schema, string name, 
            int currentDepth, int maxDepth, 
            HashSet<string> visited, List<DependencyInfo> dependencies)
        {
            if (currentDepth > maxDepth) return;

            List<DependencyInfo> rawDeps;
            try
            {
                rawDeps = await GetRawDependenciesAsync(connectionString, schema, name);
            }
            catch
            {
                return; // 수집 실패 시 조용히 스킵 (Soft Fail)
            }

            foreach (var rawDep in rawDeps)
            {
                var depFullName = $"{rawDep.Schema}.{rawDep.Name}";
                if (visited.Contains(depFullName)) continue;

                visited.Add(depFullName);

                var depInfo = new DependencyInfo
                {
                    Schema = rawDep.Schema,
                    Name = rawDep.Name,
                    Type = rawDep.Type,
                    DiscoveryDepth = currentDepth
                };

                // 스키마 조회 분기 (테이블, 뷰)
                if (rawDep.Type.Contains("TABLE") || rawDep.Type.Contains("VIEW"))
                {
                    try
                    {
                        depInfo.Columns = await GetTableColumnsAsync(connectionString, rawDep.Schema, rawDep.Name);
                    }
                    catch
                    {
                        // 일부 권한 누락 스킵
                    }
                }
                // 코드 수집 및 하위 재귀 분기 (UDF, SP)
                else if (rawDep.Type.Contains("FUNCTION") || rawDep.Type.Contains("PROCEDURE"))
                {
                    try
                    {
                        depInfo.ReferencedDdlText = await GetObjectDdlAsync(connectionString, rawDep.Schema, rawDep.Name);
                        
                        // 하위 재귀 수집 호출
                        await GatherDependenciesRecursiveAsync(
                            connectionString, rawDep.Schema, rawDep.Name, 
                            currentDepth + 1, maxDepth, visited, dependencies);
                    }
                    catch
                    {
                        // 권한 오류 등 무시
                    }
                }

                dependencies.Add(depInfo);
            }
        }
```

- [ ] **Step 5.4: 테스트를 재실행하여 컴파일 및 동작 무결성 확인**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 5.5: Git Commit**

```bash
git add src/SpAnalyzer.Core/Services/IDbMetadataService.cs src/SpAnalyzer.Core/Services/DbMetadataService.cs tests/SpAnalyzer.Core.Tests/DbMetadataServiceDetailsTests.cs
git commit -m "feat: complete recursive dependency search and metadata extraction"
```

---

### Task 4: AI 서비스 마크다운 포맷터 및 프롬프트 조립 로직 구현 (`AiService.cs`)

**Files:**
- Modify: `src/SpAnalyzer.Core/Services/AiService.cs`
- Modify: `tests/SpAnalyzer.Core.Tests/AiServiceTests.cs`

- [ ] **Step 1: AI 명세서 작성 API 키 누락에 따른 예외 처리 테스트 수정 및 빌드 확인**

파일 수정: `tests/SpAnalyzer.Core.Tests/AiServiceTests.cs`
```csharp
using System;
using System.Threading.Tasks;
using Xunit;
using SpAnalyzer.Core.Models;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class AiServiceTests
    {
        [Fact]
        public async Task GenerateSpecificationAsync_WithEmptyApiKeyForOpenAi_ShouldThrowException()
        {
            // Arrange
            var spDef = new SpDefinition { Schema = "dbo", Name = "USP_Test", DdlText = "SELECT 1;" };
            var instructions = "규칙1: 상세하게 쓸 것.";
            
            IAiService service = new AiService("OpenAI", "gpt-4o", "", "https://api.openai.com/v1", 0.2f);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.GenerateSpecificationAsync(spDef, instructions));
        }
    }
}
```

- [ ] **Step 2: 빌드 정상 여부 스크리닝**

Run: `dotnet test`
Expected: 아직 `AiService` 내부 구현을 수정하지 않았으므로 기존 테스트와 함께 빌드 성공 확인.

- [ ] **Step 3: AiService 내에 컬럼 마크다운 테이블 변환기 및 DDL 주입 코드 작성**

파일 수정: `src/SpAnalyzer.Core/Services/AiService.cs` 내의 `GenerateSpecificationAsync` 및 포맷터 함수를 다음과 같이 수정 및 추가
```csharp
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
```

- [ ] **Step 4: 단위 테스트 구동 검증**

Run: `dotnet test`
Expected: 모든 테스트 성공 (PASS)

- [ ] **Step 5: Git Commit**

```bash
git add src/SpAnalyzer.Core/Services/AiService.cs tests/SpAnalyzer.Core.Tests/AiServiceTests.cs
git commit -m "feat: enrich AiService prompts with Markdown schemas and dependency UDF source codes"
```

---

### Task 5: CLI 연동 및 appsettings.json 깊이 한계 설정 반영

**Files:**
- Modify: `src/SpAnalyzer.Cli/Program.cs`
- Modify: `src/SpAnalyzer.Cli/appsettings.json`

- [ ] **Step 1: appsettings.json에 MaxDependencyDepth 설정 키 추가**

파일 수정: `src/SpAnalyzer.Cli/appsettings.json`
```json
{
  "DatabaseSettings": {
    "Server": "localhost",
    "Database": "master",
    "MaxDependencyDepth": 3
  },
  "AiSettings": {
    "Provider": "OpenAI",
    "ModelName": "gpt-4o",
    "ApiKey": "",
    "Endpoint": "",
    "Temperature": 0.2
  },
  "OutputSettings": {
    "Directory": "./output",
    "InstructionsFile": "./instructions.txt"
  }
}
```

- [ ] **Step 2: Program.cs 파일에 MaxDependencyDepth 설정 바인딩 및 GetSpDetailsAsync 호출 인자 연동**

파일 수정: `src/SpAnalyzer.Cli/Program.cs`에서 `configuration` 값 바인딩 및 호출하는 라인을 수정합니다.
- 변경 구간 1 (라인 95~101 부근):
  ```csharp
              // MaxDependencyDepth 추가 로드
              var depthStr = configuration["DatabaseSettings:MaxDependencyDepth"] ?? "3";
              int.TryParse(depthStr, out int maxDepth);

              var outputDir = configuration["OutputSettings:Directory"] ?? "./output";
  ```
- 변경 구간 2 (라인 171 부근 `GetSpDetailsAsync` 호출):
  ```csharp
                              ctx.Status($"[yellow]{selectedOption}[/] - DB 메타데이터 및 의존성 분석 중 (최대 깊이: {maxDepth}단계)...");
                              spDef = await dbService.GetSpDetailsAsync(connectionString, schema, name, maxDepth);
  ```

- [ ] **Step 3: CLI 어플리케이션 컴파일 빌드 검증**

Run: `dotnet build`
Expected: Build Success

- [ ] **Step 4: 무결성 전체 단위 테스트 및 수동 작동 테스트 검증**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 5: Git Commit**

```bash
git add src/SpAnalyzer.Cli/Program.cs src/SpAnalyzer.Cli/appsettings.json
git commit -m "feat: apply MaxDependencyDepth configuration to CLI application execution"
```
