using System;
using System.Reflection;
using System.Text.Json;
using Xunit;
using ReSet.Validator.Core.Services;

namespace ReSet.Core.Tests
{
    public class SandboxSeedingServiceTests
    {
        [Theory]
        [InlineData("dbo.MyTable", "[dbo].[MyTable]")]
        [InlineData("[dbo].[MyTable]", "[dbo].[MyTable]")]
        [InlineData("MySchema.MyTable", "[MySchema].[MyTable]")]
        [InlineData("[MySchema].[MyTable]", "[MySchema].[MyTable]")]
        public void SanitizeTableName_ShouldEscapeTableNamesCorrectly(string raw, string expected)
        {
            // Arrange
            var service = new SandboxSeedingService();
            var methodInfo = typeof(SandboxSeedingService).GetMethod("SanitizeTableName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(methodInfo);

            // Act
            var result = (string?)methodInfo.Invoke(service, new object[] { raw });

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertJsonValue_ShouldParseJsonElementsCorrectly()
        {
            // Arrange
            var service = new SandboxSeedingService();
            var methodInfo = typeof(SandboxSeedingService).GetMethod("ConvertJsonValue", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(methodInfo);

            using var doc = JsonDocument.Parse("{\"str\": \"2026-07-02T20:00:00\", \"num\": 12345, \"dbl\": 12.34, \"bool\": true, \"nullVal\": null}");
            var root = doc.RootElement;

            // Act & Assert
            // 1. Date string -> DateTime
            var dateVal = methodInfo.Invoke(service, new object[] { root.GetProperty("str") });
            Assert.IsType<DateTime>(dateVal);
            Assert.Equal(new DateTime(2026, 7, 2, 20, 0, 0), dateVal);

            // 2. Integer number -> long
            var longVal = methodInfo.Invoke(service, new object[] { root.GetProperty("num") });
            Assert.IsType<long>(longVal);
            Assert.Equal(12345L, longVal);

            // 3. Double number -> double
            var dblVal = methodInfo.Invoke(service, new object[] { root.GetProperty("dbl") });
            Assert.IsType<double>(dblVal);
            Assert.Equal(12.34, dblVal);

            // 4. Boolean -> bool
            var boolVal = methodInfo.Invoke(service, new object[] { root.GetProperty("bool") });
            Assert.IsType<bool>(boolVal);
            Assert.True((bool?)boolVal);

            // 5. Null -> null
            var nullVal = methodInfo.Invoke(service, new object[] { root.GetProperty("nullVal") });
            Assert.Null(nullVal);
        }
    }
}
