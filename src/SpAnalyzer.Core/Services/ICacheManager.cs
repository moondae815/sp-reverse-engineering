using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface ICacheManager
    {
        string ComputeCompositeHash(SpDefinition spDef);
        bool IsCacheValid(string procedureName, string compositeHash, string outputDirectory);
        void UpdateCache(string procedureName, SpDefinition spDef, string compositeHash, string outputDirectory);
    }
}
