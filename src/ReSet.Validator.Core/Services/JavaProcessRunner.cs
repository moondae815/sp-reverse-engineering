using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReSet.Validator.Core.Abstractions;

namespace ReSet.Validator.Core.Services
{
    public class JavaProcessRunner : IRuntimeRunner
    {
        public string SupportedLanguage => "Java";

        public async Task<string> ExecuteAsync(string targetPath, string testInputsJson, string connectionString, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("타겟 경로가 지정되지 않았습니다.", nameof(targetPath));
            }

            // Java 실행 경로 탐색 (기본 java 또는 환경변수 JAVA_HOME 활용)
            string javaExe = "java";
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var jdkJava = Path.Combine(javaHome, "bin", "java");
                if (File.Exists(jdkJava)) javaExe = jdkJava;
                else if (File.Exists(jdkJava + ".exe")) javaExe = jdkJava + ".exe";
            }

            // Java 실행 인자 구성
            // 1) targetPath가 JAR 파일인 경우: -jar targetPath
            // 2) targetPath가 class 파일 또는 소스파일인 경우: 클래스 패스 지정하여 클래스명 실행
            string arguments;
            if (Path.GetExtension(targetPath).Equals(".jar", StringComparison.OrdinalIgnoreCase))
            {
                arguments = $"-jar \"{targetPath}\"";
            }
            else
            {
                // 소스파일(.java)인 경우 컴파일된 디렉토리를 클래스패스로 추정
                var className = Path.GetFileNameWithoutExtension(targetPath);
                var classDir = Path.GetDirectoryName(targetPath) ?? ".";
                
                // bin/ 또는 target/classes/ 등 컴파일 산출물 폴더가 있을 경우 클래스패스 확장
                var classpath = classDir;
                var parent = Path.GetDirectoryName(classDir);
                if (!string.IsNullOrEmpty(parent))
                {
                    var targetClasses = Path.Combine(parent, "target", "classes");
                    if (Directory.Exists(targetClasses)) classpath = targetClasses;
                    else
                    {
                        var binDir = Path.Combine(parent, "bin");
                        if (Directory.Exists(binDir)) classpath = binDir;
                    }
                }

                arguments = $"-cp \"{classpath}\" {className}";
            }

            // 표준 입력을 통해 전달할 통합 데이터 (JSON 포맷)
            var inputPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                ConnectionString = connectionString,
                TestInputs = System.Text.Json.JsonSerializer.Deserialize<object>(testInputsJson)
            });

            var startInfo = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException("Java 프로세스를 시작할 수 없습니다.");
                    }

                    // 입력 스트림 쓰기
                    using (var writer = process.StandardInput)
                    {
                        await writer.WriteAsync(inputPayload);
                        await writer.FlushAsync();
                    }

                    // 출력 및 에러 비동기 읽기
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    // 타임아웃 30초 대기
                    var completedTask = await Task.WhenAny(
                        Task.Delay(30000, cancellationToken),
                        Task.WhenAll(outputTask, errorTask)
                    );

                    if (completedTask == outputTask || completedTask != Task.Delay(30000, cancellationToken))
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }

                        var output = await outputTask;
                        var error = await errorTask;

                        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
                        {
                            throw new Exception($"Java 실행 오류 (ExitCode: {process.ExitCode}): {error}");
                        }

                        return output;
                    }
                    else
                    {
                        try { process.Kill(); } catch { }
                        throw new TimeoutException("Java 테스트 프로세스 실행 시간 초과(30초).");
                    }
                }
            }
            catch (Exception ex)
            {
                // Soft Fail: 실패 상세를 JSON 포맷으로 패키징하여 리턴
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    ProcedureName = Path.GetFileNameWithoutExtension(targetPath),
                    ExecutionResults = new[]
                    {
                        new {
                            CaseId = "JAVA_EXECUTION_ERROR",
                            Status = "FAIL",
                            ErrorCode = ex.Message,
                            ResultSets = new object[0]
                        }
                    }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }
    }
}
