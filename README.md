# SQL Server Stored Procedure Reverse Engineering Tool (SP Analyzer)

본 프로젝트는 **SQL Server 2022**에 저장된 Stored Procedure(SP)를 분석하여, AI(OpenAI, Ollama 등)를 통해 사용자 정의 지침에 맞춘 마크다운 형식의 기능 명세서를 자동 생성하는 개발자용 터미널 기반 CLI(TUI) 도구입니다.

---

## 🚀 주요 특징 (Key Features)

- **재귀적 하이브리드 의존성 분석**: 
  - 타겟 SP가 참조하는 테이블, 뷰, 사용자 정의 함수(UDF), 다른 SP를 깊이(Depth) 설정에 따라 **재귀적(DFS)**으로 추적합니다.
  - 중복 탐색 방지를 위한 방문 노드 제어(`HashSet`) 및 스키마 분석 권한 누락 시의 안전한 스킵(Soft Fail) 정책이 내장되어 있습니다.
  - 수집 실패(권한 오류 등) 시 예외를 무조건 삼키는 대신 오류/경고를 누적(`Warnings`)하여 TUI 화면(Spectre.Console Panel) 및 AI 프롬프트에 제공함으로써 수집 결과물 유실 상태를 명확히 인지하게 돕습니다.
- **상세 테이블 스키마 및 설명 자동 추출**:
  - 분석 대상 테이블들의 컬럼명, 데이터 타입(길이/정밀도 포함), Null 허용 여부를 수집합니다.
  - 시스템 메타데이터 뷰를 조회하여 **Primary Key(PK)** 및 **Foreign Key(FK)** 여부를 판별합니다.
  - **테이블 및 컬럼 주석(Extended Properties의 `MS_Description`)**을 함께 긁어와 AI 프롬프트에 자동으로 주입하여 분석 보고서의 정확성을 극대화하며, 산출물에 한글 설명 필드를 표 형태로 포함시킵니다.
- **배치 현대화 설계(Batch Modernization Planner) 지원**:
  - 개별 Stored Procedure의 비즈니스 정적 분석(`_Spec.md`)과 다중 분석서 기반의 **통합 배치 현대화 계획서 도출** 단계를 완전히 이원화하여 설계했습니다.
  - 사용자가 이미 분석 완료해 놓은 명세서 파일들을 원하는 만큼 수동 다중 선택(Multi-select)하여, 하나의 유기적인 배치 Job 아키텍처(예: .NET Worker Service, Java Spring Batch 등 지정된 프레임워크의 Job & Step 구조)로 전환하는 `[Job이름]_BatchMigrationPlan.md` 전환 계획 설계서를 자동으로 생성합니다.
  - **워크플로우 제어 및 재시작성(Restartability)**, **대용량 청크(Chunk) 페이징 연산**, **구조화된 에러 로깅 및 실패 알림 통합**, 그리고 **통합 데이터 정합성 검증 SQL 세트**를 도출해 줍니다.
  - **동적 SQL 및 Linked Server 대응 가이드라인**: DB 정적 의존성 분석(`sys.sql_expression_dependencies`)의 한계인 동적 SQL(`EXEC`, `EXECUTE`, `sp_executesql`)과 Linked Server(4파트 식별자) 호출 패턴을 AI 시스템 프롬프트 지침 고도화를 통해 정확히 포착하고 안전한 ORM 쿼리 및 멀티 데이터소스 전환 설계로 마이그레이션하도록 안내합니다.
- **참조 UDF 및 SP 소스코드 자동 주입**:
  - 호출되는 사용자 정의 함수 및 프로시저의 DDL 생성 쿼리 본문(`sys.sql_modules.definition`)을 DB에서 실시간으로 긁어와 AI에게 컨텍스트 소스코드로 함께 제공합니다.
- **원천 데이터(Raw Metadata) 다중 포맷 덤프 및 수출**:
  - AI 분석에 활용된 DB 수집 메타데이터 원본을 다양한 포맷으로 출력 디렉터리에 함께 보관하여 보존성을 높입니다.
    1. **구조화된 JSON 덤프**: 수집된 데이터 객체 전체를 `.json` 파일로 저장.
    2. **가공 프롬프트 텍스트**: AI 서비스로 전달된 조립 컨텍스트 원문을 `.txt` 파일로 저장.
    3. **개별 파일/폴더 분산 저장**: 메인 DDL(sql), 개별 테이블 스키마 표(md), 참조 함수/SP 본문(sql)을 계층화된 폴더 트리 구조로 각각 쪼개어 저장.
  - 저장 처리 도중 용량 부족 등으로 에러가 발생하더라도 핵심 명세서 저장은 지속되도록 예외 격리(Soft Fail) 처리를 적용했습니다.
