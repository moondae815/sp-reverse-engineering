using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class DbMetadataService : IDbMetadataService
    {
        public async Task<List<string>> GetStoredProcedureNamesAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            var spList = new List<string>();
            var query = @"
                SELECT ROUTINE_SCHEMA + '.' + ROUTINE_NAME 
                FROM INFORMATION_SCHEMA.ROUTINES 
                WHERE ROUTINE_TYPE = 'PROCEDURE' 
                ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using (var cmd = new SqlCommand(query, conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            spList.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return spList;
        }

        // 헬퍼 메서드: 특정 객체의 DDL 원본 텍스트 조회
        private async Task<string> GetObjectDdlAsync(string connectionString, string schema, string objectName, CancellationToken cancellationToken)
        {
            var fullName = $"{schema}.{objectName}";
            var query = @"
                SELECT definition 
                FROM sys.sql_modules 
                WHERE object_id = OBJECT_ID(@FullName);";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", fullName);
                    var result = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString() ?? string.Empty;
                    }
                }
            }
            throw new InvalidOperationException($"'{fullName}'의 DDL 코드를 찾을 수 없습니다.");
        }

        // 헬퍼 메서드: 특정 객체의 1차 의존 정보 목록 수집
        private async Task<List<DependencyInfo>> GetRawDependenciesAsync(string connectionString, string schema, string objectName, CancellationToken cancellationToken)
        {
            var rawDeps = new List<DependencyInfo>();
            var fullName = $"{schema}.{objectName}";
            var query = @"
                SELECT 
                    COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), 'dbo') AS ReferencedSchema,
                    d.referenced_entity_name AS ReferencedEntityName,
                    o.type_desc AS ReferencedType
                FROM sys.sql_expression_dependencies d
                INNER JOIN sys.objects o ON d.referenced_id = o.object_id
                WHERE d.referencing_id = OBJECT_ID(@FullName);";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", fullName);
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            rawDeps.Add(new DependencyInfo
                            {
                                Schema = reader.GetString(0),
                                Name = reader.GetString(1),
                                Type = reader.GetString(2)
                            });
                        }
                    }
                }
            }
            return rawDeps;
        }

        // 메인 재귀 탐색 진입점
        public async Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName, int maxDepth, CancellationToken cancellationToken = default)
        {
            var spDef = new SpDefinition { Schema = schema, Name = spName };
            var spFullName = $"{schema}.{spName}";

            // 1. 메인 SP의 DDL 조회
            try
            {
                spDef.DdlText = await GetObjectDdlAsync(connectionString, schema, spName, cancellationToken);
            }
            catch (Exception ex)
            {
                spDef.Warnings.Add($"[{spFullName}] 메인 프로시저 DDL 수집 실패: {ex.Message}");
                throw;
            }

            // 2. 중복 방지 방문 해시셋 및 재귀 리스트 생성
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { spFullName };

            // 2.5 메인 SP 내 동적 SQL 의존성 선행 분석
            await ResolveDynamicSqlDependenciesAsync(connectionString, spDef.DdlText, 1, visited, spDef.Dependencies, spDef.Warnings, cancellationToken);
            
            // 3. 재귀 수집 시작
            await GatherDependenciesRecursiveAsync(connectionString, schema, spName, 1, maxDepth, visited, spDef.Dependencies, spDef.Warnings, cancellationToken);

            return spDef;
        }

        // 재귀 호출 메서드 (DFS)
        private async Task GatherDependenciesRecursiveAsync(
            string connectionString, string schema, string name, 
            int currentDepth, int maxDepth, 
            HashSet<string> visited, List<DependencyInfo> dependencies,
            List<string> warnings, CancellationToken cancellationToken)
        {
            if (currentDepth > maxDepth) return;

            List<DependencyInfo> rawDeps;
            try
            {
                rawDeps = await GetRawDependenciesAsync(connectionString, schema, name, cancellationToken);
            }
            catch (Exception ex)
            {
                warnings.Add($"[{schema}.{name}] 의존 관계 정보 수집 실패: {ex.Message}");
                return; // 수집 실패 시 조용히 스킵 (Soft Fail)
            }

            foreach (var rawDep in rawDeps)
            {
                var depFullName = $"{rawDep.Schema}.{rawDep.Name}";
                if (visited.Contains(depFullName)) continue;

                visited.Add(depFullName);

                var depInfo = new DependencyInfo
                {
                    Schema = rawDep.Schema,
                    Name = rawDep.Name,
                    Type = rawDep.Type,
                    DiscoveryDepth = currentDepth
                };

                // 스키마 조회 분기 (테이블, 뷰)
                if (rawDep.Type.Contains("TABLE") || rawDep.Type.Contains("VIEW"))
                {
                    try
                    {
                        depInfo.Columns = await GetTableColumnsAsync(connectionString, rawDep.Schema, rawDep.Name, cancellationToken);
                        depInfo.Description = await GetTableDescriptionAsync(connectionString, rawDep.Schema, rawDep.Name, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"[{depFullName}] 테이블 스키마 및 코멘트 정보 수집 실패: {ex.Message}");
                    }
                }
                // 코드 수집 및 하위 재귀 분기 (UDF, SP)
                else if (rawDep.Type.Contains("FUNCTION") || rawDep.Type.Contains("PROCEDURE"))
                {
                    try
                    {
                        depInfo.ReferencedDdlText = await GetObjectDdlAsync(connectionString, rawDep.Schema, rawDep.Name, cancellationToken);
                        
                        // 참조 DDL 내 동적 SQL 의존성 분석 수행
                        await ResolveDynamicSqlDependenciesAsync(
                            connectionString, depInfo.ReferencedDdlText, currentDepth, visited, dependencies, warnings, cancellationToken);

                        // 하위 재귀 수집 호출
                        await GatherDependenciesRecursiveAsync(
                            connectionString, rawDep.Schema, rawDep.Name, 
                            currentDepth + 1, maxDepth, visited, dependencies, warnings, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"[{depFullName}] 참조 객체 DDL 수집 실패: {ex.Message}");
                    }
                }

                dependencies.Add(depInfo);
            }
        }

        public async Task<List<ColumnInfo>> GetTableColumnsAsync(string connectionString, string schema, string tableName, CancellationToken cancellationToken = default)
        {
            var columns = new List<ColumnInfo>();
            var query = @"
                SELECT 
                    c.COLUMN_NAME,
                    c.DATA_TYPE + 
                        CASE 
                            WHEN c.CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN 
                                '(' + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN 'MAX' ELSE CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR(10)) END + ')'
                            WHEN c.NUMERIC_PRECISION IS NOT NULL AND c.NUMERIC_SCALE IS NOT NULL AND c.DATA_TYPE IN ('decimal', 'numeric') THEN 
                                '(' + CAST(c.NUMERIC_PRECISION AS VARCHAR(10)) + ',' + CAST(c.NUMERIC_SCALE AS VARCHAR(10)) + ')'
                            ELSE ''
                        END AS DataType,
                    CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                    ISNULL((SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' 
                              AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA 
                              AND tc.TABLE_NAME = c.TABLE_NAME 
                              AND kcu.COLUMN_NAME = c.COLUMN_NAME), 0) AS IsPrimaryKey,
                    ISNULL((SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                            WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY' 
                              AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA 
                              AND tc.TABLE_NAME = c.TABLE_NAME 
                              AND kcu.COLUMN_NAME = c.COLUMN_NAME), 0) AS IsForeignKey,
                    ISNULL((SELECT CAST(value AS NVARCHAR(1000))
                            FROM sys.extended_properties
                            WHERE major_id = OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME)
                              AND minor_id = COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'ColumnId')
                              AND class = 1
                              AND name = 'MS_Description'), '') AS Description
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @TableName
                ORDER BY c.ORDINAL_POSITION;";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Schema", schema);
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            columns.Add(new ColumnInfo
                            {
                                ColumnName = reader.GetString(0),
                                DataType = reader.GetString(1),
                                IsNullable = reader.GetInt32(2) == 1,
                                IsPrimaryKey = reader.GetInt32(3) == 1,
                                IsForeignKey = reader.GetInt32(4) == 1,
                                Description = reader.GetString(5)
                            });
                        }
                    }
                }
            }
            return columns;
        }

        private async Task<string> GetTableDescriptionAsync(string connectionString, string schema, string tableName, CancellationToken cancellationToken)
        {
            var fullName = $"{schema}.{tableName}";
            var query = @"
                SELECT CAST(value AS NVARCHAR(MAX)) 
                FROM sys.extended_properties 
                WHERE major_id = OBJECT_ID(@FullName) 
                  AND minor_id = 0 
                  AND class = 1
                  AND name = 'MS_Description';";

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync(cancellationToken);
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@FullName", fullName);
                        var result = await cmd.ExecuteScalarAsync(cancellationToken);
                        if (result != null && result != DBNull.Value)
                        {
                            return result.ToString() ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
                // 권한 오류 등 무시
            }
            return string.Empty;
        }

        // 동적 SQL DDL 텍스트 분석 및 누락된 의존 테이블 수집 헬퍼
        private async Task ResolveDynamicSqlDependenciesAsync(
            string connectionString, string ddlText, int currentDepth,
            HashSet<string> visited, List<DependencyInfo> dependencies,
            List<string> warnings, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(ddlText)) return;

            // 동적 SQL 감지 여부 파악 (EXEC, EXECUTE, sp_executesql)
            bool hasDynamicSql = ddlText.Contains("EXEC", StringComparison.OrdinalIgnoreCase) || 
                                 ddlText.Contains("EXECUTE", StringComparison.OrdinalIgnoreCase) || 
                                 ddlText.Contains("sp_executesql", StringComparison.OrdinalIgnoreCase);

            if (!hasDynamicSql) return;

            var tablePatterns = new[]
            {
                @"FROM\s+([a-zA-Z0-9_\.\[\]]+)",
                @"JOIN\s+([a-zA-Z0-9_\.\[\]]+)",
                @"INSERT\s+(?:INTO\s+)?([a-zA-Z0-9_\.\[\]]+)",
                @"UPDATE\s+([a-zA-Z0-9_\.\[\]]+)",
                @"MERGE\s+(?:INTO\s+)?([a-zA-Z0-9_\.\[\]]+)"
            };

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in tablePatterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(ddlText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    if (m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                    {
                        var rawName = m.Groups[1].Value.Trim().Replace("[", "").Replace("]", "");
                        if (!string.IsNullOrEmpty(rawName) && 
                            !rawName.Equals("SELECT", StringComparison.OrdinalIgnoreCase) && 
                            !rawName.Equals("INSERT", StringComparison.OrdinalIgnoreCase) && 
                            !rawName.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
                        {
                            candidates.Add(rawName);
                        }
                    }
                }
            }

            foreach (var candidate in candidates)
            {
                var schema = "dbo";
                var name = candidate;
                if (candidate.Contains('.'))
                {
                    var parts = candidate.Split('.', 2);
                    schema = parts[0];
                    name = parts[1];
                }

                var fullName = $"{schema}.{name}";
                if (visited.Contains(fullName)) continue;

                // 데이터베이스 실제 개체 여부 및 타입 조회
                string? objectType = null;
                var checkQuery = @"
                    SELECT type_desc 
                    FROM sys.objects 
                    WHERE object_id = OBJECT_ID(@FullName);";

                try
                {
                    using (var conn = new SqlConnection(connectionString))
                    {
                        await conn.OpenAsync(cancellationToken);
                        using (var cmd = new SqlCommand(checkQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@FullName", fullName);
                            var result = await cmd.ExecuteScalarAsync(cancellationToken);
                            if (result != null && result != DBNull.Value)
                            {
                                objectType = result.ToString();
                            }
                        }
                    }
                }
                catch
                {
                    // 조회 에러 시 소프트 페일로 스킵
                }

                if (objectType != null && (objectType.Contains("TABLE") || objectType.Contains("VIEW")))
                {
                    visited.Add(fullName);

                    var depInfo = new DependencyInfo
                    {
                        Schema = schema,
                        Name = name,
                        Type = objectType,
                        DiscoveryDepth = currentDepth,
                        Description = "Dynamic SQL Analysis"
                    };

                    try
                    {
                        depInfo.Columns = await GetTableColumnsAsync(connectionString, schema, name, cancellationToken);
                        depInfo.Description = await GetTableDescriptionAsync(connectionString, schema, name, cancellationToken);
                        if (string.IsNullOrEmpty(depInfo.Description))
                        {
                            depInfo.Description = "Dynamic SQL에 의해 동적 감지된 테이블";
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"[Dynamic SQL: {fullName}] 테이블 스키마 수집 실패: {ex.Message}");
                    }

                    dependencies.Add(depInfo);
                }
            }
        }
    }
}
