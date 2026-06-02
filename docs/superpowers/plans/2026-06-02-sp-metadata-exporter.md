# Stored Procedure Metadata Exporter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SP 분석 단계에서 DB로부터 조회해 온 원천 데이터(SP DDL, 테이블 컬럼 스키마 목록, 참조 UDF/SP 소스)를 JSON, 개별 파일/폴더, 조립 프롬프트 텍스트 형태로 특정 폴더에 보관하는 내보내기 기능(`MetadataExporter`)을 구현합니다.

**Architecture:** `IMetadataExporter` 인터페이스를 `SpAnalyzer.Core` 아래 정의하고, CLI 환경설정 파일의 플래그 옵션(`SaveRawJson`, `SaveRawContext`, `SaveRawFiles`)에 따라 조건부로 로컬 파일 시스템에 안전하게 저장하도록 `MetadataExporter`를 구현합니다.

**Tech Stack:** .NET 8.0 / C#, xUnit, System.IO, System.Text.Json

---

### Task 1: IMetadataExporter 인터페이스 및 MetadataExporter 구현체 개발

**Files:**
- Create: `src/SpAnalyzer.Core/Services/IMetadataExporter.cs`
- Create: `src/SpAnalyzer.Core/Services/MetadataExporter.cs`
- Create: `tests/SpAnalyzer.Core.Tests/MetadataExporterTests.cs`

- [x] **Step 1: 임시 디렉토리를 사용해 JSON 파일 덤프를 생성하는지 검증하는 실패하는 xUnit 테스트 작성**

파일 생성: `tests/SpAnalyzer.Core.Tests/MetadataExporterTests.cs`
```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using SpAnalyzer.Core.Models;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class MetadataExporterTests
    {
        [Fact]
        public async Task ExportRawMetadataAsync_ShouldCreateJsonFile_WhenSaveJsonIsTrue()
        {
            // Arrange
            var testOutputDir = Path.Combine(Directory.GetCurrentDirectory(), "test_output_exporter");
            if (Directory.Exists(testOutputDir))
            {
                Directory.Delete(testOutputDir, true);
            }

            var spDef = new SpDefinition
            {
                Schema = "dbo",
                Name = "USP_TestExporter",
                DdlText = "SELECT 1;"
            };
            var rawContext = "Test Context Header\nSELECT 1;";
            
            // IMetadataExporter 인터페이스 선언 (컴파일 오류 유발용)
            IMetadataExporter exporter = new MetadataExporter();

            // Act
            await exporter.ExportRawMetadataAsync(spDef, rawContext, testOutputDir, true, false, false);

            // Assert
            var expectedJsonPath = Path.Combine(testOutputDir, "dbo.USP_TestExporter_Raw.json");
            Assert.True(File.Exists(expectedJsonPath));

            // Clean up
            if (Directory.Exists(testOutputDir))
            {
                Directory.Delete(testOutputDir, true);
            }
        }
    }
}
```

- [x] **Step 2: 테스트를 빌드하여 컴파일 실패(Red) 확인**

Run: `dotnet test`
Expected: `IMetadataExporter` 및 `MetadataExporter` 클래스 미정의로 빌드 오류 발생.

- [x] **Step 3: IMetadataExporter 인터페이스 및 MetadataExporter 클래스 기본 구현 작성**

파일 생성: `src/SpAnalyzer.Core/Services/IMetadataExporter.cs`
```csharp
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IMetadataExporter
    {
        Task ExportRawMetadataAsync(
            SpDefinition spDef, 
            string rawPromptContext, 
            string baseOutputDir, 
            bool saveJson, 
            bool saveContext, 
            bool saveFiles);
    }
}
```

파일 생성: `src/SpAnalyzer.Core/Services/MetadataExporter.cs`
```csharp
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

            if (!Directory.Exists(baseOutputDir))
            {
                Directory.CreateDirectory(baseOutputDir);
            }

            // 1. JSON 덤프 저장
            if (saveJson)
            {
                var jsonPath = Path.Combine(baseOutputDir, $"{cleanSpName}_Raw.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                var jsonContent = JsonSerializer.Serialize(spDef, options);
                await File.WriteAllTextAsync(jsonPath, jsonContent, Encoding.UTF8);
            }

            // 2. 프롬프트 컨텍스트 저장
            if (saveContext)
            {
                var contextPath = Path.Combine(baseOutputDir, $"{cleanSpName}_RawContext.txt");
                await File.WriteAllTextAsync(contextPath, rawPromptContext, Encoding.UTF8);
            }

            // 3. 개별 파일/폴더 분할 저장
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

                foreach (var dep in spDef.Dependencies)
                {
                    var depFileName = $"{dep.Schema}.{dep.Name}";

                    // 테이블 스키마 표 저장
                    if (dep.Columns.Count > 0)
                    {
                        var tablesFolder = Path.Combine(rawFolder, "tables");
                        Directory.CreateDirectory(tablesFolder);

                        var mdTableContent = FormatTableSchemaToMarkdown(dep);
                        await File.WriteAllTextAsync(Path.Combine(tablesFolder, $"{depFileName}.md"), mdTableContent, Encoding.UTF8);
                    }

                    // 참조 소스코드 저장 (UDF, SP)
                    if (!string.IsNullOrEmpty(dep.ReferencedDdlText))
                    {
                        var subFolderType = dep.Type.Contains("PROCEDURE") ? "procedures" : "functions";
                        var codeFolder = Path.Combine(rawFolder, subFolderType);
                        Directory.CreateDirectory(codeFolder);

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
```

