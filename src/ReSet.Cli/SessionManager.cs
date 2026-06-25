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
            public string LastUsedServer { get; set; } = string.Empty;
            public string LastUsedDatabase { get; set; } = string.Empty;
        }

        public static SessionData LoadSession(string filePath = DefaultSessionFileName)
        {
            if (!File.Exists(filePath))
            {
                return new SessionData();
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<SessionData>(json);
                return data ?? new SessionData();
            }
            catch
            {
                return new SessionData();
            }
        }

        public static void SaveSession(string userId, string server, string database, string filePath = DefaultSessionFileName)
        {
            try
            {
                var data = new SessionData
                {
                    LastUsedUserId = userId,
                    LastUsedServer = server,
                    LastUsedDatabase = database
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // 세션 파일 쓰기 실패 처리 무시
            }
        }

        public static string LoadLastUsedUserId(string filePath = DefaultSessionFileName)
        {
            return LoadSession(filePath).LastUsedUserId;
        }

        public static void SaveLastUsedUserId(string userId, string filePath = DefaultSessionFileName)
        {
            var data = LoadSession(filePath);
            SaveSession(userId, data.LastUsedServer, data.LastUsedDatabase, filePath);
        }
    }
}
