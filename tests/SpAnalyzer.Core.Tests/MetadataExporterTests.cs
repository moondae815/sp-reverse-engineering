using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using SpAnalyzer.Core.Models;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class MetadataExporterTests
    {
        [Fact]
        public async Task ExportRawMetadataAsync_ShouldCreateJsonFile_WhenSaveJsonIsTrue()
        {
            // Arrange
            var testOutputDir = Path.Combine(Directory.GetCurrentDirectory(), "test_output_exporter");
            if (Directory.Exists(testOutputDir))
            {
                Directory.Delete(testOutputDir, true);
            }

            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "USP_TestExporter",
                DdlText = "SELECT 1;"
            };
            var rawContext = "Test Context Header\nSELECT 1;";
            
            // IMetadataExporter 선언
            IMetadataExporter exporter = new MetadataExporter();

            // Act
            await exporter.ExportRawMetadataAsync(spDef, rawContext, testOutputDir, true, false, false);

            // Assert
            var expectedJsonPath = Path.Combine(testOutputDir, "dbo.USP_TestExporter_Raw.json");
            Assert.True(File.Exists(expectedJsonPath));

            // Clean up
            if (Directory.Exists(testOutputDir))
            {
                Directory.Delete(testOutputDir, true);
            }
        }
    }
}