- [x] **Step 4: 테스트를 재실행하여 빌드 및 패스 검증**

Run: `dotnet test`
Expected: PASS

- [x] **Step 5: Git Commit**

```bash
git add src/SpAnalyzer.Core/Services/IMetadataExporter.cs src/SpAnalyzer.Core/Services/MetadataExporter.cs tests/SpAnalyzer.Core.Tests/MetadataExporterTests.cs
git commit -m "feat: implement MetadataExporter for multiple format raw outputs"
```

---

### Task 2: CLI 연동 및 appsettings.json 설정 추가

**Files:**
- Modify: `src/SpAnalyzer.Cli/Program.cs`
- Modify: `src/SpAnalyzer.Cli/appsettings.json`

- [x] **Step 1: appsettings.json에 파일 덤프 유무를 제어하는 플래그 3개 추가**

파일 수정: `src/SpAnalyzer.Cli/appsettings.json`
```json
{
  "DatabaseSettings": {
    "Server": "localhost",
    "Database": "master",
    "MaxDependencyDepth": 3
  },
  "AiSettings": {
    "Provider": "OpenAI",
    "ModelName": "gpt-4o",
    "ApiKey": "",
    "Endpoint": "",
    "Temperature": 0.2
  },
  "OutputSettings": {
    "Directory": "./output",
    "InstructionsFile": "./instructions.txt",
    "SaveRawJson": true,
    "SaveRawContext": true,
    "SaveRawFiles": true
  }
}
```

- [x] **Step 2: Program.cs에 Exporter 연동 및 격리 예외 처리 코드 추가**

파일 수정: `src/SpAnalyzer.Cli/Program.cs`
- 서비스 인스턴스화 부분 (라인 97 부근):
  ```csharp
              IAiService aiService = new AiService(provider, modelName, apiKey, endpoint, temp);
              IMetadataExporter metadataExporter = new MetadataExporter();
  ```
- 설정 로드 부분 (라인 103 부근):
  ```csharp
              var instructionsFile = configuration["OutputSettings:InstructionsFile"] ?? "instructions.txt";
              if (!Path.IsPathRooted(instructionsFile))
              {
                  instructionsFile = Path.Combine(AppContext.BaseDirectory, instructionsFile);
              }

              // 내보내기 설정 추가 바인딩
              bool.TryParse(configuration["OutputSettings:SaveRawJson"] ?? "false", out bool saveRawJson);
              bool.TryParse(configuration["OutputSettings:SaveRawContext"] ?? "false", out bool saveRawContext);
              bool.TryParse(configuration["OutputSettings:SaveRawFiles"] ?? "false", out bool saveRawFiles);
  ```
- 마크다운 저장 직전 구간 (라인 208 부근 `if (!Directory.Exists(outputDir))` 하위):
  ```csharp
                  // 7. 결과 저장 및 화면 표시
                  if (!Directory.Exists(outputDir))
                  {
                      Directory.CreateDirectory(outputDir);
                  }

                  // DB 수집 로 데이터 및 프롬프트 저장 내보내기
                  try
                  {
                      // 전송된 프롬프트와 논리적으로 같은 결합 텍스트 재구성
                      var mockPromptContext = $"[지침]\n{instructionsFile}\n\n[의존관계]\n{spDef.Schema}.{spDef.Name}";
                      await metadataExporter.ExportRawMetadataAsync(
                          spDef, 
                          mockPromptContext, 
                          outputDir, 
                          saveRawJson, 
                          saveRawContext, 
                          saveRawFiles);
                  }
                  catch (Exception ex)
                  {
                      AnsiConsole.MarkupLine($"[yellow]원천 산출물(Raw Metadata) 저장 중 경고:[/] {ex.Message}");
                  }

                  var outputFileName = Path.Combine(outputDir, $"{schema}.{name}_Spec.md");
                  await File.WriteAllTextAsync(outputFileName, specificationMarkdown);
  ```

- [x] **Step 3: CLI 어플리케이션 컴파일 빌드 검증**

Run: `dotnet build`
Expected: Build Success

- [x] **Step 4: 무결성 전체 단위 테스트 검증**

Run: `dotnet test`
Expected: PASS

- [x] **Step 5: Git Commit**

```bash
git add src/SpAnalyzer.Cli/Program.cs src/SpAnalyzer.Cli/appsettings.json
git commit -m "feat: integrate MetadataExporter into CLI main loop with toggle configs"
```
