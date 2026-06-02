# SQL Server Stored Procedure Reverse Engineering Tool Design Specification

이 문서는 SQL Server 2022에 저장된 Stored Procedure(SP)를 조회하고 분석하여, AI를 통해 사용자 정의 지침에 맞춘 마크다운 형식의 기능 명세서를 자동 생성하는 CLI 도구의 설계 사양을 정의합니다.

- **작성일**: 2026-05-28 (고도화 수정일: 2026-06-02)
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
   - 역할: SQL Server 2022 메타데이터 수집, 재귀적 의존성 및 스키마 정보 추적, AI 공급자(OpenAI, Claude, Gemini, Ollama) 연동 추상화 및 처리.
   - 핵심 라이브러리: `Microsoft.Data.SqlClient`, `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`
2. **SpAnalyzer.Cli**
   - 역할: `appsettings.json` 로드, 로그인 계정 기억(세션 보관), 사용자 인터랙티브 입력 처리(자동완성 선택 프롬프트), 진행률 스피너 제공, 마크다운 파일 저장.
   - 핵심 라이브러리: `Spectre.Console`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Hosting`

---

## 2. 데이터 모델 및 인터페이스 설계

### 2.1 데이터 모델 (Models)

재귀적으로 탐색한 참조 테이블의 스키마와 함수/SP의 소스코드를 수집하고 구조화하기 위한 모델입니다.

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

### 2.2 DB 메타데이터 서비스 인터페이스
```csharp
namespace SpAnalyzer.Core.Services
{
    public interface IDbMetadataService
    {
        // 자동완성용 전체 SP 목록을 조회합니다.
        Task<List<string>> GetStoredProcedureNamesAsync(string connectionString);

        // 지정된 SP의 DDL 소스코드와 재귀적 의존 객체 정보(상세 스키마 및 참조 코드 포함)를 추출합니다.
        Task<SpDefinition> GetSpDetailsAsync(string connectionString, string schema, string spName, int maxDepth);
    }
}
```

### 2.3 DB 데이터 조회 쿼리 사양

1. **의존성 쿼리**:
   ```sql
   SELECT 
       COALESCE(OBJECT_SCHEMA_NAME(d.referenced_id), 'dbo') AS ReferencedSchema,
       d.referenced_entity_name AS ReferencedEntityName,
       o.type_desc AS ReferencedType
   FROM sys.sql_expression_dependencies d
   INNER JOIN sys.objects o ON d.referenced_id = o.object_id
   WHERE d.referencing_id = OBJECT_ID(@SpFullName);
   ```

2. **테이블 스키마 상세 조회 쿼리**:
   ```sql
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
   ORDER BY c.ORDINAL_POSITION;
   ```

### 2.4 AI 서비스 인터페이스
```csharp
namespace SpAnalyzer.Core.Services
{
    public interface IAiService
    {
        // SP 메타데이터(참조 스키마 표 및 UDF 코드 본문 포함)와 사용자 지침 파일을 결합하여 마크다운 문서를 작성합니다.
        Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions);
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
    "MaxDependencyDepth": 3        // 재귀 의존성 분석 깊이 설정
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
    "InstructionsFile": "./instructions.txt"
  }
}
```

### 4.2 AI 프롬프트 조립 포맷 사양

`AiService`는 수집된 `Dependencies` 목록을 파싱하여, 테이블은 Markdown 표로 변환하고 UDF/SP는 DDL 소스코드로 가공하여 프롬프트에 동적으로 첨부합니다.

```text
분석 대상 Stored Procedure 정보:
- Schema: dbo
- Name: USP_GetOrderDetails

[참조 테이블 상세 스키마 정보 (Markdown Tables)]
### 테이블: dbo.Orders (USER_TABLE) - 발견 깊이: 1단계
| 컬럼명 | 데이터 타입 | Null 허용 | 제약 조건 |
| :--- | :--- | :---: | :--- |
| OrderId | int | No | PRIMARY KEY |
| CustomerId | int | Yes | FOREIGN KEY |

[참조 함수/SP 소스 코드 정의]
### 함수: dbo.fn_CalculateTax (SQL_SCALAR_FUNCTION) - 발견 깊이: 2단계
```sql
CREATE FUNCTION dbo.fn_CalculateTax...
```
```

---

## 5. 예외 및 에러 처리 (Error Handling)

1. **DB 연결 실패 시**: 예외 메시지를 보기 좋은 경고 패널로 출력하고 프로그램을 안전하게 종료합니다.
2. **의존성 쿼리 및 DDL 부재**: 대상 SP의 메타데이터를 수집하지 못할 경우, 경고 처리 후 SP 선택 화면으로 되돌아갑니다.
3. **지침 파일 누락**: `instructions.txt` 파일이 누락되었을 경우 경고 문구를 콘솔에 노출하고, 내부(Fallback)에 내장된 기본 AI 템플릿을 사용하여 분석을 진행합니다.
4. **AI 통신 및 API 장애**: API 호출 중 네트워크 에러나 API 키 유효성 문제 발생 시 스피너를 중단하고, 에러 요약본을 출력한 뒤 세션을 복귀시킵니다.
5. **의존 객체 수집 중 권한 부족**: 재귀 수집 과정에서 암호화되거나 권한이 없어 소스코드를 얻지 못하는 의존 객체가 있다면 이를 스킵하고 다른 객체를 수집하도록 예외를 안전하게 처리(Soft fail)합니다.
