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

        public async Task ExportMigrationInstructionsAsync(
            SpDefinition spDef,
            string specMarkdown,
            string migrationPlan,
            string baseOutputDir)
        {
            var cleanSpName = $"{spDef.Schema}.{spDef.Name}";
            var instructionsPath = Path.Combine(baseOutputDir, $"{cleanSpName}_MigrationInstructions.md");

            if (!Directory.Exists(baseOutputDir))
            {
                Directory.CreateDirectory(baseOutputDir);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# 🚀 Migration Instructions for Coding Agent ({cleanSpName})");
            sb.AppendLine();
            sb.AppendLine("본 문서는 SQL Server Stored Procedure에서 현대화된 배치 소스 코드로의 마이그레이션을 자동 수행하기 위해 코딩 에이전트(Claude Code, Antigravity CLI 등)에 제공되는 지시서 및 컨텍스트입니다.");
            sb.AppendLine("아래 설계 문서(Specification)와 레거시 SQL DDL 소스코드를 읽고 지침에 따라 현대화된 타겟 소스코드를 작성해 주십시오.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 📋 1. 마이그레이션 기본 정보");
            sb.AppendLine($"- **레거시 SP 개체**: `{cleanSpName}`");
            sb.AppendLine($"- **수집된 의존성 개체 개수**: {spDef.Dependencies.Count}개");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 📄 2. 현대화 비즈니스 설계서 (Business Specification)");
            sb.AppendLine("이 설계서는 레거시 SP의 핵심 비즈니스 로직, 입출력 스펙, 예외 처리 규칙을 명시합니다. 구현 시 반드시 이 동작 방식을 1:1로 만족해야 합니다.");
            sb.AppendLine();
            sb.AppendLine(specMarkdown);
            sb.AppendLine();
            
            if (!string.IsNullOrEmpty(migrationPlan))
            {
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## 🗺️ 3. 마이그레이션 전환 계획 (Migration Plan)");
                sb.AppendLine("배치 마이그레이션을 구성하기 위한 실행 계획입니다.");
                sb.AppendLine();
                sb.AppendLine(migrationPlan);
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 💻 4. 레거시 SQL Server Stored Procedure DDL 원본");
            sb.AppendLine("마이그레이션의 최종 로직 정합성을 유지하기 위해 참고해야 할 오리지널 SQL 코드입니다.");
            sb.AppendLine();
            sb.AppendLine("```sql");
            sb.AppendLine(spDef.DdlText);
            sb.AppendLine("```");
            sb.AppendLine();

            if (spDef.Dependencies.Count > 0)
            {
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## 📦 5. 의존하는 주요 참조 스키마 및 소스코드");
                sb.AppendLine("SP 내에서 참조하는 테이블, 함수, 또는 하위 SP들의 메타데이터입니다. 소스코드 마이그레이션 시 데이터 엑세스 계층(Repository/DAO) 구현에 참고하십시오.");
                sb.AppendLine();

                foreach (var dep in spDef.Dependencies)
                {
                    sb.AppendLine($"### 🔹 {dep.Type}: `{dep.Schema}.{dep.Name}`");
                    
                    if (dep.Columns.Count > 0)
                    {
                        sb.AppendLine(FormatTableSchemaToMarkdown(dep));
                    }
                    
                    if (!string.IsNullOrEmpty(dep.ReferencedDdlText))
                    {
                        sb.AppendLine("#### Referenced SQL DDL:");
                        sb.AppendLine("```sql");
                        sb.AppendLine(dep.ReferencedDdlText);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 🔑 6. 코딩 에이전트 명령 가이드 (Prompt for Agent)");
            sb.AppendLine("코딩 에이전트(Claude Code 등)에 입력할 때 아래 텍스트를 그대로 복사하여 사용하십시오:");
            sb.AppendLine();
            sb.AppendLine($"> \"이 파일(`{cleanSpName}_MigrationInstructions.md`)에 기술된 비즈니스 명세와 레거시 SQL DDL을 분석하여 현대화된 배치 소스 코드를 생성해줘.");
            sb.AppendLine("> 1. 설계서의 입출력 규격 및 비즈니스 로직 단계를 그대로 구현할 것.");
            sb.AppendLine("> 2. 생성할 파일 경로는 프로젝트 아키텍처 규칙에 맞춰 작성해줘.");
            sb.AppendLine("> 3. 구현이 완료되면 빌드가 통과하는지 검증하고 완료 메시지를 보여줘.\"");
            sb.AppendLine();

            await File.WriteAllTextAsync(instructionsPath, sb.ToString(), Encoding.UTF8);
        }

        private string FormatTableSchemaToMarkdown(DependencyInfo dep)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 테이블 스키마: {dep.Schema}.{dep.Name}");
            sb.AppendLine($"* 객체 타입: {dep.Type}");
            sb.AppendLine($"* 발견 깊이: {dep.DiscoveryDepth}단계");
            if (!string.IsNullOrEmpty(dep.Description))
            {
                sb.AppendLine($"* 테이블 설명: {dep.Description}");
            }
            sb.AppendLine();
            sb.AppendLine("| 컬럼명 | 데이터 타입 | Null 허용 | 제약 조건 | 설명 |");
            sb.AppendLine("| :--- | :--- | :---: | :--- | :--- |");
            
            foreach (var col in dep.Columns)
            {
                var constraints = new System.Collections.Generic.List<string>();
                if (col.IsPrimaryKey) constraints.Add("PRIMARY KEY");
                if (col.IsForeignKey) constraints.Add("FOREIGN KEY");
                
                var constraintStr = string.Join(", ", constraints);
                var nullableStr = col.IsNullable ? "Yes" : "No";
                
                sb.AppendLine($"| {col.ColumnName} | {col.DataType} | {nullableStr} | {constraintStr} | {col.Description} |");
            }
            return sb.ToString();
        }
    }
}