- **비즈니스 흐름 시각화 (Mermaid Diagram)**: 명세서 내에 실행 흐름과 CRUD 제어 시퀀스를 표현하는 Mermaid Flowchart 자동 생성 지침이 내장되어 노드 단위 특수문자 문법 예외 처리까지 완벽히 대응합니다.
- **3단계 신뢰성 검증 파이프라인 (Verification Pipeline)**:
  - **개별 명세서와 통합 계획서 대칭 검증**: 개별 SP 분석서(`*_Spec.md`)뿐만 아니라, 2단계의 통합 배치 전환 계획서(`*_BatchMigrationPlan.md`) 작성 완료 시에도 동일한 3단계 검증 루프가 작동합니다.
  - **Level 1 (기계적 정적 검증)**: 마크다운 구조(AST) 파서인 **`Markdig`**을 이용하여 필수 섹션 헤더 누락 여부를 구조적으로 정밀 확인하고, 설정된 `UseMermaidCli` 옵션에 따라 **실시간 `mermaid-cli` 컴파일 테스트** 또는 기존 괄호 마스킹 규칙을 적용하여 Mermaid 다이어그램 구문을 린팅합니다. 통합 배치 계획서의 L1 검증 실패 시 교정 템플릿(`SuggestedPromptFix`)이 개별 명세서용으로 제안되던 버그를 구조적으로 분리하여 통합 계획서 전용 헤더 템플릿을 제안하도록 완성도를 높였습니다.
  - **Level 2 (AI 교차 검증 및 자가 보완)**: 1차 작성이 끝나면 검토관(Reviewer) 에이전트가 비즈니스 로직 및 통합 배치 흐름의 결함과 왜곡을 검증해 설정에 지정된 횟수(또는 검증 완료까지 무제한) 동안 자가 수정(`Self-Correction`) 보완 루프를 실행합니다. AI의 JSON 응답 파싱 시 마크다운 백틱 및 추가 설명 래핑으로 인한 파싱 예외를 원천 방지하기 위해 중괄호 `{ }` 추적 기반의 정교한 JSON 추출 알고리즘을 도입했습니다.
  - **Level 3 (인간 개입 피드백 루프)**: TUI 대화형 화면에서 개발자가 생성된 산출물의 전체 내용을 실시간 미리보기(Preview)로 확인한 후 최종 승인(`Approve`)하거나, 직접 자연어 보완 의견(`Feedback`)을 기재하여 즉시 재생성할 수 있습니다. 피드백 반영 후 다시 완성된 문서 역시 한 번 더 화면에 렌더링되며 만족할 때까지 다회차에 걸쳐 반복 검증할 수 있는 직관적인 인터랙티브 흐름을 제공합니다. (무인 배치 모드에서는 자동으로 생략)
- **다양한 AI 공급자 지원**: OpenAI(GPT) 외에도 Anthropic Claude, Google Gemini, 그리고 로컬 환경에서 실행되는 Ollama 등을 추상화 계층(`IAiClient`)을 통해 제약 없이 유연하게 연결 및 전환하여 사용할 수 있습니다.
- **커스터마이징 가능한 지침**: 외부 `instructions.md` 파일의 내용을 프롬프트에 자동으로 주입하여, 원하는 명세서 형식 및 분석 규칙을 텍스트 에디터로 간편하게 커스텀할 수 있습니다.
- **인터랙티브 TUI 제공**: 
  - **보안 로그인**: 직전 성공 계정을 로컬 세션 파일(`.session.json`)에 기억하여 재입력을 최소화하며, 비밀번호는 입력 시 화면에 마스킹(`Secret()`)되어 안전합니다.
  - **자동완성 검색**: 타이핑 시 실시간으로 SP 목록이 필터링되는 자동완성 검색 기능이 탑재되어 있습니다.
  - **안정적인 텍스트 렌더링**: 분석 진행률 표시 및 자가 검증 결과 출력 과정에서 대괄호(`[...]`) 등의 텍스트가 Spectre.Console의 마크업 태그로 오인되어 런타임 예외(`System.InvalidOperationException`)를 발생시키는 문제를 방지하기 위해, 모든 사용자 인터랙션 렌더링에 이스케이프(`Markup.Escape`) 처리를 적용하여 안전하고 견고한 UI 환경을 보장합니다.
  - **비동기 작업 취소 및 Ctrl+C 연동**: DB 조회나 AI 호출 도중 장시간 블록될 경우 TUI에서 `Ctrl+C` 인터럽트로 파이프라인을 중단하고 메인 메뉴로 복귀할 수 있도록 모든 비동기 핵심 서비스에 `CancellationToken` 연동을 완료했습니다.
