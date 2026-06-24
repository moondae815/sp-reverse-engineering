using System.IO;
using System.Text.Json;

namespace ReSet.Cli
{
    public static class SessionManager
    {
        private const string DefaultSessionFileName = ".session.json";

        public class SessionData
        {
            public string LastUsedUserId { get; set; } = string.Empty;
        }

        public static string LoadLastUsedUserId(string filePath = DefaultSessionFileName)
        {
            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<SessionData>(json);
                return data?.LastUsedUserId ?? string.Empty;
            }
            catch
            {
                return string.Empty; // 세션 로드 실패 시 조용히 넘어감
            }
        }

        public static void SaveLastUsedUserId(string userId, string filePath = DefaultSessionFileName)
        {
            try
            {
                var data = new SessionData { LastUsedUserId = userId };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // 세션 파일 쓰기 실패 처리 무시
            }
        }
    }
}
