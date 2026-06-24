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
│   ├── ReSet.Core/            # [클래스 라이브러리] 핵심 비즈니스 로직
│   │   ├── Models/                 # DB 및 AI 통신용 데이터 모델
│   │   ├── Services/               # DB 조회, AI 처리, 메타데이터 수출 서비스
│   │   └── ReSet.Core.csproj
│   │
│   └── ReSet.Cli/             # [콘솔 애플리케이션] Spectre.Console 기반 UI 및 CLI 파서
│       ├── Program.cs              # CLI 진입점, 옵션 분기 및 TUI/TUI 흐름 제어
│       ├── CliArgs.cs              # 명령행 아규먼트 파싱 데이터 모델
│       ├── appsettings.json        # 시스템 구성 설정 파일
│       ├── instructions.txt        # 사용자 정의 AI 명세서 작성 규칙 파일
│       └── ReSet.Cli.csproj
│
└── tests/
    └── ReSet.Core.Tests/      # [단위 테스트 프로젝트] 핵심 비즈니스 로직 테스트
        └── ReSet.Core.Tests.csproj
```

### 핵심 프로젝트별 역할 및 의존성
1. **ReSet.Core**
   - 역할: SQL Server 2022 메타데이터 수집, 재귀적 의존성 및 스키마 정보 추적, AI 공급자 연동, 원천 수집 데이터의 덤프 파일 내보내기 처리.
   - 핵심 라이브러리: `Microsoft.Data.SqlClient`, `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`
2. **ReSet.Cli**
   - 역할: `appsettings.json` 로드, 로그인 계정 기억(세션 보관), 사용자 인터랙티브 입력 처리(자동완성 선택 프롬프트) 및 무인 자동화 배치 명령줄 파싱, 진행률 스피너 제공, 마크다운 파일 저장 및 원천 데이터 덤프 명령 제어.
   - 핵심 라이브러리: `Spectre.Console`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Hosting`

---

## 2. 데이터 모델 및 인터페이스 설계

### 2.1 데이터 모델 (Models)

```csharp
namespace ReSet.Core.Models
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
        public string Type { get; set; } = string.Empty;
        public int DiscoveryDepth { get; set; }
        public List<ColumnInfo> Columns { get; set; } = new();
        public string? ReferencedDdlText { get; set; }
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
namespace ReSet.Core.Services
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
namespace ReSet.Core.Services
{
    public interface IAiService
    {
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions);
    }
}
```

#### 원천 데이터 내보내기 서비스 인터페이스 (Exporter)
```csharp
namespace ReSet.Core.Services
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

---

## 3. 사용자 인터페이스 (TUI) 및 무인 자동화 (CLI) 설계

### 3.1 DB 로그인 정책 및 인터랙티브 입력
보안 및 개발자 편의성을 위해 DB 로그인 및 세션 유지 방식은 다음과 같습니다.
1. **마지막 계정 로컬 보관**: 성공적으로 연결된 DB 계정 ID를 로컬 세션 파일(`.session.json`)에 임시 보관하여, 다음 프로그램 실행 시 계정 입력창의 기본 제안값(Default Value)으로 보여줍니다.
2. **보안 비밀번호 입력**: 비밀번호는 매 실행 시마다 새로 입력을 받되, `Spectre.Console`의 `Secret()` 속성을 활성화하여 화면상에 텍스트가 마스킹 처리되도록 합니다.

### 3.2 무인 자동화 (Batch) 분기 실행 설계 (`CliArgs`)
명령행 인수 파싱 결과에 따라 비대화형 자동화 모드로 실행할 수 있는 구조를 제공합니다.

- **옵션 명세**:
  - `--conn "[ConnectionString]"`: 환경변수 `SP_ANALYZER_CONN_STR` 혹은 이 플래그가 있으면 연결 문자열을 즉시 주입하여 ID/PW 대화형 입력을 생략합니다.
  - `--all`: 로그인 완료 후 전체 SP 목록을 조회하여 대화형 선택 프롬프트 없이 루프를 돌며 일괄 분석을 실행합니다.
  - `--sp "[Schema.SpName],[Schema.SpName]"`: 쉼표로 구분된 프로시저 목록만 타겟팅하여 대화형 선택 프롬프트 없이 연속 일괄 분석을 실행합니다.
- **분기 규칙**:
  - 위의 배치 옵션(`--all` 또는 `--sp`)이 입력 배열(`args`)에 포함되면 자동화 배치 모드로 진입하며, 그 외의 경우 기존 TUI 로그인 및 자동완성 검색 화면으로 기동합니다.
  - 배치 모드 실행 중 `--conn`이나 환경 변수로 연결 정보가 주입되지 않은 경우에는 에러 패널을 출력하고 프로그램 비정상 종료(Exit Code 1) 처리합니다.

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
    "SaveRawJson": true,
    "SaveRawContext": true,
    "SaveRawFiles": true
  }
}
```

### 4.2 AI 프롬프트 조립 및 Mermaid 다이어그램 자동화
명세서 문서의 시각화 수준을 극대화하기 위하여 지침 체계를 보완합니다.

1. **`instructions.txt` 지침 추가**:
   - Stored Procedure의 내부 조건 분기 및 데이터 변경(CRUD) 시퀀스를 시각적으로 요약할 수 있는 **Mermaid Flowchart** 다이어그램을 마크다운 출력 내에 ````mermaid ... ```` 블록 형태로 최소 1개 이상 작성하도록 지시합니다.
   - 다이어그램 컴파일 에러를 방지하기 위해 노드 라벨 명시 시 쌍따옴표 문자열 처리(예: `id["Text"]`)를 철저히 지키도록 명시합니다.
2. **`AiService` 시스템 프롬프트 가이드 보강**:
   - 의존 스키마 정보와 SP 쿼리 로직 흐름이 적합한 형태의 흐름도로 시각화될 수 있도록 시스템 명령 프롬프트 규칙에 Mermaid 생성을 필수화하는 가이드라인을 강제 주입합니다.

---

## 5. 예외 및 에러 처리 (Error Handling)

1. **DB 연결 실패 시**: 예외 메시지를 보기 좋은 경고 패널로 출력하고 프로그램을 안전하게 종료합니다. (배치 모드 시 Exit Code 1 반환)
2. **의존성 쿼리 및 DDL 부재**: 대상 SP의 메타데이터를 수집하지 못할 경우, 경고 처리 후 SP 선택 화면으로 되돌아갑니다. (배치 모드 시는 해당 SP를 스킵하고 다음 SP로 자동 전환)
3. **지침 파일 누락**: `instructions.txt` 파일이 누락되었을 경우 경고 문구를 콘솔에 노출하고, 내부(Fallback)에 내장된 기본 AI 템플릿을 사용하여 분석을 진행합니다.
4. **AI 통신 및 API 장애**: API 호출 중 네트워크 에러나 API 키 유효성 문제 발생 시 스피너를 중단하고, 에러 요약본을 출력한 뒤 세션을 복귀시킵니다.
5. **의존 객체 수집 중 권한 부족**: 재귀 수집 과정에서 암호화되거나 권한이 없어 소스코드를 얻지 못하는 의존 객체가 있다면 이를 스킵하고 다른 객체를 수집하도록 예외를 안전하게 처리(Soft fail)합니다.
6. **원천 메타데이터 덤프 오류**: 파일 쓰기 등의 오류가 발생하더라도 핵심 명세서 마크다운 생성은 중단되지 않도록 덤프 오류를 예외 격리(Try-catch) 처리합니다.
