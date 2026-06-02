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
                .SetBasePath(AppContext.BaseDirectory)
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

            // 3. 서비스 구성
            IDbMetadataService dbService = new DbMetadataService();
            var provider = configuration["AiSettings:Provider"] ?? "OpenAI";
            var modelName = configuration["AiSettings:ModelName"] ?? "gpt-4o";
            var apiKey = configuration["AiSettings:ApiKey"] ?? string.Empty;
            var endpoint = configuration["AiSettings:Endpoint"] ?? string.Empty;
            var tempStr = configuration["AiSettings:Temperature"] ?? "0.2";
            float.TryParse(tempStr, out float temp);

            IAiService aiService = new AiService(provider, modelName, apiKey, endpoint, temp);

            // MaxDependencyDepth 추가 로드
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

                SpAnalyzer.Core.Models.SpDefinition? spDef = null;
                string specificationMarkdown = string.Empty;

                bool processSuccess = false;

                // 6. DB 조회 및 AI 통신 진행 상황 표시
                await AnsiConsole.Status()
                    .StartAsync($"[yellow]{selectedOption}[/] 분석 프로세스 가동 중...", async ctx =>
                    {
                        try
                        {
                            ctx.Status($"[yellow]{selectedOption}[/] - DB 메타데이터 및 의존성 분석 중 (최대 깊이: {maxDepth}단계)...");
                            spDef = await dbService.GetSpDetailsAsync(connectionString, schema, name, maxDepth);

                            ctx.Status($"[yellow]{selectedOption}[/] - AI 리버스 엔지니어링 수행 중 ({provider})...");
                            
                            // 지침 문서 로드 (누락 시 Fallback 로직 적용)
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
                            else
                            {
                                AnsiConsole.MarkupLine("[red]SP 메타데이터가 올바르게 로드되지 않았습니다.[/]");
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
        }
    }
}