- **테스트 주도 개발(TDD) 기반**: 솔루션 코드가 최종 추가된 검증 케이스를 포함한 48개의 xUnit 단위 테스트 코드를 통해 견고하게 검증되어 있습니다.
- **문서 이력 및 메타데이터 자동 추적**: 최종 저장되는 명세서 및 계획서 상단에 **생성 일시 및 사용된 AI 정보(Provider, Model)**가 YAML Front Matter 형식의 Alert 블록으로 자동 기입되어 관리 이력 추적이 용이합니다.
- **코드 일치성 및 런타임 데이터 정합성 검증 에이전트 (SpAnalyzer.Validator)**:
  - 역공학으로 추출된 비즈니스 명세서(`*_Spec.md`)와 실제 변환 구현된 소스코드 간의 **논리적 구문 일치성** 및 **실제 DB 실행 데이터 정합성**을 1:1 교차 검증하는 통합 QA 에이전트입니다.
  - **Level 1 (정적 검증)**: 소스코드의 중괄호 구조적 무결성 및 명세서 내의 클래스/메소드 명칭 일치성을 플러그인 인터페이스(`IValidatorPlugin`)를 통해 정적으로 린팅합니다.
  - **Level 2 (AI 논리 검증)**: AI가 입출력 파라미터 매핑, 반환 DTO 필드, 조건문/연산 분기, 예외 및 트랜잭션 제어를 상세 분석하여 불일치점(Gap) 보고서를 작성하고 자가 보완 루프를 실행합니다.
  - **Level 3 (인간 개입 TUI 검증)**: TUI 모드에서 AI 교차 검토 및 Gap 분석 결과를 실시간 확인하여 수동 승인(Approve)하거나 피드백 의견을 기재해 재생성을 제어합니다.
  - **데이터 정합성 검증 엔진 (Data Verification Runner)**: 명세서의 사양을 토대로 AI가 테스트 파라미터 케이스 JSON을 설계하고, 실제 Legacy DB에서 SP를 실행해 수집한 다중 결과셋 데이터를 신규 타겟 결과 데이터와 1:1로 비교 대조하여 세밀한 데이터 정합성 보고서(`*_CompareReport.md`)를 생성합니다. (오류 시 Soft Fail 격리 지원)
  - **사용성 편의 기능**: TUI 상에서 로컬 디렉토리 경로 입력 시 `Tab` 키로 실시간 자동완성을 지원하며, 디렉토리 미존재 시 강제 종료 없이 즉시 복구 선택 프롬프트를 제공합니다.

---

## 📂 프로젝트 구조 (Project Structure)

```
SP-Reverse-Engineering/
│
├── SP-Reverse-Engineering.slnx      # .NET 솔루션 파일
│
├── src/
│   ├── SpAnalyzer.Core/            # [클래스 라이브러리] 핵심 비즈니스 로직
│   │   ├── Models/                 # SpDefinition, DependencyInfo, ColumnInfo 데이터 모델
│   │   └── Services/               # DB 조회, AI API 통신 및 메타데이터 내보내기 서비스 구현
│   │
│   ├── SpAnalyzer.Cli/             # [콘솔 애플리케이션] Spectre.Console 기반 TUI (설계서 생성)
│   │   ├── Program.cs              # CLI 진입점 및 대화형 워크플로우 제어
│   │   ├── appsettings.json        # DB/AI 연동 설정 파일
│   │   └── instructions.md        # AI 분석 세부 지침 규칙 파일
│   │
│   ├── SpAnalyzer.Validator.Core/  # [클래스 라이브러리] 소스코드 정적/논리 검사, Legacy DB 런타임 데이터 수집 및 1:1 대조 정합성 검증 서비스
│   │
│   └── SpAnalyzer.Validator.Cli/   # [콘솔 애플리케이션] Spectre.Console 기반 TUI 및 배치 모드 (소스코드 및 데이터 정합성 대조 검증기)
│       ├── Program.cs              # 검증기 CLI 진입점 및 대화형 TUI / 배치 실행 흐름 제어
│       └── appsettings.json        # 검증기용 기본 설정 파일
│
└── tests/
    └── SpAnalyzer.Core.Tests/      # [단위 테스트 프로젝트] xUnit 기반 단위 테스트 (검증 테스트 포함)
```

