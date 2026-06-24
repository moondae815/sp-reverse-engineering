using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;
using ReSet.Validator.Core.Models;
using ReSet.Validator.Core.Services;

namespace ReSet.Core.Tests
{
    public class RunnerTests
    {
        [Fact]
        public void CSharpReflectionRunner_FindExecuteMethod_ShouldLocateExecuteAsync()
        {
            // Arrange
            var runner = new CSharpReflectionRunner();
            var type = typeof(MockTargetClass);

            // Reflection으로 private 메서드 호출하여 검증
            var methodInfo = runner.GetType().GetMethod("FindExecuteMethod", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(methodInfo);

            // Act
            var executeMethod = (MethodInfo?)methodInfo.Invoke(runner, new object[] { type });

            // Assert
            Assert.NotNull(executeMethod);
            Assert.Equal("ExecuteAsync", executeMethod.Name);
        }

        [Fact]
        public void CSharpReflectionRunner_CreateInstance_WithDbObjects_ShouldSucceed()
        {
            // Arrange
            var runner = new CSharpReflectionRunner();
            var type = typeof(MockTargetClass);
            
            // Dummy connection
            using var conn = new SqlConnection("Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;");
            
            var methodInfo = runner.GetType().GetMethod("CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(methodInfo);

            // Act
            var instance = methodInfo.Invoke(runner, new object?[] { type, conn, null!, "conn_str" });

            // Assert
            Assert.NotNull(instance);
            Assert.IsType<MockTargetClass>(instance);
        }

        [Fact]
        public void CSharpReflectionRunner_MapMethodParameters_ShouldMapCorrectly()
        {
            // Arrange
            var runner = new CSharpReflectionRunner();
            var type = typeof(MockTargetClass);
            var executeMethod = type.GetMethod("ExecuteAsync");
            Assert.NotNull(executeMethod);

            using var conn = new SqlConnection("Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;");
            
            var inputParams = new Dictionary<string, object?>
            {
                { "customerId", "CUST01" },
                { "age", 30 }
            };

            var methodInfo = runner.GetType().GetMethod("MapMethodParameters", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(methodInfo);

            // Act
            var args = (object?[]?)methodInfo.Invoke(runner, new object?[] { executeMethod, inputParams, conn, null! });

            // Assert
            Assert.NotNull(args);
            Assert.Equal(2, args.Length);
            Assert.Equal("CUST01", args[0]);
            Assert.Equal(30, args[1]);
        }

        [Fact]
        public async Task CSharpReflectionRunner_ExecuteAsync_HandleConnectionError_SoftFail()
        {
            // Arrange
            var runner = new CSharpReflectionRunner();
            
            // 테스트 프로젝트 자체 DLL을 타겟으로 잡음
            var currentAssemblyPath = typeof(RunnerTests).Assembly.Location;

            var testInputs = new TestInputsDto
            {
                ProcedureName = "dbo.MockTargetClass",
                TestCases = new List<TestCaseDto>
                {
                    new TestCaseDto
                    {
                        CaseId = "CASE01",
                        Parameters = new Dictionary<string, object?>
                        {
                            { "customerId", "CUST01" },
                            { "age", 30 }
                        }
                    }
                }
            };

            var inputsJson = JsonSerializer.Serialize(testInputs);
            // 잘못된 연결 문자열로 연결 오류 유도
            var invalidConnStr = "Server=invalid_server_xyz;Database=master;Connect Timeout=1;";

            // Act
            var resultsJson = await runner.ExecuteAsync(currentAssemblyPath, inputsJson, invalidConnStr, CancellationToken.None);

            // Assert
            Assert.False(string.IsNullOrEmpty(resultsJson));
            var output = JsonSerializer.Deserialize<ExecutionOutputDto>(resultsJson);
            Assert.NotNull(output);
            Assert.Single(output.ExecutionResults);
            Assert.Equal("CASE01", output.ExecutionResults[0].CaseId);
            Assert.Equal("FAIL", output.ExecutionResults[0].Status);
            Assert.False(string.IsNullOrEmpty(output.ExecutionResults[0].ErrorCode));
        }
    }

    // --- Reflection 테스트 대상 Mock 클래스 ---
    public class MockTargetClass
    {
        private readonly SqlConnection? _conn;
        private readonly SqlTransaction? _trans;

        public MockTargetClass(SqlConnection? conn)
        {
            _conn = conn;
        }

        public MockTargetClass(SqlConnection? conn, SqlTransaction? trans)
        {
            _conn = conn;
            _trans = trans;
        }

        public async Task<List<Dictionary<string, object>>> ExecuteAsync(string customerId, int age)
        {
            var result = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "CustomerID", customerId },
                    { "Age", age },
                    { "Status", "Active" }
                }
            };
            return await Task.FromResult(result);
        }
    }
}
