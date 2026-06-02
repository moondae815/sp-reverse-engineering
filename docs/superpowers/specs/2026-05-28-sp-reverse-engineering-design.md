# SQL Server Stored Procedure Reverse Engineering Tool Design Specification

이 문서는 SQL Server 2022에 저장된 Stored Procedure(SP)를 조회하고 분석하여, AI를 통해 사용자 정의 지침에 맞춘 마크다운 형식의 기능 명세서를 자동 생성하는 CLI 도구의 설계 사양을 정의합니다.

- **작성일**: 2026-05-28 (최종 업데이트일: 2026-06-02)
- **상태**: 승인됨 (Approved)
- **대상 플랫폼**: .NET 8.0 / C#

---

## 1. 아키텍처 개요 (Architecture Overview)

본 시스템은 **클래스 라이브러리(Core)**와 **TUI/CLI 인터페이스(Cli)**를 물리적으로 분리하는 모듈형 아키텍처를 채택하여, 핵심 분석 엔진과 사용자 경험(UX) 영역의 관심사를 완벽히 분리합니다.

```
SP-Reverse-Engineering/
│
├── SP-Reverse-Engineering.slnx      # .NET 솔루션 파일
│
├── src/
│   ├── SpAnalyzer.Core/            # [클래스 라이브러리] 핵심 비즈니스 로직
│   │   ├── Models/                 # DB 및 AI 통신용 데이터 모델
│   │   ├── Services/               # DB 조회, AI 처리, 메타데이터 수출 서비스
│   │   └── SpAnalyzer.Core.csproj
│   │
│   └── SpAnalyzer.Cli/             # [콘솔 애플리케이션] Spectre.Console 기반 UI
│       ├── Program.cs              # CLI 진입점 및 의존성 주입(DI) 설정
│       ├── appsettings.json        # 시스템 구성 설정 파일
│       ├── instructions.txt        # 사용자 정의 AI 명세서 작성 규칙 파일
│       └── SpAnalyzer.Cli.csproj
│
└── tests/
    └── SpAnalyzer.Core.Tests/      # [단위 테스트 프로젝트] 핵심 비즈니스 로직 테스트
        └── SpAnalyzer.Core.Tests.csproj
```

### 핵심 프로젝트별 역할 및 의존성
1. **SpAnalyzer.Core**
   - 역할: SQL Server 2022 메타데이터 수집, 재귀적 의존성 및 스키마 정보 추적, AI 공급자 연동, 원천 수집 데이터의 덤프 파일 내보내기 처리.
   - 핵심 라이브러리: `Microsoft.Data.SqlClient`, `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`
