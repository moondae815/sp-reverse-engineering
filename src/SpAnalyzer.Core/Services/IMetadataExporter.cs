using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IMetadataExporter
    {
        /// <summary>
        /// 수집된 원천 DB 정보 및 프롬프트 컨텍스트를 디스크에 저장합니다.
        /// </summary>
        Task ExportRawMetadataAsync(
            SpDefinition spDef, 
            string rawPromptContext, 
            string baseOutputDir, 
            bool saveJson, 
            bool saveContext, 
            bool saveFiles);

        /// <summary>
        /// 코딩 에이전트에 최적화된 마이그레이션 가이드라인과 설계 텍스트 번들을 저장합니다.
        /// </summary>
        Task ExportMigrationInstructionsAsync(
            SpDefinition spDef,
            string specMarkdown,
            string migrationPlan,
            string baseOutputDir);
    }
}
