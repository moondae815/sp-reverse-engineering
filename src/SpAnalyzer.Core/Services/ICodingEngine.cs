using System.Threading;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface ICodingEngine
    {
        string Name { get; }

        /// <summary>
        /// 외부 코딩 에이전트를 프로세스로 기동하여 마이그레이션 코드를 작성하도록 지시합니다.
        /// </summary>
        /// <param name="spDef">SP 정의 메타데이터</param>
        /// <param name="instructionsFilePath">마이그레이션 지시서 번들 경로 (*_MigrationInstructions.md)</param>
        /// <param name="targetProjectDir">코드가 구현될 대상 프로젝트 디렉터리</param>
        /// <param name="cancellationToken">작업 취소 토큰</param>
        /// <returns>코드 생성 성공 여부</returns>
        Task<bool> GenerateCodeAsync(
            SpDefinition? spDef,
            string instructionsFilePath,
            string targetProjectDir,
            CancellationToken cancellationToken);
    }
}
