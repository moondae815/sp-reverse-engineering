using Xunit;
using SpAnalyzer.Core.Models;

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
                    new DependencyInfo { Schema = "dbo", Name = "Users", Type = "USER_TABLE" }
                }
            };

            // Assert
            Assert.Equal("dbo", spDef.Schema);
            Assert.Equal("USP_GetUsers", spDef.Name);
            Assert.Equal("CREATE PROCEDURE USP_GetUsers AS SELECT * FROM Users;", spDef.DdlText);
            Assert.Single(spDef.Dependencies);
            Assert.Equal("Users", spDef.Dependencies[0].Name);
            Assert.Equal("USER_TABLE", spDef.Dependencies[0].Type);
        }
    }
}
