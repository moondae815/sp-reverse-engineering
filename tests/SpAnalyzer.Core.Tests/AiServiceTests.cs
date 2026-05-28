using System;
using System.Threading.Tasks;
using Xunit;
using SpAnalyzer.Core.Models;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class AiServiceTests
    {
        [Fact]
        public async Task GenerateSpecificationAsync_WithEmptyApiKeyForOpenAi_ShouldThrowException()
        {
            // Arrange
            var spDef = new SpDefinition { Schema = "dbo", Name = "USP_Test", DdlText = "SELECT 1;" };
            var instructions = "규칙1: 상세하게 쓸 것.";
            
            // OpenAI Provider이지만 API Key가 비어있는 상태
            IAiService service = new AiService("OpenAI", "gpt-4o", "", "https://api.openai.com/v1", 0.2f);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.GenerateSpecificationAsync(spDef, instructions));
        }
    }
}
