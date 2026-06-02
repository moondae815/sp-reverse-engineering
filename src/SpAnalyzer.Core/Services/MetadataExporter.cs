using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public class MetadataExporter : IMetadataExporter
    {
        public async Task ExportRawMetadataAsync(
            SpDefinition spDef, 
            string rawPromptContext, 
            string baseOutputDir, 
            bool saveJson, 
            bool saveContext, 
            bool saveFiles)
        {
            var cleanSpName = $"{spDef.Schema}.{spDef.Name}";

            // 1. 출력 기본 디렉터리 생성 보장
            if (!Directory.Exists(baseOutputDir))
            {
                Directory.CreateDirectory(baseOutputDir);
            }

            // 2. 단일 JSON 덤프 저장
            if (saveJson)
            {
                var jsonPath = Path.Combine(baseOutputDir, $"{cleanSpName}_Raw.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                var jsonContent = JsonSerializer.Serialize(spDef, options);
                await File.WriteAllTextAsync(jsonPath, jsonContent, Encoding.UTF8);
            }

            // 3. 프롬프트 컨텍스트 저장
            if (saveContext)
            {
                var contextPath = Path.Combine(baseOutputDir, $"{cleanSpName}_RawContext.txt");
                await File.WriteAllTextAsync(contextPath, rawPromptContext, Encoding.UTF8);
            }

            // 4. 개별 파일/폴더 분산 저장
            if (saveFiles)
            {
                var rawFolder = Path.Combine(baseOutputDir, $"{cleanSpName}_Raw");
                if (!Directory.Exists(rawFolder))
                {
                    Directory.CreateDirectory(rawFolder);
                }

                // 메인 SP DDL 저장
                var spDdlPath = Path.Combine(rawFolder, "sp_definition.sql");
                await File.WriteAllTextAsync(spDdlPath, spDef.DdlText, Encoding.UTF8);

                // 의존성 순회하여 개별 덤프
                foreach (var dep in spDef.Dependencies)
                {
                    var depFileName = $"{dep.Schema}.{dep.Name}";

                    // 테이블 스키마 md 저장
                    if (dep.Columns.Count > 0)
                    {
                        var tablesFolder = Path.Combine(rawFolder, "tables");
                        if (!Directory.Exists(tablesFolder))
                        {
                            Directory.CreateDirectory(tablesFolder);
                        }

                        var mdTableContent = FormatTableSchemaToMarkdown(dep);
                        await File.WriteAllTextAsync(Path.Combine(tablesFolder, $"{depFileName}.md"), mdTableContent, Encoding.UTF8);
                    }

                    // 코드형 객체 DDL 저장
                    if (!string.IsNullOrEmpty(dep.ReferencedDdlText))
                    {
                        var subFolderType = dep.Type.Contains("PROCEDURE") ? "procedures" : "functions";
                        var codeFolder = Path.Combine(rawFolder, subFolderType);
                        if (!Directory.Exists(codeFolder))
                        {
                            Directory.CreateDirectory(codeFolder);
                        }

                        await File.WriteAllTextAsync(Path.Combine(codeFolder, $"{depFileName}.sql"), dep.ReferencedDdlText, Encoding.UTF8);
                    }
                }
            }
        }

        private string FormatTableSchemaToMarkdown(DependencyInfo dep)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 테이블 스키마: {dep.Schema}.{dep.Name}");
            sb.AppendLine($"* 객체 타입: {dep.Type}");
            sb.AppendLine($"* 발견 깊이: {dep.DiscoveryDepth}단계");
            sb.AppendLine();
            sb.AppendLine("| 컬럼명 | 데이터 타입 | Null 허용 | 제약 조건 |");
            sb.AppendLine("| :--- | :--- | :---: | :--- |");
            
            foreach (var col in dep.Columns)
            {
                var constraints = new System.Collections.Generic.List<string>();
                if (col.IsPrimaryKey) constraints.Add("PRIMARY KEY");
                if (col.IsForeignKey) constraints.Add("FOREIGN KEY");
                
                var constraintStr = string.Join(", ", constraints);
                var nullableStr = col.IsNullable ? "Yes" : "No";
                
                sb.AppendLine($"| {col.ColumnName} | {col.DataType} | {nullableStr} | {constraintStr} |");
            }
            return sb.ToString();
        }
    }
}
