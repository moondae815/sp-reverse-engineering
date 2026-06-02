# Stored Procedure CLI Batch Mode and Mermaid Vis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CLI 명령줄 인자를 파싱하여 비대화형 일괄 분석 모드(배치)를 지원하고, 생성되는 명세서 결과에 비즈니스 흐름을 시각화하는 Mermaid 다이어그램이 자동 포함되도록 시스템 지침 및 템플릿을 고도화합니다.

**Architecture:** `SpAnalyzer.Cli` 하위에 인자 바인딩 모델 `CliArgs` 및 정적 파서를 추가하여 실행 분기를 제어하고, `instructions.txt` 및 `AiService` 프롬프트를 보강해 Mermaid Flowchart 출력을 필수화합니다.

**Tech Stack:** .NET 8.0 / C#, xUnit, Spectre.Console

---

### Task 1: CliArgs 클래스 및 CLI 아규먼트 파싱 헬퍼 구현

**Files:**
- Create: `src/SpAnalyzer.Cli/CliArgs.cs`
- Create: `tests/SpAnalyzer.Core.Tests/CliArgsTests.cs`

- [ ] **Step 1: CLI 옵션 배열 파싱 및 환경 변수 연동이 정상 맵핑되는지 검증하는 xUnit 테스트 작성**

파일 생성: `tests/SpAnalyzer.Core.Tests/CliArgsTests.cs`
```csharp
using System;
using Xunit;
using SpAnalyzer.Cli;

namespace SpAnalyzer.Core.Tests
{
    public class CliArgsTests
    {
        [Fact]
        public void ParseCommandLineArgs_ShouldBindCorrectly()
        {
            // Arrange
            string[] args = new[] { "--conn", "Server=my_server;", "--all", "--sp", "dbo.USP_1,dbo.USP_2" };

            // Act
            CliArgs result = Program.ParseCommandLineArgs(args);

            // Assert
            Assert.Equal("Server=my_server;", result.ConnectionString);
            Assert.True(result.AnalyzeAll);
            Assert.Equal(2, result.TargetProcedures.Count);
            Assert.Equal("dbo.USP_1", result.TargetProcedures[0]);
            Assert.Equal("dbo.USP_2", result.TargetProcedures[1]);
            Assert.True(result.IsBatchMode);
        }
    }
}
```

- [ ] **Step 2: 테스트를 빌드하여 컴파일 실패(Red) 확인**

Run: `dotnet test`
Expected: `CliArgs` 클래스 및 `ParseCommandLineArgs` 메서드가 존재하지 않아 컴파일 빌드 에러 발생.

- [ ] **Step 3: CliArgs 모델 생성 및 Program.cs 내에 ParseCommandLineArgs 정적 메서드 구현**

파일 생성: `src/SpAnalyzer.Cli/CliArgs.cs`
```csharp
using System.Collections.Generic;

namespace SpAnalyzer.Cli
{
    public class CliArgs
    {
        public string? ConnectionString { get; set; }
        public bool AnalyzeAll { get; set; }
        public List<string> TargetProcedures { get; set; } = new();

        public bool IsBatchMode => AnalyzeAll || TargetProcedures.Count > 0;
    }
}
```

파일 수정: `src/SpAnalyzer.Cli/Program.cs` 클래스 내부에 다음 정적 메서드를 삽입합니다. (Program 클래스 선언부에 `using SpAnalyzer.Cli;` 또는 필요한 네임스페이스가 겹치지 않게 조율합니다.)
```csharp
        public static CliArgs ParseCommandLineArgs(string[] args)
        {
            var cliArgs = new CliArgs();

            // 환경 변수 연동
            cliArgs.ConnectionString = Environment.GetEnvironmentVariable("SP_ANALYZER_CONN_STR");

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (arg.Equals("--conn", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    cliArgs.ConnectionString = args[++i];
                }
                else if (arg.Equals("--all", StringComparison.OrdinalIgnoreCase))
                {
                    cliArgs.AnalyzeAll = true;
                }
                else if (arg.Equals("--sp", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var sps = args[++i].Split(',');
                    foreach (var sp in sps)
                    {
                        var trimmed = sp.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            cliArgs.TargetProcedures.Add(trimmed);
                        }
                    }
                }
            }

            return cliArgs;
        }
```

