# SQL Server Stored Procedure Reverse Engineering Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SQL Server 2022와 연동하여 Stored Procedure 목록을 가져오고, TUI/CLI 상에서 자동완성으로 검색/선택한 SP의 코드와 의존 테이블을 AI를 사용해 마크다운 기능 명세서로 변환하는 도구를 빌드합니다.

**Architecture:** 솔루션을 핵심 비즈니스 로직 및 AI 연동을 담당하는 클래스 라이브러리(`SpAnalyzer.Core`), Spectre.Console 기반 TUI를 제공하는 콘솔 애플리케이션(`SpAnalyzer.Cli`), 그리고 단위 테스트를 위한 xUnit 프로젝트(`SpAnalyzer.Core.Tests`)로 구성합니다.

**Tech Stack:** .NET 8.0, C#, Microsoft.Data.SqlClient, Spectre.Console, Microsoft.Extensions.Configuration, Microsoft.Extensions.DependencyInjection, xUnit, NSubstitute (Mocking용)

---

### Task 1: SpAnalyzer.Core 프로젝트 구조 및 데이터 모델 셋업

**Files:**
- Create: `src/SpAnalyzer.Core/Models/SpDefinition.cs`
- Create: `src/SpAnalyzer.Core/Models/DependencyInfo.cs`
- Create: `tests/SpAnalyzer.Core.Tests/ModelsTest.cs`

- [x] **Step 1: SpDefinition 및 DependencyInfo 모델 매핑을 검증하는 실패하는 테스트 작성**

파일 생성: `tests/SpAnalyzer.Core.Tests/ModelsTest.cs`
```csharp
using Xunit;
using SpAnalyzer.Core.Models;

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
                    new DependencyInfo { Schema = "dbo", Name = "Users", Type = "USER_TABLE" }
                }
            };

            // Assert
            Assert.Equal("dbo", spDef.Schema);
            Assert.Equal("USP_GetUsers", spDef.Name);
            Assert.Equal("CREATE PROCEDURE USP_GetUsers AS SELECT * FROM Users;", spDef.DdlText);
            Assert.Single(spDef.Dependencies);
            Assert.Equal("Users", spDef.Dependencies[0].Name);
            Assert.Equal("USER_TABLE", spDef.Dependencies[0].Type);
        }
    }
}
```

- [x] **Step 2: 테스트를 빌드하여 실패하는지 확인**

실행: `dotnet test` (또는 프로젝트 디렉터리에서 수행)
Expected: `SpDefinition` 및 `DependencyInfo` 가 정의되지 않아 빌드 실패.

- [x] **Step 3: 데이터 모델 클래스 정의 작성**

파일 생성: `src/SpAnalyzer.Core/Models/DependencyInfo.cs`
```csharp
namespace SpAnalyzer.Core.Models
{
    public class DependencyInfo
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
```

파일 생성: `src/SpAnalyzer.Core/Models/SpDefinition.cs`
```csharp
using System.Collections.Generic;

namespace SpAnalyzer.Core.Models
{
    public class SpDefinition
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string DdlText { get; set; } = string.Empty;
        public List<DependencyInfo> Dependencies { get; set; } = new();
    }
}
```

- [x] **Step 4: 테스트를 재실행하여 빌드 및 패스 검증**

실행: `dotnet test`
Expected: PASS

- [x] **Step 5: Git Commit**

실행:
```bash
git add src/SpAnalyzer.Core/Models/DependencyInfo.cs src/SpAnalyzer.Core/Models/SpDefinition.cs tests/SpAnalyzer.Core.Tests/ModelsTest.cs
git commit -m "feat: add SpDefinition and DependencyInfo data models"
```

---

### Task 2: IDbMetadataService 정의 및 DbMetadataService 구현 (SP 목록 조회)

