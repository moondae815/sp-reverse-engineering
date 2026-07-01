using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using ReSet.Core.Models;
using ReSet.Core.Services;
using Xunit;

namespace ReSet.Core.Tests
{
    public class SettlementPolicyServiceTests
    {
        [Fact]
        public async Task GenerateSettlementPolicyRulebookAsync_ShouldGatherMetadataAndCallAiService()
        {
            // Arrange
            var dbService = Substitute.For<IDbMetadataService>();
            var aiService = Substitute.For<IAiService>();
            var service = new SettlementPolicyService(dbService, aiService);

            var connectionString = "Server=localhost;Database=Northwind;Integrated Security=true;TrustServerCertificate=true;";
            var spList = new List<string> { "dbo.sp_Test" };
            var maxDepth = 3;

            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "sp_Test",
                DdlText = "SELECT * FROM dbo.TestCodeTable",
                Dependencies = new List<DependencyInfo>
                {
                    new DependencyInfo
                    {
                        Schema = "dbo",
                        Name = "TestCodeTable",
                        Type = "USER_TABLE",
                        Columns = new List<ColumnInfo>
                        {
                            new ColumnInfo { ColumnName = "Code", DataType = "varchar(10)", IsPrimaryKey = true },
                            new ColumnInfo { ColumnName = "CodeName", DataType = "nvarchar(50)" }
                        }
                    }
                }
            };

            dbService.GetSpDetailsAsync(connectionString, "dbo", "sp_Test", maxDepth, Arg.Any<CancellationToken>())
                .Returns(spDef);

            var previewData = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "Code", "01" }, { "CodeName", "정산대기" } }
            };

            dbService.GetTableDataPreviewAsync(connectionString, null, "dbo", "TestCodeTable", 100, Arg.Any<CancellationToken>())
                .Returns(previewData);

            aiService.GenerateSettlementPolicyRulebookAsync(Arg.Any<List<SpDefinition>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new AiResult { Content = "Generated Policy Document" }));

            // Act
            var result = await service.GenerateSettlementPolicyRulebookAsync(connectionString, spList, maxDepth, CancellationToken.None);

            // Assert
            Assert.Equal("Generated Policy Document", result);
            await dbService.Received(1).GetSpDetailsAsync(connectionString, "dbo", "sp_Test", maxDepth, Arg.Any<CancellationToken>());
            await dbService.Received(1).GetTableDataPreviewAsync(connectionString, null, "dbo", "TestCodeTable", 100, Arg.Any<CancellationToken>());
            await aiService.Received(1).GenerateSettlementPolicyRulebookAsync(
                Arg.Is<List<SpDefinition>>(list => list.Count == 1 && list[0].Name == "sp_Test"),
                Arg.Is<string>(json => json.Contains("TestCodeTable") && json.Contains("정산대기")),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GenerateSettlementPolicyRulebookAsync_WhenTableMissing_ShouldAppendWarningAndPrependToDocument()
        {
            // Arrange
            var dbService = Substitute.For<IDbMetadataService>();
            var aiService = Substitute.For<IAiService>();
            var service = new SettlementPolicyService(dbService, aiService);

            var connectionString = "Server=localhost;Database=Northwind;Integrated Security=true;TrustServerCertificate=true;";
            var spList = new List<string> { "dbo.sp_Test" };
            var maxDepth = 3;

            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "sp_Test",
                DdlText = "SELECT * FROM dbo.TestCodeTable",
                Dependencies = new List<DependencyInfo>
                {
                    new DependencyInfo
                    {
                        Schema = "dbo",
                        Name = "TestCodeTable",
                        Type = "USER_TABLE",
                        Columns = new List<ColumnInfo>()
                    }
                }
            };

            dbService.GetSpDetailsAsync(connectionString, "dbo", "sp_Test", maxDepth, Arg.Any<CancellationToken>())
                .Returns(spDef);

            dbService.GetTableDataPreviewAsync(connectionString, null, "dbo", "TestCodeTable", 100, Arg.Any<CancellationToken>())
                .Returns(Task.FromException<List<Dictionary<string, object>>>(new System.Exception("Table not found")));

            aiService.GenerateSettlementPolicyRulebookAsync(Arg.Any<List<SpDefinition>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new AiResult { Content = "Generated Policy Document" }));

            // Act
            var result = await service.GenerateSettlementPolicyRulebookAsync(connectionString, spList, maxDepth, CancellationToken.None);

            // Assert
            Assert.Contains("> [!WARNING]", result);
            Assert.Contains("TestCodeTable 테이블이 데이터베이스에 존재하지 않거나 액세스할 수 없습니다.", result);
            Assert.Contains("Generated Policy Document", result);
        }

        [Fact]
        public async Task GenerateSettlementPolicyRulebookAsync_WhenDataEmpty_ShouldAppendWarningAndPrependToDocument()
        {
            // Arrange
            var dbService = Substitute.For<IDbMetadataService>();
            var aiService = Substitute.For<IAiService>();
            var service = new SettlementPolicyService(dbService, aiService);

            var connectionString = "Server=localhost;Database=Northwind;Integrated Security=true;TrustServerCertificate=true;";
            var spList = new List<string> { "dbo.sp_Test" };
            var maxDepth = 3;

            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "sp_Test",
                DdlText = "SELECT * FROM dbo.TestCodeTable",
                Dependencies = new List<DependencyInfo>
                {
                    new DependencyInfo
                    {
                        Schema = "dbo",
                        Name = "TestCodeTable",
                        Type = "USER_TABLE",
                        Columns = new List<ColumnInfo>()
                    }
                }
            };

            dbService.GetSpDetailsAsync(connectionString, "dbo", "sp_Test", maxDepth, Arg.Any<CancellationToken>())
                .Returns(spDef);

            dbService.GetTableDataPreviewAsync(connectionString, null, "dbo", "TestCodeTable", 100, Arg.Any<CancellationToken>())
                .Returns(new List<Dictionary<string, object>>());

            aiService.GenerateSettlementPolicyRulebookAsync(Arg.Any<List<SpDefinition>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new AiResult { Content = "Generated Policy Document" }));

            // Act
            var result = await service.GenerateSettlementPolicyRulebookAsync(connectionString, spList, maxDepth, CancellationToken.None);

            // Assert
            Assert.Contains("> [!WARNING]", result);
            Assert.Contains("TestCodeTable 테이블의 실제 설정/공통코드 데이터가 비어있습니다.", result);
            Assert.Contains("Generated Policy Document", result);
        }
    }
}
