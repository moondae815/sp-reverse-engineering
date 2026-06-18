using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class ExternalCliCodingEngine : ICodingEngine
    {
        private readonly string _command;
        private readonly string _argumentsTemplate;

        public string Name { get; }

        public ExternalCliCodingEngine(string name, string command, string argumentsTemplate)
        {
            Name = name;
            _command = command;
            _argumentsTemplate = argumentsTemplate;
        }

        public async Task<bool> GenerateCodeAsync(
            SpDefinition? spDef,
            string instructionsFilePath,
            string targetProjectDir,
            CancellationToken cancellationToken)
        {
            // {instructions} 자리표시자를 지시서 절대 경로로 치환
            var absoluteInstructionsPath = Path.GetFullPath(instructionsFilePath);
            var arguments = _argumentsTemplate.Replace("{instructions}", $"\"{absoluteInstructionsPath}\"");

            var startInfo = new ProcessStartInfo
            {
                FileName = _command,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(targetProjectDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(targetProjectDir),
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    // 취소 토큰 감지 시 프로세스 강제 종료 등록
                    using (cancellationToken.Register(() =>
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true);
                            }
                        }
                        catch
                        {
                            // 무시
                        }
                    }))
                    {
                        // 프로세스가 종료될 때까지 비동기적으로 대기
                        await process.WaitForExitAsync(cancellationToken);
                        return process.ExitCode == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"외부 코딩 엔진({Name}) 기동 중 오류가 발생했습니다. 명령어가 설치되어 있는지 확인해 주십시오. (오류: {ex.Message})", ex);
            }
        }
    }
}
