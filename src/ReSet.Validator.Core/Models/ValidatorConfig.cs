using System;

namespace ReSet.Validator.Core.Models
{
    public class ValidatorConfig
    {
        public string SpecDirectory { get; set; } = "./output";
        public string SourceCodeDirectory { get; set; } = "./src";
        public string TargetLanguage { get; set; } = "Auto"; // Auto, C#, Java
        
        // AI Settings (Optional overrides; falls back to appsettings)
        public string? AiProvider { get; set; }
        public string? ModelName { get; set; }
        public string? ApiKey { get; set; }
        public string? Endpoint { get; set; }
        public int MaxL2Attempts { get; set; } = 2;

        public string OutputDirectory { get; set; } = "./output/validation";
    }
}
