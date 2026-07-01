namespace ReSet.Core.Models
{
    public class ColumnInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsDescriptionMissing { get; set; }
        public bool IsIdentity { get; set; }
        public string? DefaultValue { get; set; }
    }
}

