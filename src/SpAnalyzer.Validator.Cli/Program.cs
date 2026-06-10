using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using SpAnalyzer.Core.Services;
using SpAnalyzer.Core.Services.Clients;
using SpAnalyzer.Validator.Core.Models;
using SpAnalyzer.Validator.Core.Services;

namespace SpAnalyzer.Validator.Cli
{
    public class ValidatorCliArgs
    {
        public string? SpecDirectory { get; set; }
        public string? SourceCodeDirectory { get; set; }
        public string? TargetLanguage { get; set; }
        public bool IsBatchMode { get; set; }
    }

    public class Program
    {
        private static CancellationTokenSource? _currentCts;

        public static ValidatorCliArgs ParseCommandLineArgs(string[] args)
        {
            var cliArgs = new ValidatorCliArgs();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if ((arg.Equals("--spec", StringComparison.OrdinalIgnoreCase) || arg.Equals("--spec-dir", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    cliArgs.SpecDirectory = args[++i];
                }
                else if ((arg.Equals("--code", StringComparison.OrdinalIgnoreCase) || arg.Equals("--code-dir", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                {
                    cliArgs.SourceCodeDirectory = args[++i];
                }
                else if (arg.Equals("--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    cliArgs.TargetLanguage = args[++i];
                }
                else if (arg.Equals("--batch", StringComparison.OrdinalIgnoreCase))
                {
                    cliArgs.IsBatchMode = true;
                }
            }

            return cliArgs;
        }

        static async Task Main(string[] args)
        {
            // Ctrl+C 처리
            Console.CancelKeyPress += (sender, e) =>
            {
                if (_currentCts != null && !_currentCts.IsCancellationRequested)
                {
                    AnsiConsole.MarkupLine("\n[red]사용자 요청에 의해 검증 작업이 취소되었습니다. 정리 중...[/]");
                    _currentCts.Cancel();
                    e.Cancel = true;
                }
            };

            using var globalCts = new CancellationTokenSource();
            _currentCts = globalCts;

            AnsiConsole.Write(
                new FigletText("SP VALIDATOR")
                    .Color(Color.Blue));

            AnsiConsole.MarkupLine("[bold blue]=== SP Analyzer 코드 일치성 검증 도구 ===[/]");

            // 1. CLI 아규먼트 분석
            var cliArgs = ParseCommandLineArgs(args);

            // 2. 설정 로드
            var configuration = LoadConfiguration();

            // 3. 검증 환경 설정
            var validatorConfig = GetValidatorConfig(configuration, cliArgs);
            var ui = new ConsoleUserInteraction();

            // TUI 대화형 모드인 경우 유효하지 않은 디렉토리에 대해 사용자 재요청
            if (!cliArgs.IsBatchMode)
            {
                if (!Directory.Exists(validatorConfig.SpecDirectory))
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠️ 설정된 설계서 디렉토리가 존재하지 않습니다: {Markup.Escape(validatorConfig.SpecDirectory)}[/]");
                    var slnRoot = FindSolutionRoot();
                    var defaultSpecDir = slnRoot != null 
                        ? Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(slnRoot, "output")) 
                        : "./output";
                    var choices = GetDirectoryChoices(slnRoot);
                    validatorConfig.SpecDirectory = ui.PromptDirectoryPath("설계서(*_Spec.md)가 위치한 올바른 디렉토리 경로를 입력해 주세요:", defaultSpecDir, choices);
                }

                if (!Directory.Exists(validatorConfig.SourceCodeDirectory))
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠️ 설정된 소스코드 디렉토리가 존재하지 않습니다: {Markup.Escape(validatorConfig.SourceCodeDirectory)}[/]");
                    var slnRoot = FindSolutionRoot();
                    var defaultSrcDir = slnRoot != null 
                        ? Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.Combine(slnRoot, "src")) 
                        : "./src";
                    var choices = GetDirectoryChoices(slnRoot);
                    validatorConfig.SourceCodeDirectory = ui.PromptDirectoryPath("구현된 소스코드가 위치한 올바른 디렉토리 경로를 입력해 주세요:", defaultSrcDir, choices);
                }
            }

            AnsiConsole.MarkupLine($"[grey]설계서 디렉토리: {Markup.Escape(validatorConfig.SpecDirectory)}[/]");
            AnsiConsole.MarkupLine($"[grey]소스코드 디렉토리: {Markup.Escape(validatorConfig.SourceCodeDirectory)}[/]");
            AnsiConsole.MarkupLine($"[grey]보고서 출력 경로: {Markup.Escape(validatorConfig.OutputDirectory)}[/]");
            AnsiConsole.MarkupLine($"[grey]설정 언어 모드: {Markup.Escape(validatorConfig.TargetLanguage)}[/]");

            // 4. AI 서비스 연동 설정
            var provider = configuration["AiSettings:Provider"] ?? "OpenAI";
            var modelName = configuration["AiSettings:ModelName"] ?? "gpt-4o";
            
            // 기존 SpAnalyzer.Cli appsettings.local.json이 있을 경우 API Key를 가져오기 위한 대체 탐색 적용
            var apiKey = LoadApiKeyWithFallback(configuration, provider);
            var endpoint = configuration[$"AiSettings:Providers:{provider}:Endpoint"] ?? string.Empty;

            if (string.IsNullOrEmpty(apiKey) && !provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[red]에러: {provider} AI 클라이언트를 구동하기 위한 API Key가 설정되어 있지 않습니다.[/]");
                AnsiConsole.MarkupLine("[yellow]src/SpAnalyzer.Validator.Cli/appsettings.local.json 에 ApiKey를 지정해 주세요.[/]");
                return;
            }

            IAiClient aiClient;
            try
            {
                aiClient = AiClientFactory.CreateClient(provider, modelName, apiKey, endpoint);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]AI 클라이언트 생성 실패: {Markup.Escape(ex.Message)}[/]");
                return;
            }

            // 5. 오케스트레이터 구동
            var orchestrator = new CodeVerificationOrchestrator(validatorConfig, aiClient, ui);

            try
            {
                await orchestrator.RunVerificationAsync(cliArgs.IsBatchMode, globalCts.Token);
                AnsiConsole.MarkupLine("\n[bold green]🎉 검증 작업이 모두 완료되었습니다![/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]검증 작업이 취소되었습니다.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }
        }

        private static string? FindSolutionRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "SP-Reverse-Engineering.slnx")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir)!;
            }

