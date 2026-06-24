using System;

namespace ReSet.Validator.Core.Models
{
    public class GapReport
    {
        public string OverallStatus { get; set; } = "MATCH"; // MATCH, MISMATCH, PARTIAL
        public string InputParametersGap { get; set; } = string.Empty;
        public string OutputResultSetsGap { get; set; } = string.Empty;
        public string BusinessLogicGap { get; set; } = string.Empty;
        public string ExceptionHandlingGap { get; set; } = string.Empty;
        public string Suggestions { get; set; } = string.Empty;

        public bool HasGaps => OverallStatus != "MATCH" || 
                              !string.IsNullOrEmpty(InputParametersGap) || 
                              !string.IsNullOrEmpty(OutputResultSetsGap) || 
                              !string.IsNullOrEmpty(BusinessLogicGap) || 
                              !string.IsNullOrEmpty(ExceptionHandlingGap);
    }
}
