using System;
using System.Threading.Tasks;
using Xunit;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class DbMetadataServiceDetailsTests
    {
        [Fact]
        public async Task GetSpDetailsAsync_WithInvalidConn_ShouldThrowException()
        {
            // Arrange
            var invalidConnString = "Server=invalid_server;Database=invalid_db;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=1;";
            IDbMetadataService service = new DbMetadataService();

            // Act & Assert
            // maxDepth=3 인자를 전달하여 호출 시그니처 변경에 따른 오류 유발 및 1차 예외 통과 확인
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetSpDetailsAsync(invalidConnString, "dbo", "USP_NonExistent", 3));
        }
    }
}
