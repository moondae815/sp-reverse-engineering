# SQL Server Stored Procedure Reverse Engineering Tool Design Specification

이 문서는 SQL Server 2022에 저장된 Stored Procedure(SP)를 조회하고 분석하여, AI를 통해 사용자 정의 지침에 맞춘 마크다운 형식의 기능 명세서를 자동 생성하는 CLI 도구의 설계 사양을 정의합니다.

- **작성일**: 2026-05-28
- **상태**: 승인됨 (Approved)
- **대상 플랫폼**: .NET 8.0 / C#

---

## 1. 아키텍처 개요 (Architecture Overview)

본 시스템은 **클래스 라이브러리(Core)**와 **TUI/CLI 인터페이스(Cli)**를 물리적으로 분리하는 모듈형 아키텍처를 채택하여, 핵심 분석 엔진과 사용자 경험(UX) 영역의 관심사를 완벽히 분리합니다.

```
SP-Reverse-Engineering/
│
├── SP-Reverse-Engineering.sln      # .NET 솔루션 파일
│
├── src/
│   ├── SpAnalyzer.Core/            # [클래스 라이브러리] 핵심 비즈니스 로직
│   │   ├── Models/                 # DB 및 AI 통신용 데이터 모델
│   │   ├── Services/               # DB 조회, AI 처리 서비스 인터페이스 및 구현
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
   - 역할: SQL Server 2022 메타데이터 수집, 시스템 뷰 기반 의존성 추적, AI 공급자(OpenAI, Claude, Gemini, Ollama) 연동 추상화 및 처리.
   - 핵심 라이브러리: `Microsoft.Data.SqlClient`, `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`
2. **SpAnalyzer.Cli**
   - 역할: `appsettings.json` 로드, 로그인 계정 기억(세션 보관), 사용자 인터랙티브 입력 처리(자동완성 선택 프롬프트), 진행률 스피너 제공, 마크다운 파일 저장.
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
        public string DdlText { get; set; } = string.Empty; // SP 생성 DDL 쿼리 본문
        public List<DependencyInfo> Dependencies { get; set; } = new();
    }

    public class DependencyInfo
    {
        public string Schema { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;    // 참조 대상 객체명 (예: Users 테이블)
        public string Type { get; set; } = string.Empty;    // 객체 유형 (TABLE, VIEW, FUNCTION, PROCEDURE 등)
    }
}
```

### 2.2 DB 메타데이터 서비스 인터페이스

```csharp
namespace SpAnalyzer.Core.Services
{
    public interface IDbMetadataService
    {
        // 자동완성용 전체 SP 목록을 조회합니다.
        Task<List<string>> GetStoredProcedureNamesAsync(string connectionString);

        // 지정된 SP의 DDL 소스코드와 SQL 의존 관계(Dependencies)를 추출합니다.
        Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName);
    }
}
```

* **의존성 정보 조회용 SQL 쿼리**:
  ```sql
  SELECT 
      COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), 'dbo') AS ReferencedSchema,
      d.referenced_entity_name AS ReferencedEntityName,
      o.type_desc AS ReferencedType
  FROM sys.sql_expression_dependencies d
  INNER JOIN sys.objects o ON d.referenced_id = o.object_id
  WHERE d.referencing_id = OBJECT_ID(@SpFullName);
  ```

### 2.3 AI 서비스 인터페이스

```csharp
namespace SpAnalyzer.Core.Services
{
    public interface IAiService
    {
        // SP 메타데이터와 사용자 지침 파일을 결합하여 AI 기능 명세서를 마크다운으로 생성합니다.
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions);
    }
}
```

---

## 3. 사용자 인터페이스 (TUI) 및 로그인 세션 설계

### 3.1 DB 로그인 정책 및 인터랙티브 입력
보안 및 개발자 편의성을 위해 DB 로그인 및 세션 유지 방식은 다음과 같습니다.

1. **마지막 계정 로컬 보관**: 
   성공적으로 연결된 DB 계정 ID를 로컬 세션 파일(`.session.json`)에 임시 보관하여, 다음 프로그램 실행 시 계정 입력창의 기본 제안값(Default Value)으로 보여줍니다. (보안을 위해 `.session.json`은 Git 관리 대상에서 제외)
2. **보안 비밀번호 입력**: 
   비밀번호는 매 실행 시마다 새로 입력을 받되, `Spectre.Console`의 `Secret()` 속성을 활성화하여 화면상에 텍스트가 마스킹되거나 표시되지 않도록 처리합니다.

