using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SpAnalyzer.Validator.Core.Models;

namespace SpAnalyzer.Validator.Core.Services
{
    public class SandboxSeedingService
    {
        public async Task SeedMockDataAsync(string connectionString, MockDataDto mockData)
        {
            if (mockData?.Tables == null || !mockData.Tables.Any())
                return;

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (var table in mockData.Tables)
                        {
                            if (string.IsNullOrWhiteSpace(table.TableName) || table.Rows == null || !table.Rows.Any())
                                continue;

                            string tableName = SanitizeTableName(table.TableName);

                            foreach (var row in table.Rows)
                            {
                                if (row == null || !row.Any())
                                    continue;

                                var columns = row.Keys.ToList();
                                var values = row.Values.ToList();

                                var colCsv = string.Join(", ", columns.Select(c => $"[{c}]"));
                                var paramNames = string.Join(", ", columns.Select((c, i) => $"@p{i}"));
                                var sql = $"INSERT INTO {tableName} ({colCsv}) VALUES ({paramNames})";

                                using (var cmd = new SqlCommand(sql, conn, transaction))
                                {
                                    for (int i = 0; i < columns.Count; i++)
                                    {
                                        var val = values[i];
                                        var dbVal = ConvertJsonValue(val);
                                        cmd.Parameters.AddWithValue($"@p{i}", dbVal ?? DBNull.Value);
                                    }
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }
                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public async Task CleanupMockDataAsync(string connectionString, MockDataDto mockData)
        {
            if (mockData?.Tables == null || !mockData.Tables.Any())
                return;

            // Delete in reverse order of Seeding to respect potential physical FK dependencies
            var tablesToCleanup = mockData.Tables.Select(t => t.TableName).Reverse().ToList();

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (var rawTableName in tablesToCleanup)
                        {
                            if (string.IsNullOrWhiteSpace(rawTableName))
                                continue;

                            string tableName = SanitizeTableName(rawTableName);
                            var sql = $"DELETE FROM {tableName}";
                            using (var cmd = new SqlCommand(sql, conn, transaction))
                            {
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }
                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private string SanitizeTableName(string rawTableName)
        {
            var parts = rawTableName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(".", parts.Select(p => $"[{p.Trim('[', ']')}]"));
        }

        private object? ConvertJsonValue(object? val)
        {
            if (val is JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        if (DateTime.TryParse(element.GetString(), out var dt))
                            return dt;
                        return element.GetString();
                    case JsonValueKind.Number:
                        if (element.TryGetInt64(out var l))
                            return l;
                        if (element.TryGetDouble(out var d))
                            return d;
                        return element.GetRawText();
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.False:
                        return false;
                    case JsonValueKind.Null:
                        return null;
                    default:
                        return element.GetRawText();
                }
            }
            return val;
        }
    }
}
