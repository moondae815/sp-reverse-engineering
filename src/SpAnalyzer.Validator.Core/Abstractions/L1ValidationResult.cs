using System;
using System.Collections.Generic;

namespace SpAnalyzer.Validator.Core.Abstractions
{
    public class L1ValidationResult
    {
        public bool Passed { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ClassOrMethodName { get; set; } = string.Empty;
        public Dictionary<string, string> ExtractedMetadata { get; set; } = new();
    }
}
