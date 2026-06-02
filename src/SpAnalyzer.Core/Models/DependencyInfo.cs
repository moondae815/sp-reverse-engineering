using System.Collections.Generic;

namespace SpAnalyzer.Core.Models
{
    public class DependencyInfo
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int DiscoveryDepth { get; set; }
        public List<ColumnInfo> Columns { get; set; } = new();
        public string? ReferencedDdlText { get; set; }
    }
}
