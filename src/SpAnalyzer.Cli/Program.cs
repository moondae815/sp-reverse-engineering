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
