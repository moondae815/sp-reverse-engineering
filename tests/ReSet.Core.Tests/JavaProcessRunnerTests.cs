using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ReSet.Validator.Core.Services;
using ReSet.Validator.Core.Models;

namespace ReSet.Core.Tests
{
    public class JavaProcessRunnerTests
    {
        [Fact]
        public async Task ExecuteAsync_WithInvalidTargetPath_ShouldSoftFailAndReturnJson()
        {
            // Arrange
            var runner = new JavaProcessRunner();
            var invalidPath = Path.Combine(Path.GetTempPath(), "non_existent_file_xyz.jar");
            var testInputsJson = "{}";
            var connectionString = "Server=localhost;Database=master;";

            // Act
            var resultJson = await runner.ExecuteAsync(invalidPath, testInputsJson, connectionString, CancellationToken.None);

            // Assert
            Assert.False(string.IsNullOrEmpty(resultJson));

            using (var doc = JsonDocument.Parse(resultJson))
            {
                var root = doc.RootElement;
                Assert.True(root.TryGetProperty("ProcedureName", out var procName));
                Assert.Equal("non_existent_file_xyz", procName.GetString());

                Assert.True(root.TryGetProperty("ExecutionResults", out var resultsElem));
                Assert.Equal(JsonValueKind.Array, resultsElem.ValueKind);
                Assert.Equal(1, resultsElem.GetArrayLength());

                var firstResult = resultsElem[0];
                Assert.Equal("JAVA_EXECUTION_ERROR", firstResult.GetProperty("CaseId").GetString());
                Assert.Equal("FAIL", firstResult.GetProperty("Status").GetString());
                Assert.True(firstResult.TryGetProperty("ErrorCode", out _));
            }
        }
    }
}
