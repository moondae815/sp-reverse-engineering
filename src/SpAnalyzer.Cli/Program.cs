using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Cli
{
    public class Program
    {
        public static CliArgs ParseCommandLineArgs(string[] args)
        {
            var cliArgs = new CliArgs();
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

        static async Task Main(string[] args)
        {
            // 0. 취소 토큰 소스 생성 및 Ctrl+C 이벤트 바인딩
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                AnsiConsole.MarkupLine("\n[red]사용자에 의해 작업 취소 요청이 발생했습니다. 안전하게 정리 중...[/]");
                cts.Cancel();
                e.Cancel = true; // 프로세스 즉시 종료 방지 및 OperationCanceledException 유도
            };

            // 1. 커맨드라인 아규먼트 및 환경 변수 파싱
            var cliArgs = ParseCommandLineArgs(args);

            // 2. 설정 로드
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var server = configuration["DatabaseSettings:Server"] ?? "localhost";
            var database = configuration["DatabaseSettings:Database"] ?? "master";

            // 3. 서비스 구성 변수 준비
            var provider = configuration["AiSettings:Provider"] ?? "OpenAI";
            var modelName = configuration["AiSettings:ModelName"] ?? "gpt-4o";
            var apiKey = configuration["AiSettings:ApiKey"] ?? string.Empty;
            var endpoint = configuration["AiSettings:Endpoint"] ?? string.Empty;
            var tempStr = configuration["AiSettings:Temperature"] ?? "0.2";
            float.TryParse(tempStr, out float temp);

            var depthStr = configuration["DatabaseSettings:MaxDependencyDepth"] ?? "3";
            int.TryParse(depthStr, out int maxDepth);

            var outputDir = configuration["OutputSettings:Directory"] ?? "./output";
            if (!Path.IsPathRooted(outputDir))
            {
                outputDir = Path.Combine(AppContext.BaseDirectory, outputDir);
            }

            var instructionsFile = configuration["OutputSettings:InstructionsFile"] ?? "instructions.md";
            if (!Path.IsPathRooted(instructionsFile))
            {
                instructionsFile = Path.Combine(AppContext.BaseDirectory, instructionsFile);
            }

            bool.TryParse(configuration["OutputSettings:SaveRawJson"] ?? "false", out bool saveRawJson);
            bool.TryParse(configuration["OutputSettings:SaveRawContext"] ?? "false", out bool saveRawContext);
            bool.TryParse(configuration["OutputSettings:SaveRawFiles"] ?? "false", out bool saveRawFiles);

            bool.TryParse(configuration["MigrationSettings:Enabled"] ?? "true", out bool migrationEnabled);
            var targetLanguage = configuration["MigrationSettings:TargetLanguage"] ?? "C#";

            string connectionString = string.Empty;
            string? userId = null;

            bool connectionSuccess = false;

            if (cliArgs.IsBatchMode)
            {
                // 배치 모드
                AnsiConsole.MarkupLine("[bold blue]=== 배치 모드 자동 분석 시작 ===[/]");
                if (string.IsNullOrEmpty(cliArgs.ConnectionString))
                {
                    AnsiConsole.MarkupLine("[red]에러: 배치 모드 실행 시 연결 문자열(--conn 또는 SP_ANALYZER_CONN_STR 환경 변수)은 필수입니다.[/]");
                    return;
                }
                connectionString = cliArgs.ConnectionString;

                // 연결 테스트
                await AnsiConsole.Status()
                    .StartAsync("데이터베이스 연결 시도 중...", async ctx =>
                    {
                        try
                        {
                            using (var conn = new SqlConnection(connectionString))
                            {
                                await conn.OpenAsync(cts.Token);
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
            }
            else
            {
                // 대화형 TUI 모드 - 로그인 성공 시까지 루프
                while (true)
                {
                    AnsiConsole.Clear();
                    AnsiConsole.Write(new FigletText("SP Analyzer").Color(Color.Green));
                    AnsiConsole.WriteLine("SQL Server Stored Procedure Reverse Engineering Tool");
                    AnsiConsole.WriteLine();

                    AnsiConsole.MarkupLine($"[bold blue]서버:[/] {server}");
                    AnsiConsole.MarkupLine($"[bold blue]DB:[/] {database}");
                    AnsiConsole.WriteLine();

                    // 대화형 ID/비밀번호 로그인 처리
                    var lastUserId = SessionManager.LoadLastUsedUserId();
                    userId = AnsiConsole.Prompt(
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
                    connectionString = connStrBuilder.ConnectionString;

                    // 연결 테스트
                    string? loginError = null;
                    await AnsiConsole.Status()
                        .StartAsync("데이터베이스 연결 시도 중...", async ctx =>
                        {
                            try
                            {
                                using (var conn = new SqlConnection(connectionString))
                                {
                                    await conn.OpenAsync(cts.Token);
                                    connectionSuccess = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                loginError = ex.Message;
                            }
                        });

                    if (connectionSuccess)
                    {
                        if (userId != null)
                        {
                            SessionManager.SaveLastUsedUserId(userId);
                        }
                        break;
                    }

                    AnsiConsole.MarkupLine("[red]로그인에 실패하였습니다. 계정 정보 또는 비밀번호를 확인해 주세요.[/]");
                    if (!string.IsNullOrEmpty(loginError))
                    {
                        AnsiConsole.MarkupLine($"[grey](오류 상세: {Markup.Escape(loginError)})[/]");
                    }
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]아무 키나 누르면 로그인 화면으로 돌아갑니다...[/]");
                    Console.ReadKey(true);
                }
            }

            AnsiConsole.MarkupLine("[green]데이터베이스 연결 성공![/]");

            // 4. 서비스 구성
            IDbMetadataService dbService = new DbMetadataService();
            IAiService aiService = new AiService(provider, modelName, apiKey, endpoint, temp);
            IMetadataExporter metadataExporter = new MetadataExporter();
            bool.TryParse(configuration["ValidationSettings:UseMermaidCli"] ?? "false", out bool useMermaidCli);
            var validator = new MechanicalValidator(useMermaidCli);
            var userInteraction = new ConsoleUserInteraction();
            var maxL2Attempts = configuration["AiSettings:MaxL2Attempts"] ?? "1";
            var orchestrator = new VerificationPipelineOrchestrator(dbService, aiService, validator, userInteraction, maxL2Attempts, modelName);

            string instructions = "기본 마크다운 규칙을 적용하여 분석해 주세요.";
            if (File.Exists(instructionsFile))
            {
                instructions = await File.ReadAllTextAsync(instructionsFile);
            }

            // 5. Stored Procedure 목록 로드
            List<string> spNames = new();
            await AnsiConsole.Status()
                .StartAsync("Stored Procedure 목록 로드 중...", async ctx =>
                {
                    try
                    {
                        spNames = await dbService.GetStoredProcedureNamesAsync(connectionString, cts.Token);
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

            if (cliArgs.IsBatchMode)
            {
                // 배치 모드 실행 흐름
                List<string> targetSps = new();
                if (cliArgs.AnalyzeAll)
                {
                    targetSps.AddRange(spNames);
                }
                else
                {
                    foreach (var target in cliArgs.TargetProcedures)
                    {
                        string? matchedSp = null;
                        if (target.Contains('.'))
                        {
                            matchedSp = spNames.Find(x => x.Equals(target, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            matchedSp = spNames.Find(x =>
                            {
                                var parts = x.Split('.', 2);
                                var nameOnly = parts.Length > 1 ? parts[1] : parts[0];
                                return nameOnly.Equals(target, StringComparison.OrdinalIgnoreCase);
                            });
                        }

                        if (matchedSp != null)
                        {
                            targetSps.Add(matchedSp);
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[yellow]경고: 입력된 SP '{target}'를 DB에서 찾을 수 없어 건너뜁니다.[/]");
                        }
                    }
                }

                if (targetSps.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]분석 대상 Stored Procedure가 없습니다. 종료합니다.[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[bold blue]총 {targetSps.Count}개의 Stored Procedure 분석 시작...[/]");

                foreach (var selectedOption in targetSps)
                {
                    // 각 SP 처리 단위 예외 격리
                    try
                    {
                        var parts = selectedOption.Split('.', 2);
                        var schema = parts[0];
                        var name = parts[1];

                        var (specMarkdown, spDef) = await orchestrator.RunPipelineAsync(
                            connectionString, schema, name, maxDepth, provider, instructions, isBatchMode: true, cts.Token);

                        if (string.IsNullOrEmpty(specMarkdown))
                        {
                            throw new Exception("검증 파이프라인을 통과한 명세서 획득 실패");
                        }

                        string? migrationPlan = null;
                        if (migrationEnabled && spDef != null)
                        {
                            AnsiConsole.MarkupLine($"[yellow]{schema}.{name}[/] - 배치 전환 계획 설계서 작성 중 ({targetLanguage})...");
                            migrationPlan = await aiService.GenerateBatchMigrationPlanAsync(spDef, targetLanguage, cts.Token);
                        }

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        await SaveOutputsAsync(
                            spDef, specMarkdown, migrationPlan, outputDir, instructionsFile,
                            metadataExporter, saveRawJson, saveRawContext, saveRawFiles,
                            schema, name, provider, modelName);

                        AnsiConsole.MarkupLine($"[green]성공:[/] {selectedOption} 분석 완료 및 저장!");
                    }
                    catch (OperationCanceledException)
                    {
                        AnsiConsole.MarkupLine("\n[red]사용자에 의해 배치 분석 작업이 중단되었습니다. 프로세스를 종료합니다.[/]");
                        break;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]실패:[/] {selectedOption} 분석 중 오류 발생: {ex.Message}");
                    }
                }

                AnsiConsole.MarkupLine("[bold green]=== 배치 모드 자동 분석 완료 ===[/]");
            }
            else
            {
                // 대화형 TUI 모드 실행
                while (true)
                {
                    var choicesMenu = new[]
                    {
                        "1. Stored Procedure 개별 분석 명세서 작성",
                        "2. 기분석 명세서 통합 배치 전환 계획 수립 (Multi-SP)",
                        "3. 종료 (Exit)"
                    };

                    var selectedMenu = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold green]=== SP Analyzer 메인 메뉴 ===[/]")
                            .AddChoices(choicesMenu)
                    );

                    if (selectedMenu.StartsWith("3"))
                    {
                        AnsiConsole.MarkupLine("[blue]도구를 종료합니다.[/]");
                        break;
                    }
                    else if (selectedMenu.StartsWith("1"))
                    {
                        var exitOption = "-- 메인 메뉴로 돌아가기 --";
                        var choices = new List<string>(spNames) { exitOption };

                        var selectedOption = AnsiConsole.Prompt(
                            new SelectionPrompt<string>()
                                .Title("\n분석할 [green]Stored Procedure[/]를 선택하거나 검색하세요:")
                                .PageSize(12)
                                .MoreChoicesText("[grey](더 많은 목록은 방향키를 누르세요)[/]")
                                .UseConverter(x => Markup.Escape(x))
                                .AddChoices(choices)
                                .EnableSearch()
                        );

                        if (selectedOption == exitOption)
                        {
                            continue;
                        }

                        var parts = selectedOption.Split('.', 2);
                        var schema = parts[0];
                        var name = parts[1];

                        try
                        {
                            var (specMarkdown, spDef) = await orchestrator.RunPipelineAsync(
                                connectionString, schema, name, maxDepth, provider, instructions, isBatchMode: false, cts.Token);

                            if (string.IsNullOrEmpty(specMarkdown))
                            {
                                AnsiConsole.MarkupLine("[red]분석이 중단되었거나 명세서 생성에 실패했습니다.[/]");
                                continue;
                            }

                            // 분석과 전환 분리 요구에 따라, 개별 분석 시에는 배치 전환 설계서를 생성하지 않음 (null 지정)
                            string? migrationPlan = null;

                            if (!Directory.Exists(outputDir))
                            {
                                Directory.CreateDirectory(outputDir);
                            }

                            await SaveOutputsAsync(
                                spDef, specMarkdown, migrationPlan, outputDir, instructionsFile,
                                metadataExporter, saveRawJson, saveRawContext, saveRawFiles,
                                schema, name, provider, modelName);

                            var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
                            AnsiConsole.Write(new Panel(new Markup($"[green]성공적으로 파일이 생성되었습니다![/]\n[bold]저장 경로:[/] {Markup.Escape(outputFileName)}"))
                            {
                                Border = BoxBorder.Rounded,
                                Header = new PanelHeader($" {selectedOption} 분석 완료 ")
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            AnsiConsole.MarkupLine("\n[yellow]분석 작업이 사용자에 의해 중단되었습니다. 메인 메뉴로 돌아갑니다.[/]");
                            AnsiConsole.WriteLine();
                            AnsiConsole.MarkupLine("[yellow]아무 키나 누르면 계속합니다...[/]");
                            Console.ReadKey(true);
                        }
                    }
                    else if (selectedMenu.StartsWith("2"))
                    {
                        if (!Directory.Exists(outputDir))
                        {
                            AnsiConsole.MarkupLine("[yellow]경고: 출력 디렉터리가 존재하지 않거나 분석서가 없습니다. 먼저 1번 메뉴로 분석을 진행하세요.[/]");
                            continue;
                        }

                        var specFiles = Directory.GetFiles(outputDir, "*_Spec.md");
                        if (specFiles.Length == 0)
                        {
                            AnsiConsole.MarkupLine("[yellow]경고: 출력 디렉터리에 기분석된 명세서(*_Spec.md)가 존재하지 않습니다.[/]");
                            continue;
                        }

                        var backOption = "[-- 메인 메뉴로 돌아가기 --]";
                        var fileChoices = new List<string> { backOption };
                        foreach (var file in specFiles)
                        {
                            fileChoices.Add(Path.GetFileName(file));
                        }

                        var selectedFiles = AnsiConsole.Prompt(
                            new MultiSelectionPrompt<string>()
                                .Title("통합 배치 Job으로 전환할 [green]명세서 파일들[/]을 스페이스바로 선택하세요:")
                                .Required()
                                .PageSize(10)
                                .MoreChoicesText("[grey](더 많은 목록은 방향키를 누르세요)[/]")
                                .UseConverter(x => Markup.Escape(x))
                                .AddChoices(fileChoices)
                        );

                        if (selectedFiles.Contains(backOption))
                        {
                            continue;
                        }

                        if (selectedFiles.Count == 0)
                        {
                            continue;
                        }

                        var specsData = new List<(string FileName, string Content)>();
                        foreach (var fileName in selectedFiles)
                        {
                            var fullPath = Path.Combine(outputDir, fileName);
                            var content = await File.ReadAllTextAsync(fullPath);
                            specsData.Add((fileName, content));
                        }

                        var jobName = AnsiConsole.Prompt(
                            new TextPrompt<string>("생성할 통합 배치 Job의 이름을 입력하세요:")
                                .DefaultValue("Consolidated_Batch_Job")
                        );

                        string? consolidatedPlan = null;
                        try
                        {
                            consolidatedPlan = await orchestrator.RunConsolidatedPipelineAsync(specsData, targetLanguage, jobName, provider, cts.Token);
                            if (string.IsNullOrEmpty(consolidatedPlan))
                            {
                                AnsiConsole.MarkupLine("[red]통합 배치 설계서 작성이 중단되었거나 실패했습니다.[/]");
                                continue;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            AnsiConsole.MarkupLine("\n[yellow]통합 설계서 수립 작업이 사용자에 의해 중단되었습니다. 메인 메뉴로 돌아갑니다.[/]");
                            AnsiConsole.WriteLine();
                            AnsiConsole.MarkupLine("[yellow]아무 키나 누르면 계속합니다...[/]");
                            Console.ReadKey(true);
                            continue;
                        }

                        var planFileName = Path.Combine(outputDir, $"{jobName}_BatchMigrationPlan.md");
                        var metadataHeader = $"> [!NOTE]\n> **문서 작성일시**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n> **분석 AI 정보**: {provider} ({modelName})\n\n";
                        await File.WriteAllTextAsync(planFileName, metadataHeader + consolidatedPlan);
                        AnsiConsole.Write(new Panel(new Markup($"[green]통합 배치 설계서가 성공적으로 생성되었습니다![/]\n[bold]저장 경로:[/] {Markup.Escape(planFileName)}"))
                        {
                            Border = BoxBorder.Rounded,
                            Header = new PanelHeader($" {jobName} 통합 마이그레이션 완료 ")
                        });
                    }
                }
            }
        }


        private static async Task SaveOutputsAsync(
            SpAnalyzer.Core.Models.SpDefinition? spDef,
            string specMarkdown,
            string? migrationPlan,
            string outputDir,
            string instructionsFile,
            IMetadataExporter metadataExporter,
            bool saveRawJson,
            bool saveRawContext,
            bool saveRawFiles,
            string schema,
            string name,
            string provider,
            string modelName)
        {
            if (spDef != null)
            {
                try
                {
                    var dependenciesText = new System.Text.StringBuilder();
                    var tableSchemasText = new System.Text.StringBuilder();
                    var referenceDdlsText = new System.Text.StringBuilder();
                    var warningsText = new System.Text.StringBuilder();

                    if (spDef.Warnings.Count > 0)
                    {
                        warningsText.AppendLine("[DB 메타데이터 수집 중 발생한 경고/오류 목록]");
                        foreach (var warn in spDef.Warnings)
                        {
                            warningsText.AppendLine($"- {warn}");
                        }
                        warningsText.AppendLine();
                    }

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

{warningsText}
[수집된 DB 메타데이터 의존관계 목록]
{dependenciesText}

[의존하는 참조 테이블 상세 스키마 정보]
{tableSchemasText}

[의존하는 참조 UDF/SP 소스 코드]
{referenceDdlsText}

[Stored Procedure DDL SQL 원본]
{spDef.DdlText}
";
                    await metadataExporter.ExportRawMetadataAsync(
                        spDef,
                        rawPromptContext,
                        outputDir,
                        saveRawJson,
                        saveRawContext,
                        saveRawFiles);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]원천 산출물(Raw Metadata) 저장 중 경고:[/] {Markup.Escape(ex.Message)}");
                }
            }

            var metadataHeader = $"> [!NOTE]\n> **문서 작성일시**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n> **분석 AI 정보**: {provider} ({modelName})\n\n";

            var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
            await File.WriteAllTextAsync(outputFileName, metadataHeader + specMarkdown);

            if (!string.IsNullOrEmpty(migrationPlan))
            {
                var planFileName = Path.Combine(outputDir, $"{schema}.{name}_BatchMigrationPlan.md");
                await File.WriteAllTextAsync(planFileName, metadataHeader + migrationPlan);
            }
        }
    }
}