**Files:**
- Create: `src/SpAnalyzer.Core/Services/IDbMetadataService.cs`
- Create: `src/SpAnalyzer.Core/Services/DbMetadataService.cs`
- Create: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceTests.cs`

- [x] **Step 1: SQL Server 메타데이터 조회를 검증하는 테스트 인터페이스 및 가짜 연결 테스트 작성**

파일 생성: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceTests.cs`
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class DbMetadataServiceTests
    {
        [Fact]
        public async Task GetStoredProcedureNamesAsync_WithInvalidConnectionString_ShouldThrowException()
        {
            // Arrange
            var invalidConnString = "Server=invalid_server;Database=invalid_db;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=1;";
            IDbMetadataService service = new DbMetadataService();

            // Act & Assert
            await Assert.ThrowsAnyAsync<System.Exception>(() => service.GetStoredProcedureNamesAsync(invalidConnString));
        }
    }
}
```

- [x] **Step 2: 테스트를 빌드하여 실패 확인**

실행: `dotnet test`
Expected: `IDbMetadataService` 및 `DbMetadataService` 가 존재하지 않아 빌드 에러 발생.

- [x] **Step 3: 인터페이스 및 DbMetadataService 기본 구현 작성**

파일 생성: `src/SpAnalyzer.Core/Services/IDbMetadataService.cs`
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IDbMetadataService
    {
        Task<List<string>> GetStoredProcedureNamesAsync(string connectionString);
        Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName);
    }
}
```

파일 생성: `src/SpAnalyzer.Core/Services/DbMetadataService.cs`
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class DbMetadataService : IDbMetadataService
    {
        public async Task<List<string>> GetStoredProcedureNamesAsync(string connectionString)
        {
            var spList = new List<string>();
            var query = @"
                SELECT ROUTINE_SCHEMA + '.' + ROUTINE_NAME 
                FROM INFORMATION_SCHEMA.ROUTINES 
                WHERE ROUTINE_TYPE = 'PROCEDURE' 
                ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            spList.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return spList;
        }

        public Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName)
        {
            throw new NotImplementedException();
        }
    }
}
```

- [x] **Step 4: 테스트를 재실행하여 정상 동작 검증**

실행: `dotnet test`
Expected: PASS (잘못된 Connection String에 대해 예외 발생 검증 완료)

- [x] **Step 5: Git Commit**

실행:
```bash
git add src/SpAnalyzer.Core/Services/IDbMetadataService.cs src/SpAnalyzer.Core/Services/DbMetadataService.cs tests/SpAnalyzer.Core.Tests/DbMetadataServiceTests.cs
git commit -m "feat: implement IDbMetadataService and GetStoredProcedureNamesAsync"
```

---

### Task 3: DbMetadataService.GetSpDetailsAsync 구현 (DDL 및 의존관계 조회)

**Files:**
- Modify: `src/SpAnalyzer.Core/Services/DbMetadataService.cs`
- Create: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceDetailsTests.cs`

- [ ] **Step 1: 세부 정보 조회가 실패하는 경우(잘못된 연결 주소) 예외 검증 테스트 작성**

