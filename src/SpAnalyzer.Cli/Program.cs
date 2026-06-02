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

            var instructionsFile = configuration["OutputSettings:InstructionsFile"] ?? "instructions.txt";
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

            var validator = new MechanicalValidator();

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

                        var (specMarkdown, spDef) = await RunVerificationPipelineAsync(
                            dbService, aiService, validator, connectionString,
                            schema, name, maxDepth, provider, instructionsFile, isBatchMode: true);

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

                    var (specMarkdown, spDef) = await RunVerificationPipelineAsync(
                        dbService, aiService, validator, connectionString,
                        schema, name, maxDepth, provider, instructionsFile, isBatchMode: false);

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
                    AnsiConsole.Write(new Panel(new Markup($"[green]성공적으로 파일이 생성되었습니다![/]\n[bold]저장 경로:[/] {outputFileName}"))
                    {
                        Border = BoxBorder.Rounded,
                        Header = new PanelHeader($" {selectedOption} 분석 완료 ")
                    });
                }
            }
        }

        private static async Task<(string? SpecMarkdown, SpAnalyzer.Core.Models.SpDefinition? SpDef)> RunVerificationPipelineAsync(
            IDbMetadataService dbService,
            IAiService aiService,
            MechanicalValidator validator,
            string connectionString,
            string schema,
            string name,
            int maxDepth,
            string provider,
            string instructionsFile,
            bool isBatchMode)
        {
            var selectedOption = $"{schema}.{name}";
            SpAnalyzer.Core.Models.SpDefinition? spDef = null;

            await AnsiConsole.Status()
                .StartAsync($"[yellow]{selectedOption}[/] - DB 메타데이터 및 의존성 분석 중 (최대 깊이: {maxDepth}단계)...", async ctx =>
                {
                    try
                    {
                        spDef = await dbService.GetSpDetailsAsync(connectionString, schema, name, maxDepth);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]{selectedOption} - DB 조회 실패:[/] {ex.Message}");
                    }
                });

            if (spDef == null)
            {
                return (null, null);
            }

            string instructions = "기본 마크다운 규칙을 적용하여 분석해 주세요.";
            if (File.Exists(instructionsFile))
            {
                instructions = await File.ReadAllTextAsync(instructionsFile);
            }

            string? feedbackLog = null;
            string specificationMarkdown = string.Empty;

            // 최대 2회 시도 (1차 생성 + L1/L2 오류 시 1회 자가 보완)
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var attemptText = attempt == 1 ? "1차 분석" : "자가 수정 보완";
                bool genSuccess = false;

                await AnsiConsole.Status()
                    .StartAsync($"[yellow]{selectedOption}[/] - AI 리버스 엔지니어링 수행 중 ({provider}) [{attemptText}]...", async ctx =>
                    {
                        try
                        {
                            specificationMarkdown = await aiService.GenerateSpecificationAsync(spDef, instructions, feedbackLog);
                            genSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]{selectedOption} - AI 분석 실패 (시도 {attempt}):[/] {ex.Message}");
                        }
                    });

                if (!genSuccess || string.IsNullOrEmpty(specificationMarkdown))
                {
                    return (null, spDef);
                }

                // L1: 기계적 무결성 검사
                var l1Result = validator.Validate(specificationMarkdown);
                if (!l1Result.IsValid)
                {
                    AnsiConsole.MarkupLine($"[yellow]{selectedOption} - [L1 기계 검증] 문법/구조 오류 발견 (시도 {attempt}/2):[/]");
                    foreach (var err in l1Result.Errors)
                    {
                        AnsiConsole.MarkupLine($"  [red]=> {err}[/]");
                    }

                    if (attempt < 2)
                    {
                        feedbackLog = l1Result.SuggestedPromptFix;
                        continue;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]{selectedOption} - [L1 기계 검증] 최종 보완 실패. 마지막 작성 버전을 사용합니다.[/]");
                        break;
                    }
                }

                // L2: AI 교차 리뷰
                ReviewResult? l2Result = null;
                bool reviewSuccess = false;

                await AnsiConsole.Status()
                    .StartAsync($"[yellow]{selectedOption}[/] - AI 교차 리뷰 분석 중 ({provider})...", async ctx =>
                    {
                        try
                        {
                            l2Result = await aiService.ReviewSpecificationAsync(spDef, specificationMarkdown);
                            reviewSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]{selectedOption} - AI 교차 리뷰 실패 (시도 {attempt}):[/] {ex.Message}");
                        }
                    });

                if (reviewSuccess && l2Result != null && l2Result.HasDefects)
                {
                    AnsiConsole.MarkupLine($"[yellow]{selectedOption} - [L2 AI 리뷰] 결함 및 보완 권고 발견 (시도 {attempt}/2):[/]");
                    AnsiConsole.MarkupLine($"  [red]=> {l2Result.FeedbackComment}[/]");

                    if (attempt < 2)
                    {
                        feedbackLog = $"[L2 AI 리뷰 피드백]: 다음 결함/누락사항이 지적되었습니다. 전면 반영해서 수정해 주십시오.\n{l2Result.FeedbackComment}";
                        continue;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]{selectedOption} - [L2 AI 리뷰] 최종 보완 실패. 마지막 리뷰 반영 버전을 사용합니다.[/]");
                        break;
                    }
                }

                // 검증을 통과한 경우 루프 탈출
                if (l1Result.IsValid && (l2Result == null || !l2Result.HasDefects))
                {
                    AnsiConsole.MarkupLine($"[green]{selectedOption} - [L1/L2 자동 검증] 모두 통과![/]");
                    break;
                }
            }

            // L3: 인간 개입형 승인 (TUI 모드 한정)
            if (!isBatchMode)
            {
                while (true)
                {
                    AnsiConsole.WriteLine();
                    var menuChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title($"[bold blue]{selectedOption} 명세서 검증 완료.[/] 다음 작업을 선택하세요:")
                            .AddChoices(new[] { "1. 승인 및 최종 저장 (Approve)", "2. 추가 보완 요청 피드백 입력 (Feedback)", "3. 저장 없이 이탈 (Cancel)" })
                    );

                    if (menuChoice.StartsWith("1"))
                    {
                        return (specificationMarkdown, spDef);
                    }
                    else if (menuChoice.StartsWith("3"))
                    {
                        return (null, spDef);
                    }
                    else if (menuChoice.StartsWith("2"))
                    {
                        var userFeedback = AnsiConsole.Prompt(
                            new TextPrompt<string>("보완할 피드백 내용을 구체적으로 기재해 주십시오:")
                        );

                        if (string.IsNullOrWhiteSpace(userFeedback))
                        {
                            AnsiConsole.MarkupLine("[yellow]피드백이 비어있어 승인 여부 선택 메뉴로 복귀합니다.[/]");
                            continue;
                        }

                        AnsiConsole.MarkupLine("[blue]사용자 피드백을 적용하여 보완 분석 프로세스를 재가동합니다...[/]");
                        var humanFeedbackLog = $"[L3 사용자 보완 피드백 로그]:\n{userFeedback}";

                        string reSpec = string.Empty;
                        await AnsiConsole.Status()
                            .StartAsync($"[yellow]{selectedOption}[/] - 피드백 반영 재생성 중...", async ctx =>
                            {
                                try
                                {
                                    reSpec = await aiService.GenerateSpecificationAsync(spDef, instructions, humanFeedbackLog);
                                }
                                catch (Exception ex)
                                {
                                    AnsiConsole.MarkupLine($"[red]피드백 반영 재생성 실패:[/] {ex.Message}");
                                }
                            });

                        if (string.IsNullOrEmpty(reSpec))
                        {
                            continue;
                        }

                        // 피드백 반영본에 대한 L1 정적 검사 1회 수행
                        var l1Re = validator.Validate(reSpec);
                        if (!l1Re.IsValid)
                        {
                            AnsiConsole.MarkupLine("[yellow]피드백 적용본에서 정적 에러가 검출되어 AI 자가 수정 1회 더 진행합니다.[/]");
                            try
                            {
                                reSpec = await aiService.GenerateSpecificationAsync(spDef, instructions, l1Re.SuggestedPromptFix);
                            }
                            catch { }
                        }

                        return (reSpec, spDef);
                    }
                }
            }

            return (specificationMarkdown, spDef);
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
                    AnsiConsole.MarkupLine($"[yellow]원천 산출물(Raw Metadata) 저장 중 경고:[/] {ex.Message}");
                }
            }

            var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
            await File.WriteAllTextAsync(outputFileName, specMarkdown);
        }
    }
}
