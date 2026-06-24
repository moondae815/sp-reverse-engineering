using ReSet.Core.Models;

namespace ReSet.Core.Services
{
    public interface ICacheManager
    {
        string ComputeCompositeHash(SpDefinition spDef);
        bool IsCacheValid(string procedureName, string compositeHash, string outputDirectory);
        void UpdateCache(string procedureName, SpDefinition spDef, string compositeHash, string outputDirectory);
    }
}
