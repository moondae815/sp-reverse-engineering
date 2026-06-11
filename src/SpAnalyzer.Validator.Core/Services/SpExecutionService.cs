using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpAnalyzer.Validator.Core.Models;

namespace SpAnalyzer.Validator.Core.Services
{
    public class SpExecutionService
    {
        public async Task<string> ExecuteStoredProcedureAsync(string connectionString, string testInputsJson, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("데이터베이스 연결 문자열이 지정되지 않았습니다.", nameof(connectionString));
            }

            // 1. 입력 파라미터 JSON 역직렬화
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var inputData = JsonSerializer.Deserialize<TestInputsDto>(testInputsJson, options);
            if (inputData == null || string.IsNullOrEmpty(inputData.ProcedureName))
            {
                throw new ArgumentException("유효하지 않은 테스트 파라미터 JSON 형식입니다.");
            }

            var outputResult = new ExecutionOutputDto
            {
                ProcedureName = inputData.ProcedureName,
                ExecutionResults = new List<TestCaseResultDto>()
            };

            // 2. DB 연결 및 각 테스트 케이스 실행
            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync(cancellationToken);

                    foreach (var testCase in inputData.TestCases)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var caseResult = new TestCaseResultDto
                        {
                            CaseId = testCase.CaseId,
                            ResultSets = new List<List<Dictionary<string, object>>>()
                        };

                        try
                        {
                            using (var cmd = new SqlCommand(inputData.ProcedureName, conn))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;
                                cmd.CommandTimeout = 30; // 30초 제한

                                // 파라미터 바인딩
                                if (testCase.Parameters != null)
                                {
                                    foreach (var param in testCase.Parameters)
                                    {
                                        // 파라미터 이름 접두사 '@' 보정
                                        var paramName = param.Key.StartsWith("@") ? param.Key : "@" + param.Key;
                                        
                                        // Null 값 바인딩 지원
                                        object val = param.Value ?? DBNull.Value;
                                        
                                        // JSON 문자열의 숫자/불리언 파싱 처리 유연성 부여 (단순 바인딩 시 필요하면 변환 적용 가능하지만 SqlClient가 문자열을 암시적으로 타겟 형식으로 컨버팅하게 둠)
                                        cmd.Parameters.AddWithValue(paramName, val);
                                    }
                                }

                                // 다중 Result Set 읽기 수행
                                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                                {
                                    do
                                    {
                                        var rows = new List<Dictionary<string, object>>();
                                        while (await reader.ReadAsync(cancellationToken))
                                        {
                                            var row = new Dictionary<string, object>();
                                            for (int i = 0; i < reader.FieldCount; i++)
                                            {
                                                var colName = reader.GetName(i);
                                                var colValue = reader.GetValue(i);
                                                row[colName] = colValue == DBNull.Value ? null! : colValue;
                                            }
                                            rows.Add(row);
                                        }
                                        caseResult.ResultSets.Add(rows);
                                    } while (await reader.NextResultAsync(cancellationToken));
                                }
                            }

                            caseResult.Status = "SUCCESS";
                        }
                        catch (Exception ex)
                        {
                            // DB 실행 예외(제약조건 에러 등) 발생 시 Soft Fail로 실패 내역 기록
                            caseResult.Status = "FAIL";
                            caseResult.ErrorCode = ex.Message;
                        }

                        outputResult.ExecutionResults.Add(caseResult);
                    }
                }
            }
            catch (Exception connEx)
            {
                // DB 연결 자체에 실패한 경우 모든 테스트 케이스를 실패 상태로 기록
                foreach (var testCase in inputData.TestCases)
                {
                    outputResult.ExecutionResults.Add(new TestCaseResultDto
                    {
                        CaseId = testCase.CaseId,
                        Status = "FAIL",
                        ErrorCode = $"데이터베이스 연결 실패: {connEx.Message}"
                    });
                }

                // 입력 케이스가 없는 경우 임의의 실패 케이스 하나를 추가
                if (outputResult.ExecutionResults.Count == 0)
                {
                    outputResult.ExecutionResults.Add(new TestCaseResultDto
                    {
                        CaseId = "CONNECTION_ERROR",
                        Status = "FAIL",
                        ErrorCode = $"데이터베이스 연결 실패: {connEx.Message}"
                    });
                }
            }

            return JsonSerializer.Serialize(outputResult, new JsonSerializerOptions { WriteIndented = true });
        }


    }
}
