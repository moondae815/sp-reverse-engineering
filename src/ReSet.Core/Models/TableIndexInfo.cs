using System.Collections.Generic;

namespace ReSet.Core.Models
{
    public class TableIndexInfo
    {
        public string IndexName { get; set; } = string.Empty;
        public string IndexType { get; set; } = string.Empty; // CLUSTERED, NONCLUSTERED
        public bool IsUnique { get; set; }
        public bool IsPrimaryKey { get; set; }
        public List<string> Columns { get; set; } = new();
    }
}