파일 생성: `tests/SpAnalyzer.Core.Tests/DbMetadataServiceDetailsTests.cs`
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
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetSpDetailsAsync(invalidConnString, "dbo", "USP_NonExistent"));
        }
    }
}
```

- [ ] **Step 2: 테스트를 빌드하여 실패 확인**

실행: `dotnet test`
Expected: `GetSpDetailsAsync`에서 `NotImplementedException`이 슬로우되어 테스트 패스될 수 있으나, 빌드 및 실제 의존성 쿼리 미구현 확인.

- [ ] **Step 3: DbMetadataService.GetSpDetailsAsync 실물 코드 구현**

파일 수정: `src/SpAnalyzer.Core/Services/DbMetadataService.cs`의 `GetSpDetailsAsync` 메서드를 다음과 같이 교체합니다.
```csharp
        public async Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName)
        {
            var spDef = new SpDefinition { Schema = schema, Name = spName };
            var spFullName = $"{schema}.{spName}";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                // 1. SP DDL 텍스트 조회
                var ddlQuery = @"
                    SELECT definition 
                    FROM sys.sql_modules 
                    WHERE object_id = OBJECT_ID(@SpFullName);";

                using (var cmd = new SqlCommand(ddlQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@SpFullName", spFullName);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        spDef.DdlText = result.ToString() ?? string.Empty;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Stored Procedure '{spFullName}'의 DDL 코드를 찾을 수 없습니다.");
                    }
                }

                // 2. 의존 객체 메타데이터 조회
                var depQuery = @"
                    SELECT 
                        COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), 'dbo') AS ReferencedSchema,
                        d.referenced_entity_name AS ReferencedEntityName,
                        o.type_desc AS ReferencedType
                    FROM sys.sql_expression_dependencies d
                    INNER JOIN sys.objects o ON d.referenced_id = o.object_id
                    WHERE d.referencing_id = OBJECT_ID(@SpFullName);";

                using (var cmd = new SqlCommand(depQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@SpFullName", spFullName);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            spDef.Dependencies.Add(new DependencyInfo
                            {
                                Schema = reader.GetString(0),
                                Name = reader.GetString(1),
                                Type = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return spDef;
        }
```

- [ ] **Step 4: 테스트를 재실행하여 검증**

실행: `dotnet test`
Expected: PASS

- [ ] **Step 5: Git Commit**

실행:
```bash
git add src/SpAnalyzer.Core/Services/DbMetadataService.cs tests/SpAnalyzer.Core.Tests/DbMetadataServiceDetailsTests.cs
git commit -m "feat: implement GetSpDetailsAsync for SP DDL and SQL dependencies"
```

---

### Task 4: IAiService 정의 및 HttpClient 기반 AiService 구현

**Files:**
- Create: `src/SpAnalyzer.Core/Services/IAiService.cs`
- Create: `src/SpAnalyzer.Core/Services/AiService.cs`
- Create: `tests/SpAnalyzer.Core.Tests/AiServiceTests.cs`

- [x] **Step 1: AI API 연동(HttpClient 통신) 실패 및 성공 시나리오 테스트 작성**

파일 생성: `tests/SpAnalyzer.Core.Tests/AiServiceTests.cs`
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
            
            // OpenAI Provider이지만 API Key가 비어있는 상태
            IAiService service = new AiService("OpenAI", "gpt-4o", "", "https://api.openai.com/v1", 0.2f);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.GenerateSpecificationAsync(spDef, instructions));
        }
    }
}
```

- [x] **Step 2: 테스트 빌드하여 실패 확인**

실행: `dotnet test`
Expected: `IAiService` 및 `AiService` 미정의로 빌드 실패.

- [x] **Step 3: IAiService 및 OpenAI 호환 API 클라이언트 구현 작성**

파일 생성: `src/SpAnalyzer.Core/Services/IAiService.cs`
```csharp
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IAiService
    {
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions);
    }
}
```

파일 생성: `src/SpAnalyzer.Core/Services/AiService.cs`
```csharp
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

        public async Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) && _provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("OpenAI API 키가 설정되지 않았습니다.");
            }

            // 프롬프트 조립
            var systemPrompt = $"당신은 SQL Server Stored Procedure 분석 전문가입니다. 다음 규칙을 준수하여 마크다운 기능 명세서를 작성하십시오.\n\n[사용자 지침]\n{userInstructions}";
            
            var dependenciesText = new StringBuilder();
            foreach (var dep in spDef.Dependencies)
            {
                dependenciesText.AppendLine($"- Schema: {dep.Schema}, Name: {dep.Name}, Type: {dep.Type}");
            }

            var userPrompt = $@"
분석 대상 Stored Procedure 정보:
- Schema: {spDef.Schema}
- Name: {spDef.Name}

[DB에서 추출된 기계적 의존 관계 목록]
{dependenciesText}

[Stored Procedure DDL SQL 원본]
```sql
{spDef.DdlText}
```

위의 정보와 원본 코드를 자세히 리버스 엔지니어링하여 규칙에 맞게 기능 명세서를 마크다운 형식으로 한글로 작성해 주세요.
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
```

- [x] **Step 4: 테스트 재실행하여 예외 발생 및 통과 검증**

실행: `dotnet test`
Expected: PASS

- [x] **Step 5: Git Commit**

실행:
```bash
git add src/SpAnalyzer.Core/Services/IAiService.cs src/SpAnalyzer.Core/Services/AiService.cs tests/SpAnalyzer.Core.Tests/AiServiceTests.cs
git commit -m "feat: add IAiService and implement OpenAI-compatible AiService client"
```

---

### Task 5: 로컬 세션 파일(.session.json) 관리 및 설정 로더 빌드

**Files:**
- Create: `src/SpAnalyzer.Cli/SessionManager.cs`
- Create: `tests/SpAnalyzer.Core.Tests/SessionManagerTests.cs`

- [x] **Step 1: 마지막 로그인 아이디를 저장하고 가져오는 기능의 실패/성공 테스트 작성**

파일 생성: `tests/SpAnalyzer.Core.Tests/SessionManagerTests.cs`
```csharp
using System.IO;
using Xunit;
using SpAnalyzer.Cli;

namespace SpAnalyzer.Core.Tests
{
    public class SessionManagerTests
    {
        [Fact]
        public void SessionManager_ShouldSaveAndLoadLastUserId()
        {
            // Arrange
            var testFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".test.session.json");
            if (File.Exists(testFilePath)) File.Delete(testFilePath);

            // Act
            SessionManager.SaveLastUsedUserId("test_admin_user", testFilePath);
            var loadedUserId = SessionManager.LoadLastUsedUserId(testFilePath);

            // Assert
            Assert.Equal("test_admin_user", loadedUserId);

            // Clean up
            if (File.Exists(testFilePath)) File.Delete(testFilePath);
        }
    }
}
```

- [x] **Step 2: 테스트 빌드하여 실패 확인**

실행: `dotnet test`
Expected: `SessionManager` 클래스가 존재하지 않아 빌드 실패.

- [x] **Step 3: SessionManager 구현체 파일 생성**

파일 생성: `src/SpAnalyzer.Cli/SessionManager.cs`
```csharp
using System.IO;
using System.Text.Json;

namespace SpAnalyzer.Cli
{
    public static class SessionManager
    {
        private const string DefaultSessionFileName = ".session.json";

        public class SessionData
        {
            public string LastUsedUserId { get; set; } = string.Empty;
        }

        public static string LoadLastUsedUserId(string filePath = DefaultSessionFileName)
        {
            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<SessionData>(json);
                return data?.LastUsedUserId ?? string.Empty;
            }
            catch
            {
                return string.Empty; // 세션 로드 실패 시 조용히 넘어감
            }
        }

        public static void SaveLastUsedUserId(string userId, string filePath = DefaultSessionFileName)
        {
            try
            {
                var data = new SessionData { LastUsedUserId = userId };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // 세션 파일 쓰기 실패 처리 무시
            }
        }
    }
}
```

- [x] **Step 4: 테스트 재실행 및 세션 입출력 검증**

실행: `dotnet test`
Expected: PASS

- [x] **Step 5: Git Commit**

실행:
```bash
git add src/SpAnalyzer.Cli/SessionManager.cs tests/SpAnalyzer.Core.Tests/SessionManagerTests.cs
git commit -m "feat: add SessionManager to remember last used DB username"
```

---

### Task 6: appsettings.json, instructions.txt 생성 및 DB 로그인 UI 빌드

**Files:**
- Create: `src/SpAnalyzer.Cli/appsettings.json`
- Create: `src/SpAnalyzer.Cli/instructions.txt`
- Modify: `src/SpAnalyzer.Cli/Program.cs`

- [x] **Step 1: 설정 파일 및 지침 파일 생성**

파일 생성: `src/SpAnalyzer.Cli/appsettings.json`
```json
{
  "DatabaseSettings": {
    "Server": "localhost",
    "Database": "master"
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

파일 생성: `src/SpAnalyzer.Cli/instructions.txt`
```text
# Stored Procedure 기능 명세서 작성 지침

당신은 SQL Server Stored Procedure(SP)를 리버스 엔지니어링하여 비즈니스 로직 기능 명세서를 마크다운으로 작성하는 전문 아키텍트입니다. 다음 규칙을 엄격히 준수하십시오:

1. 문서는 마크다운 양식에 맞춰 구조적으로 한글 작성해야 합니다.
2. **개요**: SP 이름 및 용도 요약.
3. **파라미터 목록**: 매개변수 명칭, 데이터 타입, 주석 역할을 표 형태로 작성합니다.
4. **CRUD 분석**: SP 내부에서 읽거나 조작하는 대상 테이블을 나열하고, CRUD(Create, Read, Update, Delete) 구분을 명확히 표로 표현합니다.
5. **로직 흐름 요약**: 트랜잭션 흐름과 예외 처리(Try...Catch) 유무를 포함해 비즈니스 흐름을 단계별(Step-by-Step) 설명합니다.
```

- [x] **Step 2: Program.cs에 Spectre.Console 대화형 로그인 및 설정 로드 코드 작성**

파일 생성/수정: `src/SpAnalyzer.Cli/Program.cs`
```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Cli
{
    class Program
    {
        static async Task Main(string[] args)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("SP Analyzer").Color(Color.Green));
            AnsiConsole.WriteLine("SQL Server Stored Procedure Reverse Engineering Tool");
            AnsiConsole.WriteLine();

            // 1. 설정 로드
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var server = configuration["DatabaseSettings:Server"] ?? "localhost";
            var database = configuration["DatabaseSettings:Database"] ?? "master";

            AnsiConsole.MarkupLine($"[bold blue]서버:[/] {server}");
            AnsiConsole.MarkupLine($"[bold blue]DB:[/] {database}");
            AnsiConsole.WriteLine();

            // 2. 대화형 ID/비밀번호 로그인 처리
            var lastUserId = SessionManager.LoadLastUsedUserId();
            var userId = AnsiConsole.Prompt(
                new TextPrompt<string>("DB 계정을 입력하세요:")
                    .DefaultValue(string.IsNullOrEmpty(lastUserId) ? "sa" : lastUserId)
            );

            var password = AnsiConsole.Prompt(
                new TextPrompt<string>("DB 비밀번호를 입력하세요:")
                    .Secret()
            );

            // Connection String 빌드
            var connStrBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = userId,
                Password = password,
                TrustServerCertificate = true,
                ConnectTimeout = 5
            };
            var connectionString = connStrBuilder.ConnectionString;

            // 연결 테스트
            bool connectionSuccess = false;
            await AnsiConsole.Status()
                .StartAsync("데이터베이스 연결 시도 중...", async ctx =>
                {
                    try
                    {
                        using (var conn = new SqlConnection(connectionString))
                        {
                            await conn.OpenAsync();
                            connectionSuccess = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteException(ex);
                    }
                });

            if (!connectionSuccess)
            {
                AnsiConsole.MarkupLine("[red]데이터베이스 연결에 실패하였습니다. 종료합니다.[/]");
                return;
            }

            // 로그인 정보 성공 시 저장
            SessionManager.SaveLastUsedUserId(userId);
            AnsiConsole.MarkupLine("[green]데이터베이스 연결 성공![/]");
        }
    }
}
```

- [x] **Step 3: CLI 실행 테스트를 수동 빌드하여 에러 확인**

실행: `dotnet build`
Expected: 빌드가 성공적으로 완료되어야 함.

- [x] **Step 4: 로컬에서 CLI를 임시 가동하여 화면 출력 확인**

실행: `dotnet run --project src/SpAnalyzer.Cli`
Expected: 타이틀 출력 및 DB 계정/비밀번호 프롬프트 표시 확인.

- [x] **Step 5: Git Commit**

실행:
```bash
git add src/SpAnalyzer.Cli/appsettings.json src/SpAnalyzer.Cli/instructions.txt src/SpAnalyzer.Cli/Program.cs
git commit -m "feat: setup appsettings.json and implement TUI database login prompt"
```

---

### Task 7: SP 검색/자동완성 및 의존성 추출/AI 호출 메인 루프 연동

**Files:**
- Modify: `src/SpAnalyzer.Cli/Program.cs`

- [ ] **Step 1: Program.cs에 메인 루프 (SP 조회, 자동완성 선택, 로딩 바, AI 처리) 연동 코드를 추가**

파일 수정: `src/SpAnalyzer.Cli/Program.cs` 내의 `Main` 함수 뒷부분("데이터베이스 연결 성공!" 문구 아래)을 다음과 같이 수정합니다.
```csharp
            // 3. 서비스 구성
            IDbMetadataService dbService = new DbMetadataService();
            
            var provider = configuration["AiSettings:Provider"] ?? "OpenAI";
            var modelName = configuration["AiSettings:ModelName"] ?? "gpt-4o";
            var apiKey = configuration["AiSettings:ApiKey"] ?? string.Empty;
            var endpoint = configuration["AiSettings:Endpoint"] ?? string.Empty;
            var tempStr = configuration["AiSettings:Temperature"] ?? "0.2";
            float.TryParse(tempStr, out float temp);

            IAiService aiService = new AiService(provider, modelName, apiKey, endpoint, temp);

            var outputDir = configuration["OutputSettings:Directory"] ?? "./output";
            var instructionsFile = configuration["OutputSettings:InstructionsFile"] ?? "./instructions.txt";

            // 4. Stored Procedure 목록 로드
            List<string> spNames = new();
            await AnsiConsole.Status()
                .StartAsync("Stored Procedure 목록 로드 중...", async ctx =>
                {
                    try
                    {
                        spNames = await dbService.GetStoredProcedureNamesAsync(connectionString);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine("[red]SP 목록 조회 중 오류 발생:[/]");
                        AnsiConsole.WriteException(ex);
                    }
                });

            if (spNames.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]조회된 Stored Procedure가 없습니다. 종료합니다.[/]");
                return;
            }

            // 5. SP 선택 루프 시작
            while (true)
            {
                var exitOption = "-- 종료 (Exit) --";
                var choices = new List<string>(spNames) { exitOption };

                var selectedOption = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\n분석할 [green]Stored Procedure[/]를 선택하거나 검색하세요 (종료하려면 맨 아래 선택):")
                        .PageSize(12)
                        .MoreChoicesText("[grey](더 많은 목록은 방향키를 누르세요)[/]")
                        .AddChoices(choices)
                        .EnableSearch() // 실시간 자동완성 검색 기능 활성화
                );

                if (selectedOption == exitOption)
                {
                    AnsiConsole.MarkupLine("[blue]도구를 종료합니다.[/]");
                    break;
                }

                // 선택된 SP 파싱 (스키마.이름)
                var parts = selectedOption.Split('.', 2);
                var schema = parts[0];
                var name = parts[1];

                SpAnalyzer.Core.Models.SpDefinition spDef = null;
                string specificationMarkdown = string.Empty;

                bool processSuccess = false;

                // 6. DB 조회 및 AI 통신 진행 상황 표시
                await AnsiConsole.Status()
                    .StartAsync($"[yellow]{selectedOption}[/] 분석 프로세스 가동 중...", async ctx =>
                    {
                        try
                        {
                            ctx.Status($"[yellow]{selectedOption}[/] - DB 메타데이터 및 의존성 분석 중...");
                            spDef = await dbService.GetSpDetailsAsync(connectionString, schema, name);

                            ctx.Status($"[yellow]{selectedOption}[/] - AI 리버스 엔지니어링 수행 중 ({provider})...");
                            
                            // 지침 문서 로드 (누락 시 Fallback 로직 적용)
                            string instructions = "기본 마크다운 규칙을 적용하여 분석해 주세요.";
                            if (File.Exists(instructionsFile))
                            {
                                instructions = await File.ReadAllTextAsync(instructionsFile);
                            }
                            
                            specificationMarkdown = await aiService.GenerateSpecificationAsync(spDef, instructions);
                            processSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]분석 도중 실패:[/] {ex.Message}");
                        }
                    });

                if (!processSuccess || string.IsNullOrEmpty(specificationMarkdown))
                {
                    AnsiConsole.MarkupLine("[red]리버스 엔지니어링 분석에 실패했습니다. 다시 시도해 주세요.[/]");
                    continue;
                }

                // 7. 결과 저장 및 화면 표시
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
                await File.WriteAllTextAsync(outputFileName, specificationMarkdown);

                AnsiConsole.Write(new Panel(new Markup($"[green]성공적으로 파일이 생성되었습니다![/]\n[bold]저장 경로:[/] {outputFileName}"))
                {
                    Border = BoxBorder.Rounded,
                    Header = new PanelHeader($" {selectedOption} 분석 완료 ")
                });
            }
```

- [ ] **Step 2: 전체 코드 빌드 및 무결성 테스트 실행**

실행: `dotnet test`
Expected: 모든 단위 테스트 통과 (PASS)

- [ ] **Step 3: CLI 어플리케이션 컴파일 빌드**

실행: `dotnet build`
Expected: Build Success

- [ ] **Step 4: 로컬 실행 및 시나리오 수동 테스트 확인**

실행: `dotnet run --project src/SpAnalyzer.Cli`
Expected: DB 계정/암호 입력 후 정상적으로 연결 성공 시 SP 자동완성 선택창 제공, 드롭다운 필터링, 로딩 스피너 및 마크다운 파일 생성이 정상 루프를 도는지 확인.

- [ ] **Step 5: Git Commit**

실행:
```bash
git add src/SpAnalyzer.Cli/Program.cs
git commit -m "feat: complete interactive CLI workflow with auto-complete, spinner, and markdown exporter"
```