---

## ⚙ 설정 방법 (Configuration)

### 1. `appsettings.json` 설정
프로그램 실행 전 `src/SpAnalyzer.Cli/appsettings.json` 파일을 열어 기본적인 데이터베이스 환경 및 출력 설정을 지정합니다. 자격 증명 누출 방지를 위해 이 파일의 `ApiKey`는 비워두는 것을 권장합니다.

```json
{
  "DatabaseSettings": {
    "Server": "localhost",          // SQL Server 주소
    "Database": "Northwind",        // 대상 데이터베이스 이름
    "MaxDependencyDepth": 3         // 재귀적 의존성 탐색의 최대 깊이 (기본값: 3)
  },
  "AiSettings": {
    "Provider": "OpenAI",          // 활성화할 AI 제공자 ("OpenAI" | "Gemini" | "Claude" | "Ollama")
    "ModelName": "gpt-4o",         // 사용할 LLM 모델명
    "Temperature": 0.2,            // 분석의 일관성을 위해 낮게(0.0 ~ 0.3) 설정을 권장합니다.
    "MaxL2Attempts": 2,            // L2 AI 교차 리뷰 실패 시 추가로 재시도할 자가 보완 횟수 (1 이상의 정수 또는 "unlimited" 지정 시 검증 완료까지 무제한)
    "Providers": {
      "OpenAI": {
        "ApiKey": "",              // OpenAI API 키
        "Endpoint": "https://api.openai.com/v1"
      },
      "Gemini": {
        "ApiKey": "",              // Gemini API 키 (Google AI Studio)
        "Endpoint": "https://generativelanguage.googleapis.com"
      },
      "Claude": {
        "ApiKey": "",              // Claude API 키 (Anthropic Console)
        "Endpoint": "https://api.anthropic.com"
      },
      "Ollama": {
        "Endpoint": "http://localhost:11434" // 로컬 Ollama 엔드포인트
      }
    }
  },
  "OutputSettings": {
    "Directory": "./output",       // 명세서 파일이 저장될 출력 디렉터리
    "InstructionsFile": "./instructions.md", // 분석 규칙 지침 파일 명칭
    "SaveRawJson": true,           // [설정] SpDefinition JSON 파일 저장 여부
    "SaveRawContext": true,        // [설정] 조립된 프롬프트 텍스트 원문 저장 여부
    "SaveRawFiles": true           // [설정] 의존성 개별 객체 파일/폴더 분산 덤프 여부
  },
  "MigrationSettings": {
    "Enabled": true,               // [설정] 신규 시스템 현대화 설계서 추가 생성 활성화 여부
    "TargetLanguage": "C#"         // [설정] 제안할 신규 시스템의 배치 프레임워크 언어 (C# | Java 등)
  },
  "ValidationSettings": {
    "UseMermaidCli": false,         // [설정] mmdc(mermaid-cli)를 이용한 Mermaid 실시간 렌더링 검사 수행 여부 (기본값: false)
    "SpecDirectory": "./output",          // [설정] 검증에 쓰일 명세서 폴더
    "SourceCodeDirectory": "./src",       // [설정] 검증에 쓰일 구현 소스코드 폴더
    "TargetLanguage": "Auto",             // [설정] 검증 대상 언어 ("Auto" | "C#" | "Java")
    "OutputDirectory": "./output/validation" // [설정] 일치성 Gap 보고서 저장 경로
  }
}
```

### 2. 보안 가이드: `appsettings.local.json` 설정 (권장)
보안상 안전하게 AI API Key 정보를 관리하기 위해, Git에 추적되지 않는 로컬 전용 설정 파일을 사용하는 것을 권장합니다.

