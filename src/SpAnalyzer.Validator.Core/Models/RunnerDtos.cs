using System.Collections.Generic;

namespace SpAnalyzer.Validator.Core.Models
{
    public class TestInputsDto
    {
        public string ProcedureName { get; set; } = string.Empty;
        public List<TestCaseDto> TestCases { get; set; } = new();
    }

    public class TestCaseDto
    {
        public string CaseId { get; set; } = string.Empty;
        public Dictionary<string, object?>? Parameters { get; set; }
    }

    public class ExecutionOutputDto
    {
        public string ProcedureName { get; set; } = string.Empty;
        public List<TestCaseResultDto> ExecutionResults { get; set; } = new();
    }

    public class TestCaseResultDto
    {
        public string CaseId { get; set; } = string.Empty;
        public string Status { get; set; } = "SUCCESS"; // SUCCESS, FAIL
        public string? ErrorCode { get; set; }
        public List<List<Dictionary<string, object>>> ResultSets { get; set; } = new();
    }
}
