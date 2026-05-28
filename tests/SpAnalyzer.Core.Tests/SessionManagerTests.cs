using System.IO;
using Xunit;
using SpAnalyzer.Cli;

namespace SpAnalyzer.Core.Tests
{
    public class SessionManagerTests
    {
        [Fact]
        public void SessionManager_ShouldSaveAndLoadLastUserId()
        {
            // Arrange
            var testFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".test.session.json");
            if (File.Exists(testFilePath)) File.Delete(testFilePath);

            // Act
            SessionManager.SaveLastUsedUserId("test_admin_user", testFilePath);
            var loadedUserId = SessionManager.LoadLastUsedUserId(testFilePath);

            // Assert
            Assert.Equal("test_admin_user", loadedUserId);

            // Clean up
            if (File.Exists(testFilePath)) File.Delete(testFilePath);
        }
    }
}
