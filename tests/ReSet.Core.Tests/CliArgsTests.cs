using System;
using Xunit;
using ReSet.Cli;

namespace ReSet.Core.Tests
{
    public class CliArgsTests
    {
        [Fact]
        public void ParseCommandLineArgs_ShouldBindCorrectly()
        {
            // Arrange
            string[] args = new[] { "--conn", "Server=my_server;", "--all", "--sp", "dbo.USP_1,dbo.USP_2" };

            // Act
            CliArgs result = Program.ParseCommandLineArgs(args);

            // Assert
            Assert.Equal("Server=my_server;", result.ConnectionString);
            Assert.True(result.AnalyzeAll);
            Assert.Equal(2, result.TargetProcedures.Count);
            Assert.Equal("dbo.USP_1", result.TargetProcedures[0]);
            Assert.Equal("dbo.USP_2", result.TargetProcedures[1]);
            Assert.True(result.IsBatchMode);
        }
    }
}