```csharp
// Spectre.Console을 사용한 로그인 UI 흐름 예시
string lastUserId = LoadLastSessionUserId();

string userId = AnsiConsole.Prompt(
    new TextPrompt<string>("DB 계정을 입력하세요:")
        .DefaultValue(string.IsNullOrEmpty(lastUserId) ? "sa" : lastUserId)
);

string password = AnsiConsole.Prompt(
    new TextPrompt<string>("DB 비밀번호를 입력하세요:")
        .Secret()
);
```

### 3.2 SP 자동완성 및 검색
`Spectre.Console`의 `SelectionPrompt` 검색 기능을 통해 타이핑 시 실시간으로 SP 목록이 필터링되는 자동완성 형태를 구성합니다.

```csharp
var spNames = await dbService.GetStoredProcedureNamesAsync(connectionString);
var selectedSpName = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("분석할 [green]Stored Procedure[/]를 선택하거나 검색하세요:")
        .PageSize(10)
        .MoreChoicesText("[grey](더 많은 목록은 방향키를 누르세요)[/]")
        .AddChoices(spNames)
        .EnableSearch() // 실시간 타이핑 자동완성 필터링 기능
);
```

### 3.3 로딩 스피너 및 출력 결과
분석 중에는 `AnsiConsole.Status` 스피너를 보여주어 긴 대기 시간 동안 프로세스가 살아 있음을 시각화하고, 분석이 완료되면 결과 저장 경로를 담은 알림 패널을 출력합니다.

---

## 4. 설정 및 사용자 정의 지침 규격

### 4.1 `appsettings.json` 예시
```json
{
  "DatabaseSettings": {
    "Server": "localhost",
    "Database": "MyBusinessDb"
  },
  "AiSettings": {
    "Provider": "OpenAI",          // "OpenAI" | "Claude" | "Gemini" | "Ollama"
    "ModelName": "gpt-4o",         // 해당 공급자의 모델 지정
    "ApiKey": "YOUR_API_KEY",      // 로컬 Ollama 사용 시 빈 값
    "Endpoint": "",                // API 프록시 또는 로컬 Ollama 호출용 (예: http://localhost:11434/v1)
    "Temperature": 0.2
  },
  "OutputSettings": {
    "Directory": "./output",       // 마크다운 파일 저장 경로
    "InstructionsFile": "./instructions.txt"
  }
}
```

### 4.2 사용자 정의 지침 파일 (`instructions.txt`) 예시
AI 분석의 엄격한 포맷팅 준수를 위한 프롬프트 가이드라인을 외부 파일로 분리합니다.
```text
# Stored Procedure 기능 명세서 작성 지침

당신은 SQL Server Stored Procedure(SP)를 리버스 엔지니어링하여 비즈니스 로직 기능 명세서를 마크다운으로 작성하는 전문 소프트웨어 아키텍트입니다. 다음 규칙을 준수하세요:

1. 문서는 한글로 작성되어야 하며 읽기 쉽고 구조적이어야 합니다.
2. **문서 헤더**: SP의 전체 이름, 스키마, 그리고 생성 목적(Summary)을 명확하게 작성합니다.
3. **매개변수 분석**: 입력 및 출력 매개변수 목록을 테이블(표) 형태로 표현하고, 각 변수의 타입 및 역할을 기술합니다.
4. **참조 테이블 & CRUD 매트릭스**: 
   - SP 내에서 접근하는 모든 테이블 목록을 제시합니다.
   - 각 테이블에 대해 어떤 CRUD 작업(Create, Read, Update, Delete)을 수행하는지 분석하여 매트릭스 표로 표현합니다.
5. **비즈니스 로직 흐름**:
   - 로직의 흐름을 순차적으로 이해하기 쉽게 단계별(Step-by-Step) 요약합니다.
   - 트랜잭션 처리(BEGIN TRAN / COMMIT) 및 예외 처리(TRY...CATCH) 유무를 상세히 포함합니다.
```

---

## 5. 예외 및 에러 처리 (Error Handling)

1. **DB 연결 실패 시**: 네트워크 지연, 서버 오프라인, 잘못된 인증 정보 등의 경우 예외 메시지를 보기 좋은 경고 패널로 출력하고 즉시 로그인 절차로 되돌아가거나 프로그램을 안전하게 종료합니다.
2. **의존성 쿼리 및 DDL 부재**: 대상 SP의 메타데이터를 수집하지 못할 경우, 경고 처리 후 SP 선택 화면으로 되돌아갑니다.
3. **지침 파일 누락**: `instructions.txt` 파일이 누락되었을 경우 경고 문구를 콘솔에 노출하고, 내부(Fallback)에 내장된 기본 AI 템플릿을 사용하여 분석을 진행합니다.
4. **AI 통신 및 API 장애**: API 호출 중 네트워크 에러나 API 키 유효성 문제 발생 시 스피너를 중단하고, 에러 요약본을 출력한 뒤 세션을 원복시킵니다.
