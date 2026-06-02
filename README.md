# SQL Server Stored Procedure Reverse Engineering Tool (SP Analyzer)

본 프로젝트는 **SQL Server 2022**에 저장된 Stored Procedure(SP)를 분석하여, AI(OpenAI, Ollama 등)를 통해 사용자 정의 지침에 맞춘 마크다운 형식의 기능 명세서를 자동 생성하는 개발자용 터미널 기반 CLI(TUI) 도구입니다.

---

## 🚀 주요 특징 (Key Features)

- **재귀적 하이브리드 의존성 분석**: 
  - 타겟 SP가 참조하는 테이블, 뷰, 사용자 정의 함수(UDF), 다른 SP를 깊이(Depth) 설정에 따라 **재귀적(DFS)**으로 추적합니다.
  - 중복 탐색 방지를 위한 방문 노드 제어(`HashSet`) 및 스키마 분석 권한 누락 시의 안전한 스킵(Soft Fail) 정책이 내장되어 있습니다.
- **상세 테이블 스키마 자동 추출**:
  - 분석 대상 테이블들의 컬럼명, 데이터 타입(길이/정밀도 포함), Null 허용 여부를 수집합니다.
  - 시스템 메타데이터 뷰를 조회하여 **Primary Key(PK)** 및 **Foreign Key(FK)** 여부를 판별하고, 이를 깔끔한 **Markdown Table** 형식으로 가공하여 AI 프롬프트에 자동으로 주입합니다.
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
  - **Level 1 (기계적 정적 검증)**: 필수 마크다운 포맷 헤더가 누락되었거나 Mermaid 문법 오류 유발 여부를 정적으로 고속 검증(린팅)합니다.
  - **Level 2 (AI 교차 검증 및 자가 보완)**: 1차 작성이 끝나면 검토관(Reviewer) 에이전트가 비즈니스 로직의 결함과 왜곡을 검증해 N=1회 자가 수정(`Self-Correction`) 보완 루프를 실행합니다.
  - **Level 3 (인간 개입 피드백 루프)**: TUI 대화형 화면에서 개발자가 생성된 기능 명세서의 전체 내용을 실시간 미리보기(Preview)로 확인한 후 최종 승인(`Approve`)하거나, 직접 자연어 보완 의견(`Feedback`)을 기재하여 즉시 재생성할 수 있습니다. 피드백 반영 후 다시 완성된 명세서 역시 한 번 더 화면에 렌더링되며 만족할 때까지 다회차에 걸쳐 반복 검증할 수 있는 직관적인 인터랙티브 흐름을 제공합니다. (무인 배치 모드에서는 자동으로 생략)
- **다양한 AI 공급자 지원**: OpenAI(GPT) 및 로컬 환경에서 실행되는 Ollama(Llama 3 등)를 유연하게 연결 및 전환하여 사용할 수 있습니다.
- **커스터마이징 가능한 지침**: 외부 `instructions.txt` 파일의 내용을 프롬프트에 자동으로 주입하여, 원하는 명세서 형식 및 분석 규칙을 텍스트 에디터로 간편하게 커스텀할 수 있습니다.
- **인터랙티브 TUI 제공**: 
  - **보안 로그인**: 직전 성공 계정을 로컬 세션 파일(`.session.json`)에 기억하여 재입력을 최소화하며, 비밀번호는 입력 시 화면에 마스킹(`Secret()`)되어 안전합니다.
  - **자동완성 검색**: 타이핑 시 실시간으로 SP 목록이 필터링되는 자동완성 검색 기능이 탑재되어 있습니다.
  - **안정적인 텍스트 렌더링**: 분석 진행률 표시 및 자가 검증 결과 출력 과정에서 대괄호(`[...]`) 등의 텍스트가 Spectre.Console의 마크업 태그로 오인되어 런타임 예외(`System.InvalidOperationException`)를 발생시키는 문제를 방지하기 위해, 모든 사용자 인터랙션 렌더링에 이스케이프(`Markup.Escape`) 처리를 적용하여 안전하고 견고한 UI 환경을 보장합니다.
