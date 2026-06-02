using System;
using System.Collections.Generic;
using System.IO;
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

            string connectionString = string.Empty;
            string? userId = null;

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
            }
            else
            {
                // 대화형 TUI 모드
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
            }

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

            // TUI 모드인 경우 로그인 정보 성공 시 저장
            if (!cliArgs.IsBatchMode && userId != null)
            {
                SessionManager.SaveLastUsedUserId(userId);
            }
            AnsiConsole.MarkupLine("[green]데이터베이스 연결 성공![/]");

            // 4. 서비스 구성
            IDbMetadataService dbService = new DbMetadataService();
            IAiService aiService = new AiService(provider, modelName, apiKey, endpoint, temp);
            IMetadataExporter metadataExporter = new MetadataExporter();
            var validator = new MechanicalValidator();
            var userInteraction = new ConsoleUserInteraction();
            var orchestrator = new VerificationPipelineOrchestrator(dbService, aiService, validator, userInteraction);

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
                            connectionString, schema, name, maxDepth, provider, instructions, isBatchMode: true);

                        if (string.IsNullOrEmpty(specMarkdown))
                        {
                            throw new Exception("검증 파이프라인을 통과한 명세서 획득 실패");
                        }

                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        await SaveOutputsAsync(
                            spDef, specMarkdown, outputDir, instructionsFile,
                            metadataExporter, saveRawJson, saveRawContext, saveRawFiles,
                            schema, name);

                        AnsiConsole.MarkupLine($"[green]성공:[/] {selectedOption} 분석 완료 및 저장!");
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

                    var (specMarkdown, spDef) = await orchestrator.RunPipelineAsync(
                        connectionString, schema, name, maxDepth, provider, instructions, isBatchMode: false);

                    if (string.IsNullOrEmpty(specMarkdown))
                    {
                        AnsiConsole.MarkupLine("[red]분석이 중단되었거나 명세서 생성에 실패했습니다.[/]");
                        continue;
                    }

                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    await SaveOutputsAsync(
                        spDef, specMarkdown, outputDir, instructionsFile,
                        metadataExporter, saveRawJson, saveRawContext, saveRawFiles,
                        schema, name);

                    var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
                    AnsiConsole.Write(new Panel(new Markup($"[green]성공적으로 파일이 생성되었습니다![/]\n[bold]저장 경로:[/] {Markup.Escape(outputFileName)}"))
                    {
                        Border = BoxBorder.Rounded,
                        Header = new PanelHeader($" {selectedOption} 분석 완료 ")
                    });
                }
            }
        }


        private static async Task SaveOutputsAsync(
            SpAnalyzer.Core.Models.SpDefinition? spDef,
            string specMarkdown,
            string outputDir,
            string instructionsFile,
            IMetadataExporter metadataExporter,
            bool saveRawJson,
            bool saveRawContext,
            bool saveRawFiles,
            string schema,
            string name)
        {
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

            var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
            await File.WriteAllTextAsync(outputFileName, specMarkdown);
        }
    }
}
