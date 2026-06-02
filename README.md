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
- **다양한 AI 공급자 지원**: OpenAI(GPT) 및 로컬 환경에서 실행되는 Ollama(Llama 3 등)를 유연하게 연결 및 전환하여 사용할 수 있습니다.
- **커스터마이징 가능한 지침**: 외부 `instructions.txt` 파일의 내용을 프롬프트에 자동으로 주입하여, 원하는 명세서 형식 및 분석 규칙을 텍스트 에디터로 간편하게 커스텀할 수 있습니다.
- **인터랙티브 TUI 제공**: 
  - **보안 로그인**: 직전 성공 계정을 로컬 세션 파일(`.session.json`)에 기억하여 재입력을 최소화하며, 비밀번호는 입력 시 화면에 마스킹(`Secret()`)되어 안전합니다.
  - **자동완성 검색**: 타이핑 시 실시간으로 SP 목록이 필터링되는 자동완성 검색 기능이 탑재되어 있습니다.
- **안정적인 예외 처리**: DB 연결 불가, AI 호출 오류 등 장애 상황 발생 시 화면에 상세 에러를 렌더링하고 사용자 입력 프롬프트로 부드럽게 복귀합니다.
- **테스트 주도 개발(TDD) 기반**: 솔루션 코드가 6개의 xUnit 단위 테스트 코드를 통해 견고하게 검증되어 있습니다.

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
│   │   └── Services/               # DB 메타데이터 조회 및 AI API 서비스 구현
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
프로그램 실행 전 `src/SpAnalyzer.Cli/appsettings.json` 파일을 열어 사용할 서버와 AI API 설정을 지정합니다.

```json
{
  "DatabaseSettings": {
    "Server": "localhost",          // SQL Server 주소
    "Database": "master",           // 대상 데이터베이스 이름
    "MaxDependencyDepth": 3         // [설정] 재귀적 의존성 탐색의 최대 깊이 (기본값: 3)
  },
  "AiSettings": {
    "Provider": "OpenAI",          // "OpenAI" | "Claude" | "Gemini" | "Ollama" 중 선택
    "ModelName": "gpt-4o",         // 사용할 LLM 모델명
    "ApiKey": "YOUR_API_KEY",      // Ollama 사용 시 빈 문자열 가능
    "Endpoint": "",                // API 엔드포인트 주소 (Ollama의 경우 http://localhost:11434/v1 등)
    "Temperature": 0.2             // 분석의 일관성을 위해 낮게(0.0 ~ 0.3) 설정을 권장합니다.
  },
  "OutputSettings": {
    "Directory": "./output",       // 명세서 파일이 저장될 출력 디렉터리
    "InstructionsFile": "instructions.txt" // 분석 규칙 지침 파일 명칭
  }
}
```

### 2. `instructions.txt` 설정
분석된 결과물의 마크다운 포맷 규칙을 정의하는 가이드라인 파일입니다. `src/SpAnalyzer.Cli/instructions.txt`에 작성된 텍스트 내용대로 AI가 리버스 엔지니어링 문서를 만듭니다.

---

## 🏃 실행 및 사용 방법 (Running the Tool)

### 프로그램 실행
1. 프로젝트 루트 경로로 이동합니다.
2. 아래 명령어를 터미널에 입력하여 프로그램을 기동합니다.
   ```bash
   dotnet run --project src/SpAnalyzer.Cli
   ```
3. DB 계정(ID)과 패스워드를 입력하여 SQL Server에 로그인합니다.
4. 분석하고자 하는 Stored Procedure의 이름을 검색 또는 방향키로 선택합니다.
5. 로딩이 완료되면 지정된 출력 디렉터리(기본 `./output`) 내부에 `[스키마].[SP이름]_Spec.md` 형식의 분석 명세서 파일이 생성됩니다.

---

## 🧪 단위 테스트 실행 (Running Tests)

단위 테스트를 실행하여 모든 코드가 무결하게 작동하는지 검증합니다.
```bash
dotnet test
```
