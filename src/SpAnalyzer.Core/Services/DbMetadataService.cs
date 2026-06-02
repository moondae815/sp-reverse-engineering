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

        public async Task<List<ColumnInfo>> GetTableColumnsAsync(string connectionString, string schema, string tableName)
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
                              AND kcu.COLUMN_NAME = c.COLUMN_NAME), 0) AS IsForeignKey
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @TableName
                ORDER BY c.ORDINAL_POSITION;";

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Schema", schema);
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            columns.Add(new ColumnInfo
                            {
                                ColumnName = reader.GetString(0),
                                DataType = reader.GetString(1),
                                IsNullable = reader.GetInt32(2) == 1,
                                IsPrimaryKey = reader.GetInt32(3) == 1,
                                IsForeignKey = reader.GetInt32(4) == 1
                            });
                        }
                    }
                }
            }
            return columns;
        }
    }
}
