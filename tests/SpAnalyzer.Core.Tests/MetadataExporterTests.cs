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

        [Fact]
        public async Task ExportRawMetadataAsync_ShouldIncludeDescriptionsInMarkdown_WhenSaveFilesIsTrue()
        {
            // Arrange
            var testOutputDir = Path.Combine(Directory.GetCurrentDirectory(), "test_output_exporter_desc");
            if (Directory.Exists(testOutputDir))
            {
                Directory.Delete(testOutputDir, true);
            }

            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "USP_TestExporterDesc",
                DdlText = "SELECT 1;"
            };
            
            var depInfo = new DependencyInfo
            {
                Schema = "dbo",
                Name = "TBL_TestDesc",
                Type = "USER_TABLE",
                DiscoveryDepth = 1,
                Description = "테스트용 테이블 설명"
            };
            depInfo.Columns.Add(new ColumnInfo
            {
                ColumnName = "COL_Test",
                DataType = "INT",
                IsNullable = false,
                IsPrimaryKey = true,
                Description = "테스트용 컬럼 설명"
            });
            spDef.Dependencies.Add(depInfo);

            var rawContext = "Test Context Header";
            IMetadataExporter exporter = new MetadataExporter();

            // Act
            await exporter.ExportRawMetadataAsync(spDef, rawContext, testOutputDir, false, false, true);

            // Assert
            var expectedMdPath = Path.Combine(testOutputDir, "dbo.USP_TestExporterDesc_Raw", "tables", "dbo.TBL_TestDesc.md");
            Assert.True(File.Exists(expectedMdPath));

            var mdContent = await File.ReadAllTextAsync(expectedMdPath);
            Assert.Contains("테스트용 테이블 설명", mdContent);
            Assert.Contains("테스트용 컬럼 설명", mdContent);

            // Clean up
            if (Directory.Exists(testOutputDir))
            {
                Directory.Delete(testOutputDir, true);
            }
        }
    }
}
