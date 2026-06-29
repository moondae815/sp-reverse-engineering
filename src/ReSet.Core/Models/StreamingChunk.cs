namespace ReSet.Core.Models
{
    public enum ChunkType
    {
        Thinking,
        Text
    }

    public class StreamingChunk
    {
        public ChunkType Type { get; set; }
        public string Content { get; set; } = string.Empty;

        public StreamingChunk(ChunkType type, string content)
        {
            Type = type;
            Content = content;
        }
    }
}
