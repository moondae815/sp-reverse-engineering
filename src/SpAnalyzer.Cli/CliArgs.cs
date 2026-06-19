using System.Collections.Generic;

namespace SpAnalyzer.Cli
{
    public class CliArgs
    {
        public string? ConnectionString { get; set; }
        public bool AnalyzeAll { get; set; }
        public List<string> TargetProcedures { get; set; } = new();
        public bool EnableCodegen { get; set; }
        public string? Engine { get; set; }
        public string? JobName { get; set; }

        public bool IsBatchMode => AnalyzeAll || TargetProcedures.Count > 0;
    }
}
