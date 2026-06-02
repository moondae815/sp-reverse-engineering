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
    }
}