1. `src/SpAnalyzer.Cli/` 디렉터리에 `appsettings.local.json` 파일을 만듭니다. (이 파일은 `.gitignore`에 무시 대상 파일로 이미 등록되어 안전합니다.)
2. 생성된 `appsettings.local.json` 파일 내에 다음과 같이 발급받은 API 키 설정을 넣으면 로컬 실행 시 보안 키가 우선적으로 적용됩니다.
   ```json
   {
     "AiSettings": {
       "Providers": {
         "OpenAI": {
           "ApiKey": "여기에_새로_발급받은_API키_입력"
         },
         "Gemini": {
           "ApiKey": "여기에_새로_발급받은_API키_입력"
         },
         "Claude": {
           "ApiKey": "여기에_새로_발급받은_API키_입력"
         }
       }
     }
   }
   ```

### 3. `instructions.md` 설정
분석된 결과물의 마크다운 포맷 규칙을 정의하는 가이드라인 파일입니다. `src/SpAnalyzer.Cli/instructions.md`에 작성된 텍스트 내용대로 AI가 리버스 엔지니어링 문서를 만듭니다.

---

## 🏃 실행 및 사용 방법 (Running the Tool)
 
### 1. 대화형 TUI 모드 실행
기본적으로 아무 아규먼트 없이 실행하면 로그인 정보 입력 및 메인 메뉴 선택이 가능한 TUI 모드로 시작합니다.
```bash
dotnet run --project src/SpAnalyzer.Cli
```
1. DB 계정(ID)과 패스워드를 입력하여 SQL Server에 로그인합니다.
2. 로그인 성공 시 아래 **메인 메뉴**가 화면에 표시됩니다:
   * **`1. Stored Procedure 개별 분석 명세서 작성`**:
     SP를 1개 선택하여, 해당 프로시저의 비즈니스 로직과 데이터 입출력 명세서(`*_Spec.md`)를 작성합니다.
   * **`2. 기분석 명세서 통합 배치 전환 계획 수립 (Multi-SP)`**:
     출력 디렉터리에 축적된 `*_Spec.md` 목록 중에서 통합할 대상들을 **스페이스바를 사용해 다중 선택**하고, Job 이름(예: `Daily_Order_Job`)을 입력하여 통합 배치 전환 계획서(`*_BatchMigrationPlan.md`)를 작성합니다.
     * **이전 메뉴로 돌아가기**: 파일 다중 선택 화면의 최상단에 제공되는 `[-- 메인 메뉴로 돌아가기 --]` 옵션을 선택하여 이전 메인 메뉴로 안전하게 되돌아올 수 있습니다.
     * **대칭형 검증 적용**: 전환 계획서가 생성된 후에는 1단계와 대칭되는 **3단계 검증 파이프라인(L1 린터 -> L2 AI 리뷰 -> L3 사용자 피드백 반영 및 컨펌)**을 수행하며, 최종 승인 시에만 파일로 저장됩니다.
   * **`3. 종료 (Exit)`**: 도구를 완전히 종료합니다.

### 2. 배치 모드 및 CLI 자동화 실행 (Batch Mode)
명령줄 아규먼트(`--conn`, `--all`, `--sp`) 또는 환경 변수(`SP_ANALYZER_CONN_STR`)를 통해 로그인 및 TUI 메뉴 단계를 완전히 건너뛰고 무인 대량 일괄 처리가 가능합니다.

- **명령줄 옵션**:
  - `--conn <연결문자열>`: 분석용 데이터베이스 연결 문자열을 직접 지정합니다. (생략 시 `SP_ANALYZER_CONN_STR` 환경 변수 값을 조회합니다.)
  - `--all`: 데이터베이스 내의 모든 Stored Procedure를 일괄 분석합니다.
  - `--sp <SP이름1,SP이름2,...>`: 특정 Stored Procedure들만 지정하여 분석합니다. 쉼표(`,`)로 구분하며 스키마명을 포함(`dbo.USP_1`)하거나 생략(`USP_1`)할 수 있습니다.
  
- **배치 실행 예시**:
  - **특정 SP 지정 분석**:
    ```bash
    dotnet run --project src/SpAnalyzer.Cli -- --conn "Server=localhost;Database=my_db;User ID=sa;Password=my_password;TrustServerCertificate=true" --sp dbo.USP_GetUsers,dbo.USP_UpdateOrder
    ```
  - **전체 SP 일괄 분석**:
    ```bash
    dotnet run --project src/SpAnalyzer.Cli -- --conn "Server=localhost;Database=my_db;User ID=sa;Password=my_password;TrustServerCertificate=true" --all
    ```
  - **환경 변수를 활용한 분석**:
    ```bash
    export SP_ANALYZER_CONN_STR="Server=localhost;Database=my_db;User ID=sa;Password=my_password;TrustServerCertificate=true"
    dotnet run --project src/SpAnalyzer.Cli -- --all
    ```