            // Fallback: Current Working Directory
            dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "SP-Reverse-Engineering.slnx")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir)!;
            }

            return null;
        }

        private static IConfiguration LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

            var slnRoot = FindSolutionRoot();
            if (slnRoot != null)
            {
                var candidates = new[]
                {
                    Path.Combine(slnRoot, "src", "SpAnalyzer.Cli", "appsettings.local.json"),
                    Path.Combine(slnRoot, "src", "SpAnalyzer.Cli", "bin", "Debug", "net10.0", "appsettings.local.json"),
                    Path.Combine(slnRoot, "appsettings.local.json")
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        builder.AddJsonFile(Path.GetFullPath(path), optional: true, reloadOnChange: true);
                    }
                }
            }

            return builder.AddEnvironmentVariables().Build();
        }

        private static ValidatorConfig GetValidatorConfig(IConfiguration configuration, ValidatorCliArgs cliArgs)
        {
            var config = new ValidatorConfig();

            // appsettings 로드
            config.SpecDirectory = configuration["ValidationSettings:SpecDirectory"] ?? "./output";
            config.SourceCodeDirectory = configuration["ValidationSettings:SourceCodeDirectory"] ?? "./src";
            config.TargetLanguage = configuration["ValidationSettings:TargetLanguage"] ?? "Auto";
            config.OutputDirectory = configuration["ValidationSettings:OutputDirectory"] ?? "./output/validation";

            int.TryParse(configuration["AiSettings:MaxL2Attempts"] ?? "2", out int maxL2Attempts);
            config.MaxL2Attempts = maxL2Attempts;

            // CLI 인자가 있으면 오버라이드
            if (!string.IsNullOrEmpty(cliArgs.SpecDirectory))
            {
                config.SpecDirectory = cliArgs.SpecDirectory;
            }
            if (!string.IsNullOrEmpty(cliArgs.SourceCodeDirectory))
            {
                config.SourceCodeDirectory = cliArgs.SourceCodeDirectory;
            }
            if (!string.IsNullOrEmpty(cliArgs.TargetLanguage))
            {
                config.TargetLanguage = cliArgs.TargetLanguage;
            }

            // 상대경로 절대경로 보정
            if (!Path.IsPathRooted(config.SpecDirectory))
                config.SpecDirectory = Path.Combine(Directory.GetCurrentDirectory(), config.SpecDirectory);
            if (!Path.IsPathRooted(config.SourceCodeDirectory))
                config.SourceCodeDirectory = Path.Combine(Directory.GetCurrentDirectory(), config.SourceCodeDirectory);
            if (!Path.IsPathRooted(config.OutputDirectory))
                config.OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), config.OutputDirectory);

            return config;
        }

        private static string LoadApiKeyWithFallback(IConfiguration configuration, string provider)
        {
            // 1. 직접 지정된 API Key
            var key = configuration[$"AiSettings:Providers:{provider}:ApiKey"];
            if (!string.IsNullOrEmpty(key)) return key;

            // 2. 솔루션 루트 기반 동적 탐색
            var slnRoot = FindSolutionRoot();
            if (slnRoot != null)
            {
                var candidates = new[]
                {
                    Path.Combine(slnRoot, "src", "SpAnalyzer.Cli", "appsettings.local.json"),
                    Path.Combine(slnRoot, "src", "SpAnalyzer.Cli", "bin", "Debug", "net10.0", "appsettings.local.json"),
                    Path.Combine(slnRoot, "appsettings.local.json")
                };

                foreach (var path in candidates)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            var tempConfig = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(path)).Build();
                            var tempKey = tempConfig[$"AiSettings:Providers:{provider}:ApiKey"];
                            if (!string.IsNullOrEmpty(tempKey)) return tempKey;
                        }
                    }
                    catch { }
                }
            }

            return string.Empty;
        }

        private static List<string> GetDirectoryChoices(string? slnRoot)
        {
            var choices = new List<string>();
            var currentDir = Directory.GetCurrentDirectory();

            if (slnRoot != null)
            {
                // 1. output 및 하위 폴더들 추가
                var outputDir = Path.Combine(slnRoot, "output");
                if (Directory.Exists(outputDir))
                {
                    choices.Add(Path.GetRelativePath(currentDir, outputDir));
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(outputDir))
                        {
                            choices.Add(Path.GetRelativePath(currentDir, sub));
                        }
                    }
                    catch {}
                }

                // 2. src 및 하위 프로젝트 폴더들 추가
                var srcDir = Path.Combine(slnRoot, "src");
                if (Directory.Exists(srcDir))
                {
                    choices.Add(Path.GetRelativePath(currentDir, srcDir));
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(srcDir))
                        {
                            choices.Add(Path.GetRelativePath(currentDir, sub));
                        }
                    }
                    catch {}
                }

                // 3. 솔루션 루트 자체 추가
                choices.Add(Path.GetRelativePath(currentDir, slnRoot));
            }

            choices.Add("./output");
            choices.Add("./src");

            // 중복 제거 및 디렉토리 유효성 검사
            var result = new List<string>();
            foreach (var choice in choices)
            {
                var fullPath = Path.IsPathRooted(choice) ? choice : Path.Combine(currentDir, choice);
                if (Directory.Exists(fullPath))
                {
                    result.Add(choice);
                }
            }

            return result.Distinct().ToList();
        }
    }
}
