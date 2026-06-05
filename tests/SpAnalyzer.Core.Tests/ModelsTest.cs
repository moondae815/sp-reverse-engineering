using Xunit;
using SpAnalyzer.Core.Models;
using System.Collections.Generic;

namespace SpAnalyzer.Core.Tests
{
    public class ModelsTest
    {
        [Fact]
        public void SpDefinition_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "USP_GetUsers",
                DdlText = "CREATE PROCEDURE USP_GetUsers AS SELECT * FROM Users;",
                Dependencies = new List<DependencyInfo>
                {
                    new DependencyInfo 
                    { 
                        Schema = "dbo", 
                        Name = "Users", 
                        Type = "USER_TABLE",
                        DiscoveryDepth = 1,
                        ReferencedDdlText = null,
                        Columns = new List<ColumnInfo>
                        {
                            new ColumnInfo { ColumnName = "UserId", DataType = "INT", IsNullable = false, IsPrimaryKey = true, IsForeignKey = false }
                        }
                    }
                }
            };

            // Assert
            Assert.Equal("dbo", spDef.Schema);
            Assert.Equal("USP_GetUsers", spDef.Name);
            Assert.Single(spDef.Dependencies);
            var dep = spDef.Dependencies[0];
            Assert.Equal("Users", dep.Name);
            Assert.Equal("USER_TABLE", dep.Type);
            Assert.Equal(1, dep.DiscoveryDepth);
            Assert.Null(dep.ReferencedDdlText);
            Assert.Single(dep.Columns);
            Assert.Equal("UserId", dep.Columns[0].ColumnName);
            Assert.Equal("INT", dep.Columns[0].DataType);
            Assert.False(dep.Columns[0].IsNullable);
            Assert.True(dep.Columns[0].IsPrimaryKey);
            Assert.False(dep.Columns[0].IsForeignKey);
            Assert.NotNull(spDef.Warnings);
            Assert.Empty(spDef.Warnings);
        }
    }
}