- **테스트 주도 개발(TDD) 기반**: 솔루션 코드가 7개의 xUnit 단위 테스트 코드를 통해 견고하게 검증되어 있습니다.

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
│   └── SpAnalyzer.Cli/             # [콘솔 애플리케이션] Spectre.Console 기반 TUI
│       ├── Program.cs              # CLI 진입점 및 대화형 워크플로우 제어
│       ├── appsettings.json        # DB/AI 연동 설정 파일
│       └── instructions.txt        # AI 분석 세부 지침 규칙 파일
│
└── tests/
    └── SpAnalyzer.Core.Tests/      # [단위 테스트 프로젝트] xUnit 기반 단위 테스트
```

---

## ⚙ 설정 방법 (Configuration)

### 1. `appsettings.json` 설정
프로그램 실행 전 `src/SpAnalyzer.Cli/appsettings.json` 파일을 열어 기본적인 데이터베이스 환경 및 출력 설정을 지정합니다. 자격 증명 누출 방지를 위해 이 파일의 `ApiKey`는 비워두는 것을 권장합니다.

```json
{
  "DatabaseSettings": {
    "Server": "localhost",          // SQL Server 주소
    "Database": "master",           // 대상 데이터베이스 이름
    "MaxDependencyDepth": 3         // 재귀적 의존성 탐색의 최대 깊이 (기본값: 3)
  },
  "AiSettings": {
    "Provider": "OpenAI",          // "OpenAI" | "Claude" | "Gemini" | "Ollama" 중 선택
    "ModelName": "gpt-4o",         // 사용할 LLM 모델명
    "ApiKey": "",                  // 보안을 위해 공용 설정에서는 비워둡니다 (로컬 설정 권장)
    "Endpoint": "https://api.openai.com/v1", // API 엔드포인트 주소 (Ollama의 경우 http://localhost:11434/v1 등)
    "Temperature": 0.2             // 분석의 일관성을 위해 낮게(0.0 ~ 0.3) 설정을 권장합니다.
  },
  "OutputSettings": {
    "Directory": "./output",       // 명세서 파일이 저장될 출력 디렉터리
    "InstructionsFile": "instructions.txt", // 분석 규칙 지침 파일 명칭
    "SaveRawJson": true,           // [설정] SpDefinition JSON 파일 저장 여부
    "SaveRawContext": true,        // [설정] 조립된 프롬프트 텍스트 원문 저장 여부
    "SaveRawFiles": true           // [설정] 의존성 개별 객체 파일/폴더 분산 덤프 여부
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
       "ApiKey": "여기에_새로_발급받은_API키_입력"
     }
   }
   ```

### 3. `instructions.txt` 설정
분석된 결과물의 마크다운 포맷 규칙을 정의하는 가이드라인 파일입니다. `src/SpAnalyzer.Cli/instructions.txt`에 작성된 텍스트 내용대로 AI가 리버스 엔지니어링 문서를 만듭니다.

---

## 🏃 실행 및 사용 방법 (Running the Tool)
 
### 1. 대화형 TUI 모드 실행
기본적으로 아무 아규먼트 없이 실행하면 로그인 정보 입력 및 Stored Procedure 자동완성 검색/선택이 가능한 TUI 모드로 시작합니다.
```bash
dotnet run --project src/SpAnalyzer.Cli
```
1. DB 계정(ID)과 패스워드를 입력하여 SQL Server에 로그인합니다.
2. 분석하고자 하는 Stored Procedure의 이름을 검색 또는 방향키로 선택합니다.
3. 로딩이 완료되면 지정된 출력 디렉터리(기본 `./output`) 내부에 `[스키마].[SP이름]_Spec.md` 형식의 분석 명세서 파일이 생성됩니다.

### 2. 배치 모드 및 CLI 자동화 실행 (Batch Mode)
명령줄 아규먼트(`--conn`, `--all`, `--sp`) 또는 환경 변수(`SP_ANALYZER_CONN_STR`)를 통해 로그인 및 TUI 선택 단계를 완전히 건너뛰고 무인 대량 일괄 처리가 가능합니다.

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

---

## 🧪 단위 테스트 실행 (Running Tests)

단위 테스트를 실행하여 모든 코드가 무결하게 작동하는지 검증합니다.
```bash
dotnet test
```