- [ ] **Step 4: 테스트를 재실행하여 빌드 및 패스 검증**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 5: Git Commit**

```bash
git add src/SpAnalyzer.Cli/CliArgs.cs src/SpAnalyzer.Cli/Program.cs tests/SpAnalyzer.Core.Tests/CliArgsTests.cs
git commit -m "feat: add CliArgs model and implementation of ParseCommandLineArgs"
```

---

### Task 2: Program.cs에 CLI 배치 모드 실행 분기 연동

**Files:**
- Modify: `src/SpAnalyzer.Cli/Program.cs`

- [ ] **Step 1: Program.cs 파일에 배치 모드 흐름 바인딩 및 무인 루프 구현**

파일 수정: `src/SpAnalyzer.Cli/Program.cs` 의 `Main` 메서드 본문을 다음과 같이 수정 및 결합하여 대화형/비대화형 분기를 구현합니다.
```csharp
        static async Task Main(string[] args)
        {
            // 1. CLI 아규먼트 파싱 및 배치 여부 판별
            var cliArgs = ParseCommandLineArgs(args);

            if (!cliArgs.IsBatchMode)
            {
                AnsiConsole.Clear();
                AnsiConsole.Write(new FigletText("SP Analyzer").Color(Color.Green));
            }

            AnsiConsole.WriteLine("SQL Server Stored Procedure Reverse Engineering Tool");
            AnsiConsole.WriteLine();

            // 2. 설정 로드
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var server = configuration["DatabaseSettings:Server"] ?? "localhost";
            var database = configuration["DatabaseSettings:Database"] ?? "master";

            string connectionString = string.Empty;

            // 3. 배치 모드 또는 대화형 로그인 분기
            if (cliArgs.IsBatchMode)
            {
                if (string.IsNullOrWhiteSpace(cliArgs.ConnectionString))
                {
                    AnsiConsole.MarkupLine("[red]오류: 배치 모드 실행 시 연결 문자열(--conn 인자 또는 SP_ANALYZER_CONN_STR 환경변수)이 누락되었습니다.[/]");
                    Environment.Exit(1);
                }
                connectionString = cliArgs.ConnectionString;
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold blue]서버:[/] {server}");
                AnsiConsole.MarkupLine($"[bold blue]DB:[/] {database}");
                AnsiConsole.WriteLine();

                var lastUserId = SessionManager.LoadLastUsedUserId();
                var userId = AnsiConsole.Prompt(
                    new TextPrompt<string>("DB 계정을 입력하세요:")
                        .DefaultValue(string.IsNullOrEmpty(lastUserId) ? "sa" : lastUserId)
                );

                var password = AnsiConsole.Prompt(
                    new TextPrompt<string>("DB 비밀번호를 입력하세요:")
                        .Secret()
                );

                var connStrBuilder = new SqlConnectionStringBuilder
                {
                    DataSource = server,
                    InitialCatalog = database,
                    UserID = userId,
                    Password = password,
                    TrustServerCertificate = true,
                    ConnectTimeout = 5
                };
                connectionString = connStrBuilder.ConnectionString;
            }

            // DB 연결 검증
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
                        if (!cliArgs.IsBatchMode) AnsiConsole.WriteException(ex);
                    }
                });

            if (!connectionSuccess)
            {
                AnsiConsole.MarkupLine("[red]데이터베이스 연결에 실패하였습니다. 종료합니다.[/]");
                Environment.Exit(1);
            }

            if (!cliArgs.IsBatchMode)
            {
                var currentUserId = new SqlConnectionStringBuilder(connectionString).UserID;
                SessionManager.SaveLastUsedUserId(currentUserId);
                AnsiConsole.MarkupLine("[green]데이터베이스 연결 성공![/]");
            }

            // 4. 서비스 구성
            IDbMetadataService dbService = new DbMetadataService();
            IMetadataExporter metadataExporter = new MetadataExporter();

            var provider = configuration["AiSettings:Provider"] ?? "OpenAI";
            var modelName = configuration["AiSettings:ModelName"] ?? "gpt-4o";
            var apiKey = configuration["AiSettings:ApiKey"] ?? string.Empty;
            var endpoint = configuration["AiSettings:Endpoint"] ?? string.Empty;
            var tempStr = configuration["AiSettings:Temperature"] ?? "0.2";
            float.TryParse(tempStr, out float temp);

            IAiService aiService = new AiService(provider, modelName, apiKey, endpoint, temp);

            var depthStr = configuration["DatabaseSettings:MaxDependencyDepth"] ?? "3";
            int.TryParse(depthStr, out int maxDepth);

            var outputDir = configuration["OutputSettings:Directory"] ?? "./output";
            if (!Path.IsPathRooted(outputDir))
            {
                outputDir = Path.Combine(AppContext.BaseDirectory, outputDir);
            }

            var instructionsFile = configuration["OutputSettings:InstructionsFile"] ?? "instructions.txt";
            if (!Path.IsPathRooted(instructionsFile))
            {
                instructionsFile = Path.Combine(AppContext.BaseDirectory, instructionsFile);
            }

            bool.TryParse(configuration["OutputSettings:SaveRawJson"] ?? "false", out bool saveRawJson);
            bool.TryParse(configuration["OutputSettings:SaveRawContext"] ?? "false", out bool saveRawContext);
            bool.TryParse(configuration["OutputSettings:SaveRawFiles"] ?? "false", out bool saveRawFiles);

            // 5. SP 목록 획득
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
                        if (!cliArgs.IsBatchMode) AnsiConsole.WriteException(ex);
                    }
                });

            if (spNames.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]조회된 Stored Procedure가 없습니다. 종료합니다.[/]");
                Environment.Exit(1);
            }

            // 6. 실행 루프 분기
            if (cliArgs.IsBatchMode)
            {
                var targetSps = new List<string>();
                if (cliArgs.AnalyzeAll)
                {
                    targetSps.AddRange(spNames);
                }
                else
                {
                    // 대소문자 매칭 및 존재 여부 검증 필터링
                    foreach (var target in cliArgs.TargetProcedures)
                    {
                        var match = spNames.Find(x => x.Equals(target, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            targetSps.Add(match);
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[yellow]경고: 존재하지 않는 SP로 스킵합니다:[/] {target}");
                        }
                    }
                }

                AnsiConsole.MarkupLine($"[blue]배치 처리 모드 시작 (총 {targetSps.Count}개 SP 대상)...[/]");

                foreach (var selectedOption in targetSps)
                {
                    AnsiConsole.MarkupLine($"\n[cyan]진행 중:[/] {selectedOption}");
                    var parts = selectedOption.Split('.', 2);
                    var schema = parts[0];
                    var name = parts[1];

                    try
                    {
                        var spDef = await dbService.GetSpDetailsAsync(connectionString, schema, name, maxDepth);
                        string instructions = "기본 마크다운 규칙을 적용하여 분석해 주세요.";
                        if (File.Exists(instructionsFile))
                        {
                            instructions = await File.ReadAllTextAsync(instructionsFile);
                        }

                        var specMd = await aiService.GenerateSpecificationAsync(spDef, instructions);

                        // 산출물 덤프
                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        // Raw 데이터 세이브
                        try
                        {
                            var rawContext = $"[System Prompt]\n{instructions}\n\n[User Prompt]\n{spDef.DdlText}";
                            await metadataExporter.ExportRawMetadataAsync(spDef, rawContext, outputDir, saveRawJson, saveRawContext, saveRawFiles);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"  [yellow]Raw 덤프 저장 실패:[/] {ex.Message}");
                        }

                        var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
                        await File.WriteAllTextAsync(outputFileName, specMd);
                        AnsiConsole.MarkupLine($"  [green]완료:[/] {outputFileName}");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"  [red]실패:[/] {ex.Message}");
                    }
                }

                AnsiConsole.MarkupLine("[green]모든 배치 처리가 성공적으로 완료되었습니다![/]");
                return;
            }

            // TUI 대화형 모드 진입 (기존 코드 유지)
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
                        .EnableSearch()
                );

                if (selectedOption == exitOption)
                {
                    AnsiConsole.MarkupLine("[blue]도구를 종료합니다.[/]");
                    break;
                }

                var parts = selectedOption.Split('.', 2);
                var schema = parts[0];
                var name = parts[1];

                SpDefinition? spDef = null;
                string specificationMarkdown = string.Empty;
                bool processSuccess = false;

                await AnsiConsole.Status()
                    .StartAsync($"[yellow]{selectedOption}[/] 분석 프로세스 가동 중...", async ctx =>
                    {
                        try
                        {
                            ctx.Status($"[yellow]{selectedOption}[/] - DB 메타데이터 및 의존성 분석 중 (최대 깊이: {maxDepth}단계)...");
                            spDef = await dbService.GetSpDetailsAsync(connectionString, schema, name, maxDepth);

                            ctx.Status($"[yellow]{selectedOption}[/] - AI 리버스 엔지니어링 수행 중 ({provider})...");
                            string instructions = "기본 마크다운 규칙을 적용하여 분석해 주세요.";
                            if (File.Exists(instructionsFile))
                            {
                                instructions = await File.ReadAllTextAsync(instructionsFile);
                            }

                            if (spDef != null)
                            {
                                specificationMarkdown = await aiService.GenerateSpecificationAsync(spDef, instructions);
                                processSuccess = true;
                            }
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

                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                if (spDef != null)
                {
                    try
                    {
                        var dependenciesText = new System.Text.StringBuilder();
                        var tableSchemasText = new System.Text.StringBuilder();
                        var referenceDdlsText = new System.Text.StringBuilder();

                        foreach (var dep in spDef.Dependencies)
                        {
                            dependenciesText.AppendLine($"- Schema: {dep.Schema}, Name: {dep.Name}, Type: {dep.Type} (발견 깊이: {dep.DiscoveryDepth}단계)");
                            if (dep.Columns.Count > 0)
                            {
                                tableSchemasText.AppendLine($"### 테이블: {dep.Schema}.{dep.Name} ({dep.Type})");
                                foreach (var col in dep.Columns)
                                {
                                    tableSchemasText.AppendLine($"| {col.ColumnName} | {col.DataType} | {(col.IsNullable ? "Yes" : "No")} |");
                                }
                            }
                            if (!string.IsNullOrEmpty(dep.ReferencedDdlText))
                            {
                                referenceDdlsText.AppendLine($"### {dep.Type}: {dep.Schema}.{dep.Name}");
                                referenceDdlsText.AppendLine(dep.ReferencedDdlText);
                            }
                        }

                        var rawPromptContext = $@"
[시스템 규칙 지침]
{(File.Exists(instructionsFile) ? await File.ReadAllTextAsync(instructionsFile) : "기본 마크다운 규칙을 적용하여 분석해 주세요.")}

[수집된 DB 메타데이터 의존관계 목록]
{dependenciesText}

[의존하는 참조 테이블 상세 스키마 정보]
{tableSchemasText}

[의존하는 참조 UDF/SP 소스 코드]
{referenceDdlsText}

[Stored Procedure DDL SQL 원본]
{spDef.DdlText}
";
                        await metadataExporter.ExportRawMetadataAsync(spDef, rawPromptContext, outputDir, saveRawJson, saveRawContext, saveRawFiles);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[yellow]원천 산출물(Raw Metadata) 저장 중 경고:[/] {ex.Message}");
                    }
                }

                var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
                await File.WriteAllTextAsync(outputFileName, specificationMarkdown);

                AnsiConsole.Write(new Panel(new Markup($"[green]성공적으로 파일이 생성되었습니다![/]\n[bold]저장 경로:[/] {outputFileName}"))
                {
                    Border = BoxBorder.Rounded,
                    Header = new PanelHeader($" {selectedOption} 분석 완료 ")
                });
            }
        }
```

