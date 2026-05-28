using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class DbMetadataService : IDbMetadataService
    {
        public async Task<List<string>> GetStoredProcedureNamesAsync(string connectionString)
        {
            var spList = new List<string>();
            var query = @"
                SELECT ROUTINE_SCHEMA + '.' + ROUTINE_NAME 
                FROM INFORMATION_SCHEMA.ROUTINES 
                WHERE ROUTINE_TYPE = 'PROCEDURE' 
                ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            spList.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return spList;
        }

        public async Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName)
        {
            var spDef = new SpDefinition { Schema = schema, Name = spName };
            var spFullName = $"{schema}.{spName}";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                // 1. SP DDL 텍스트 조회
                var ddlQuery = @"
                    SELECT definition 
                    FROM sys.sql_modules 
                    WHERE object_id = OBJECT_ID(@SpFullName);";

                using (var cmd = new SqlCommand(ddlQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@SpFullName", spFullName);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        spDef.DdlText = result.ToString() ?? string.Empty;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Stored Procedure '{spFullName}'의 DDL 코드를 찾을 수 없습니다.");
                    }
                }

                // 2. 의존 객체 메타데이터 조회
                var depQuery = @"
                    SELECT 
                        COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), 'dbo') AS ReferencedSchema,
                        d.referenced_entity_name AS ReferencedEntityName,
                        o.type_desc AS ReferencedType
                    FROM sys.sql_expression_dependencies d
                    INNER JOIN sys.objects o ON d.referenced_id = o.object_id
                    WHERE d.referencing_id = OBJECT_ID(@SpFullName);";

                using (var cmd = new SqlCommand(depQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@SpFullName", spFullName);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            spDef.Dependencies.Add(new DependencyInfo
                            {
                                Schema = reader.GetString(0),
                                Name = reader.GetString(1),
                                Type = reader.GetString(2)
                            });
                        }
                    }
                }
            }

            return spDef;
        }
    }
}
