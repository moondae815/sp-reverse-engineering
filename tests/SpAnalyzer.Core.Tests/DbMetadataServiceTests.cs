using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class DbMetadataServiceTests
    {
        [Fact]
        public async Task GetStoredProcedureNamesAsync_WithInvalidConnectionString_ShouldThrowException()
        {
            // Arrange
            var invalidConnString = "Server=invalid_server;Database=invalid_db;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=1;";
            IDbMetadataService service = new DbMetadataService();

            // Act & Assert
            await Assert.ThrowsAnyAsync<System.Exception>(() => service.GetStoredProcedureNamesAsync(invalidConnString));
        }
    }
}