- [ ] **Step 2: 빌드 테스트 수행 및 기존 단위 테스트 무결성 확인**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 3: Git Commit**

```bash
git add src/SpAnalyzer.Cli/Program.cs
git commit -m "feat: implement CLI batch mode with --all, --sp, and --conn arguments"
```

---

### Task 3: instructions.txt 다이어그램 규칙 추가 및 AiService Mermaid 프롬프트 강화

**Files:**
- Modify: `src/SpAnalyzer.Cli/instructions.txt`
- Modify: `src/SpAnalyzer.Core/Services/AiService.cs`

- [ ] **Step 1: instructions.txt 규칙 보완**

파일 수정: `src/SpAnalyzer.Cli/instructions.txt` 파일 맨 아래 부분에 아래 내용 추가
```text
6. **비즈니스 흐름 시각화 (Mermaid Diagram)**:
   - 비즈니스 로직 설명 장에 해당 Stored Procedure의 전체적인 실행 제어 흐름, 조건 분기, 데이터 변경(CRUD) 시퀀스를 표현하는 **Mermaid Flowchart** 다이어그램을 최소 1개 이상 생성하십시오.
   - 반드시 ````mermaid ... ```` 블록 문법을 사용하십시오.
   - 다이어그램 컴파일 에러를 방지하기 위해 노드 라벨 내 특수문자나 괄호는 반드시 쌍따옴표 문자열로 감싸서 선언하십시오. (예: A["Check Order Status (Update)"])
```

