using System.Collections.Generic;

namespace ReSet.Core.Models
{
    public class SpDefinition
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string DdlText { get; set; } = string.Empty;
        public List<DependencyInfo> Dependencies { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
