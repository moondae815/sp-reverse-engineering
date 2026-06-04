using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using SpAnalyzer.Core.Models;
using SpAnalyzer.Core.Services;
using Xunit;

namespace SpAnalyzer.Core.Tests
{
    public class VerificationPipelineOrchestratorTests
    {
        private readonly IDbMetadataService _dbService;
        private readonly IAiService _aiService;
        private readonly MechanicalValidator _validator;
        private readonly IVerificationUserInteraction _userInteraction;
        private readonly VerificationPipelineOrchestrator _orchestrator;

        public VerificationPipelineOrchestratorTests()
        {
            _dbService = Substitute.For<IDbMetadataService>();
            _aiService = Substitute.For<IAiService>();
            _validator = new MechanicalValidator(); // 검증 규칙은 실제 동작 검증에 필수
            _userInteraction = Substitute.For<IVerificationUserInteraction>();
            _orchestrator = new VerificationPipelineOrchestrator(_dbService, _aiService, _validator, _userInteraction);
        }

        [Fact]
        public async Task RunPipelineAsync_SuccessOnFirstTry_ReturnsSpecification()
        {
            // Arrange
            var spDef = new SpDefinition { Schema = "dbo", Name = "USP_Test", DdlText = "CREATE PROCEDURE USP_Test AS SELECT 1" };
            _dbService.GetSpDetailsAsync(Arg.Any<string>(), "dbo", "USP_Test", Arg.Any<int>())
                .Returns(Task.FromResult(spDef));

            // 올바른 마크다운 명세서 형식 (MechanicalValidator 검증 필수 헤더 포함)
            var specMarkdown = "## 개요\n## 파라미터 목록\n## CRUD 분석\n## 로직 흐름 요약\n## 비즈니스 흐름 시각화\n```mermaid\ngraph TD\nA-->B\n```";
            _aiService.GenerateSpecificationAsync(spDef, Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult(specMarkdown));

            var reviewResult = new ReviewResult { HasDefects = false };
            _aiService.ReviewSpecificationAsync(spDef, specMarkdown)
                .Returns(Task.FromResult(reviewResult));

            // Act
            var (resultSpec, resultDef) = await _orchestrator.RunPipelineAsync(
                "connection_string", "dbo", "USP_Test", 3, "OpenAI", "instructions", isBatchMode: true);

            // Assert
            Assert.NotNull(resultSpec);
            Assert.Equal(specMarkdown, resultSpec);
            Assert.Equal(spDef, resultDef);
            _userInteraction.Received(1).NotifyValidationSuccess("dbo.USP_Test");
        }

        [Fact]
        public async Task RunPipelineAsync_L1ValidationError_AttemptsSelfCorrection()
        {
            // Arrange
            var spDef = new SpDefinition { Schema = "dbo", Name = "USP_Test", DdlText = "CREATE PROCEDURE USP_Test AS SELECT 1" };
            _dbService.GetSpDetailsAsync(Arg.Any<string>(), "dbo", "USP_Test", Arg.Any<int>())
                .Returns(Task.FromResult(spDef));

            // 1차 생성: 잘못된 형식 (헤더 누락) -> L1 실패 유발
            var badSpec = "잘못된 문서";
            // 2차 생성: 올바른 형식 -> L1 성공
            var goodSpec = "## 개요\n## 파라미터 목록\n## CRUD 분석\n## 로직 흐름 요약\n## 비즈니스 흐름 시각화\n```mermaid\ngraph TD\nA-->B\n```";

            _aiService.GenerateSpecificationAsync(spDef, Arg.Any<string>(), Arg.Any<string>())
                .Returns(
                    _ => Task.FromResult(badSpec),   // 1차 호출
                    _ => Task.FromResult(goodSpec)  // 2차 호출
                );

            var reviewResult = new ReviewResult { HasDefects = false };
            _aiService.ReviewSpecificationAsync(spDef, goodSpec)
                .Returns(Task.FromResult(reviewResult));

            // Act
            var (resultSpec, resultDef) = await _orchestrator.RunPipelineAsync(
                "connection_string", "dbo", "USP_Test", 3, "OpenAI", "instructions", isBatchMode: true);

            // Assert
            Assert.NotNull(resultSpec);
            Assert.Equal(goodSpec, resultSpec);
            _userInteraction.Received(1).NotifyL1Errors("dbo.USP_Test", 1, Arg.Any<int>(), Arg.Any<List<string>>());
            _userInteraction.Received(1).NotifyValidationSuccess("dbo.USP_Test");
        }

        [Fact]
        public async Task RunPipelineAsync_L3HumanFeedbackLoop_ApproveWorkflow()
        {
            // Arrange
            var spDef = new SpDefinition { Schema = "dbo", Name = "USP_Test", DdlText = "CREATE PROCEDURE USP_Test AS SELECT 1" };
            _dbService.GetSpDetailsAsync(Arg.Any<string>(), "dbo", "USP_Test", Arg.Any<int>())
                .Returns(Task.FromResult(spDef));

            var specMarkdown = "## 개요\n## 파라미터 목록\n## CRUD 분석\n## 로직 흐름 요약\n## 비즈니스 흐름 시각화\n```mermaid\ngraph TD\nA-->B\n```";
            _aiService.GenerateSpecificationAsync(spDef, Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult(specMarkdown));

            var reviewResult = new ReviewResult { HasDefects = false };
            _aiService.ReviewSpecificationAsync(spDef, specMarkdown)
                .Returns(Task.FromResult(reviewResult));

            // L3 상호작용: 1차 피드백 -> 2차 승인
            _userInteraction.RequestHumanReviewAsync("dbo.USP_Test", specMarkdown)
                .Returns(
                    _ => Task.FromResult(new HumanReviewResult { Decision = UserDecision.ProvideFeedback, UserFeedback = "수정 의견" }),
                    _ => Task.FromResult(new HumanReviewResult { Decision = UserDecision.Approve })
                );

            // Act
            var (resultSpec, resultDef) = await _orchestrator.RunPipelineAsync(
                "connection_string", "dbo", "USP_Test", 3, "OpenAI", "instructions", isBatchMode: false);

            // Assert
            Assert.NotNull(resultSpec);
            await _userInteraction.Received(2).RequestHumanReviewAsync("dbo.USP_Test", Arg.Any<string>());
        }
    }
}