2. **SpAnalyzer.Cli**
   - 역할: `appsettings.json` 로드, 로그인 계정 기억(세션 보관), 사용자 인터랙티브 입력 처리(자동완성 선택 프롬프트), 진행률 스피너 제공, 마크다운 파일 저장 및 원천 데이터 덤프 명령 제어.
   - 핵심 라이브러리: `Spectre.Console`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Hosting`

---

## 2. 데이터 모델 및 인터페이스 설계

### 2.1 데이터 모델 (Models)

```csharp
namespace SpAnalyzer.Core.Models
{
    public class SpDefinition
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string DdlText { get; set; } = string.Empty;
        public List<DependencyInfo> Dependencies { get; set; } = new();
    }

    public class DependencyInfo
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "USER_TABLE", "SQL_SCALAR_FUNCTION", "SQL_STORED_PROCEDURE" 등
        public int DiscoveryDepth { get; set; }        // 발견된 깊이 단계 (1, 2, 3...)
        public List<ColumnInfo> Columns { get; set; } = new(); // 객체가 테이블인 경우
        public string? ReferencedDdlText { get; set; }        // 객체가 UDF/SP인 경우
    }

    public class ColumnInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
    }
}
```

### 2.2 서비스 인터페이스 설계

#### DB 메타데이터 서비스 인터페이스
```csharp
namespace SpAnalyzer.Core.Services
{
    public interface IDbMetadataService
    {
        Task<List<string>> GetStoredProcedureNamesAsync(string connectionString);
        Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName, int maxDepth);
    }
}
```

#### AI 서비스 인터페이스
```csharp
namespace SpAnalyzer.Core.Services
{
    public interface IAiService
    {
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions);
    }
}
```

#### 원천 데이터 내보내기 서비스 인터페이스 (Exporter)
```csharp
namespace SpAnalyzer.Core.Services
{
    public interface IMetadataExporter
    {
        /// <summary>
        /// 수집한 DB 원천 메타데이터와 조립된 프롬프트 원문을 디렉터리에 출력 저장합니다.
        /// </summary>
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

---

## 3. 사용자 인터페이스 (TUI) 및 로그인 세션 설계

### 3.1 DB 로그인 정책 및 인터랙티브 입력
보안 및 개발자 편의성을 위해 DB 로그인 및 세션 유지 방식은 다음과 같습니다.
1. **마지막 계정 로컬 보관**: 성공적으로 연결된 DB 계정 ID를 로컬 세션 파일(`.session.json`)에 임시 보관하여, 다음 프로그램 실행 시 계정 입력창의 기본 제안값(Default Value)으로 보여줍니다.
2. **보안 비밀번호 입력**: 비밀번호는 매 실행 시마다 새로 입력을 받되, `Spectre.Console`의 `Secret()` 속성을 활성화하여 화면상에 텍스트가 마스킹 처리되도록 합니다.

### 3.2 SP 자동완성 및 검색
`Spectre.Console`의 `SelectionPrompt` 검색 기능을 통해 타이핑 시 실시간으로 SP 목록이 필터링되는 자동완성 형태를 구성합니다. (`EnableSearch()` 기능 활성화)

---

## 4. 설정 및 사용자 정의 지침 규격

### 4.1 `appsettings.json` 예시
```json
{
  "DatabaseSettings": {
    "Server": "localhost",
    "Database": "MyBusinessDb",
    "MaxDependencyDepth": 3
  },
  "AiSettings": {
    "Provider": "OpenAI",
    "ModelName": "gpt-4o",
    "ApiKey": "YOUR_API_KEY",
    "Endpoint": "",
    "Temperature": 0.2
  },
  "OutputSettings": {
    "Directory": "./output",
    "InstructionsFile": "./instructions.txt",
    "SaveRawJson": true,           // SpDefinition의 JSON 파일 덤프 옵션
    "SaveRawContext": true,        // AI 전송 원본 프롬프트 저장 옵션
    "SaveRawFiles": true           // 객체별 파일/폴더 개별 분산 덤프 옵션
  }
}
```

### 4.2 원천 데이터 분산 파일 내보내기 사양 (`SaveRawFiles = true`)
해당 저장 옵션이 활성화되면, 출력 대상 폴더 하위에 개별 객체별 정보가 분할 저장됩니다.

- `[schema].[name]_Raw/sp_definition.sql`: 원본 SP 생성 DDL 코드.
- `[schema].[name]_Raw/tables/[table_schema].[table_name].md`: 각 참조 테이블 스키마 표.
- `[schema].[name]_Raw/functions/[func_schema].[func_name].sql`: 각 참조 함수 코드 본문.
- `[schema].[name]_Raw/procedures/[proc_schema].[proc_name].sql`: 각 참조 하위 프로시저 코드 본문.

---

## 5. 예외 및 에러 처리 (Error Handling)

1. **DB 연결 실패 시**: 예외 메시지를 보기 좋은 경고 패널로 출력하고 프로그램을 안전하게 종료합니다.
2. **의존성 쿼리 및 DDL 부재**: 대상 SP의 메타데이터를 수집하지 못할 경우, 경고 처리 후 SP 선택 화면으로 되돌아갑니다.
3. **지침 파일 누락**: `instructions.txt` 파일이 누락되었을 경우 경고 문구를 콘솔에 노출하고, 내부(Fallback)에 내장된 기본 AI 템플릿을 사용하여 분석을 진행합니다.
4. **AI 통신 및 API 장애**: API 호출 중 네트워크 에러나 API 키 유효성 문제 발생 시 스피너를 중단하고, 에러 요약본을 출력한 뒤 세션을 복귀시킵니다.
5. **의존 객체 수집 중 권한 부족**: 재귀 수집 과정에서 암호화되거나 권한이 없어 소스코드를 얻지 못하는 의존 객체가 있다면 이를 스킵하고 다른 객체를 수집하도록 예외를 안전하게 처리(Soft fail)합니다.
6. **원천 메타데이터 덤프 오류**: 파일 쓰기 등의 오류가 발생하더라도 핵심 명세서 마크다운 생성은 중단되지 않도록 덤프 오류를 예외 격리(Try-catch) 처리합니다.
