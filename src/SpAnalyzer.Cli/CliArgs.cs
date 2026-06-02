using System.Collections.Generic;

namespace SpAnalyzer.Cli
{
    public class CliArgs
    {
        public string? ConnectionString { get; set; }
        public bool AnalyzeAll { get; set; }
        public List<string> TargetProcedures { get; set; } = new();

        public bool IsBatchMode => AnalyzeAll || TargetProcedures.Count > 0;
    }
}
