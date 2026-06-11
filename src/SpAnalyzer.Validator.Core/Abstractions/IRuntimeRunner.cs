using System.Threading;
using System.Threading.Tasks;

namespace SpAnalyzer.Validator.Core.Abstractions
{
    public interface IRuntimeRunner
    {
        string SupportedLanguage { get; }
        
        /// <summary>
        /// 타겟 마이그레이션 코드를 런타임에 실행하여 데이터를 수집합니다.
        /// </summary>
        /// <param name="targetPath">소스코드 파일 경로 또는 컴파일된 DLL/클래스 경로</param>
        /// <param name="testInputsJson">테스트 케이스 입력 파라미터 JSON</param>
        /// <param name="connectionString">데이터베이스 연결 문자열</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>ExecutionOutputDto 포맷의 JSON 결과</returns>
        Task<string> ExecuteAsync(string targetPath, string testInputsJson, string connectionString, CancellationToken cancellationToken = default);
    }
}
