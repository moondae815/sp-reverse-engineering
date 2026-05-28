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
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => service.GetSpDetailsAsync(invalidConnString, "dbo", "USP_NonExistent"));
        }
    }
}