> [!NOTE]
> 배치 모드로 대량 실행 중 특정 SP에 대한 메타데이터 조회 실패 또는 AI 통신 에러가 발생하더라도, 해당 SP만 에러 로그가 출력되고 스킵(try-catch 격리)되며 전체 배치 작업은 중단 없이 다음 SP 분석을 계속 수행합니다.

### 3. 코드 일치성 검증 및 데이터 정합성 검증 (SpAnalyzer.Validator)

역공학 마이그레이션이 끝난 뒤, 생성된 명세서와 실제 마이그레이션 소스코드가 동일하게 구현되었는지 검증하고, 레거시 DB와 실제 실행 결과 정합성을 대조할 때 실행합니다.

*   **대화형 TUI 모드 실행**:
    ```bash
    dotnet run --project src/SpAnalyzer.Validator.Cli
    ```
    *   **1. 설계서 vs 마이그레이션 소스코드 일치성 검증 (L1/L2/L3)**: C#/Java 소스코드 정적 분석 및 AI 의미론적 Gap 분석, 인간 피드백 루프를 가동하여 검증합니다.
    *   **2. 데이터 정합성 검증용 테스트 파라미터 설계 (AI)**: 설계서(`*_Spec.md`)를 분석해 AI가 정상/경계값/오류 시나리오 테스트 파라미터 JSON(`*_test_inputs.json`)을 생성합니다.
    *   **3. 원본 Stored Procedure 실행 데이터 수집 (Legacy DB)**: 생성된 테스트 입력값 JSON을 기반으로 실제 Legacy DB에 접근해 SP를 호출하고, 다중 ResultSet 데이터를 JSON(`*_legacy_results.json`)으로 덤프 수집합니다.
    *   **4. 실행 결과 데이터 정합성 1:1 대조 및 보고서 생성 (Compare)**: 수집된 레거시 결과와 신규 타겟 결과(`*_target_results.json`)를 상세 1:1 비교 대조하여 데이터 정합성 분석 보고서(`*_CompareReport.md`)를 작성합니다.
    *   **기타 기능**: 디렉토리 경로 입력 창에서 `Tab` 키로 로컬 폴더 자동완성이 가능하며, 부재 경로 입력 시 동적 복구 프롬프트를 띄웁니다.

*   **배치 검증 자동화 모드 실행 (CI/CD 결합용 무인 모드)**:
    ```bash
    # 소스코드 일치성 자동 검증 (L3 인간 개입 생략)
    dotnet run --project src/SpAnalyzer.Validator.Cli -- --spec "./output" --code "./src/Migration" --batch

    # 데이터 정합성 테스트 파라미터 설계 배치 모드
    dotnet run --project src/SpAnalyzer.Validator.Cli -- --spec "./output" --gen-inputs --batch

    # 레거시 DB 실행 결과 덤프 배치 모드
    dotnet run --project src/SpAnalyzer.Validator.Cli -- --exec-legacy --conn "Server=localhost;Database=Northwind;User ID=sa;Password=your_password;TrustServerCertificate=true" --batch

    # 레거시 vs 타겟 1:1 데이터 정합성 대조 배치 모드
    dotnet run --project src/SpAnalyzer.Validator.Cli -- --compare-data --batch
    ```
    *   `--spec`: 마이그레이션 설계서 및 결과 파일들이 저장될 폴더를 지정합니다.
    *   `--code`: 검증할 소스 코드가 저장된 폴더를 지정합니다.
    *   `--gen-inputs`: 테스트 케이스 입력 데이터 설계 작업을 지시합니다.
    *   `--exec-legacy`: Legacy DB를 대상으로 데이터 수집 처리를 지시합니다 (`--conn` 연결 문자열 동시 제공 필요).
    *   `--compare-data`: 수집된 레거시 결과와 신규 타겟 결과를 대조 분석하여 보고서를 작성합니다.
    *   `--batch`: 인간 개입(L3) 확인을 생략하고 자동 검증 및 결과 Export만 즉시 수행하고 성공 종료합니다.

---

## 🧪 단위 테스트 실행 (Running Tests)

단위 테스트를 실행하여 모든 코드가 무결하게 작동하는지 검증합니다.
```bash
dotnet test
```
