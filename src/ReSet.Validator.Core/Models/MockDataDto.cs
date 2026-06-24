using System.Collections.Generic;

namespace ReSet.Validator.Core.Models
{
    public class MockDataDto
    {
        public List<MockTableDto> Tables { get; set; } = new();
    }

    public class MockTableDto
    {
        public string TableName { get; set; } = string.Empty; // e.g., "dbo.Customers"
        public List<Dictionary<string, object>> Rows { get; set; } = new();
    }
}