- [ ] **Step 2: AiService 내의 시스템 프롬프트 가이드라인 지침 보강**

파일 수정: `src/SpAnalyzer.Core/Services/AiService.cs`
- `GenerateSpecificationAsync` 내의 `systemPrompt` 문자열을 다음과 같이 보강
```csharp
            // 프롬프트 조립
            var systemPrompt = $@"당신은 SQL Server Stored Procedure 분석 전문가입니다. 다음 규칙을 준수하여 마크다운 기능 명세서를 작성하십시오.

[분석 추가 규칙]
1. 분석 대상 SP 뿐만 아니라 제공된 참조 테이블 스키마 컬럼 정보 및 참조 UDF/SP 소스코드를 모두 참고하여 분석 보고서를 한글로 성실히 작성하십시오.
2. SP 내부에서 참조 테이블의 어떤 컬럼 값을 제어/수정하고 조건식에 쓰는지 파라미터 구조와 매핑하여 작성하십시오.
3. SP에서 호출하는 사용자 정의 함수(UDF)의 연산 알고리즘을 소스코드를 보고 분석하여 비즈니스 로직 요약에 포함시키십시오.
4. SP 내부의 조건 분기(IF-ELSE), 반복 루프(CURSOR, WHILE) 및 참조 테이블 데이터 CRUD 제어 단계를 종합적으로 분석하여 명세서 상에 반드시 1개 이상의 시각적 Mermaid Flowchart 코드를 그리십시오. 노드 ID와 레이블 작성 규칙을 엄격히 준수하여 다이어그램 파싱 에러가 발생하지 않도록 조치하십시오.

[사용자 지침]
{userInstructions}";
```

- [ ] **Step 3: 빌드 및 전체 단위 테스트 검증**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 4: Git Commit**

```bash
git add src/SpAnalyzer.Cli/instructions.txt src/SpAnalyzer.Core/Services/AiService.cs
git commit -m "feat: enforce Mermaid flowchart generation rules in prompts and instructions"
```
