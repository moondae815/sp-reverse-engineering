namespace ReSet.Core.Models
{
    public class AiResult
    {
        public string Content { get; set; } = string.Empty;
        public string? ThinkingText { get; set; }
        public string? SystemPrompt { get; set; }
        public string? UserPrompt { get; set; }
    }
}
