# SP Analyzer 시스템 아키텍처 정의서 (System Architecture Definition)

본 문서는 SQL Server Stored Procedure(SP)를 자율적으로 분석하고 신규 시스템으로의 전환 설계서를 도출하는 **SP Analyzer 에이전트** 프로그램의 모듈 설계, 구성 요소 간의 데이터 흐름, 핵심 알고리즘 및 검증 파이프라인의 구조적 아키텍처를 정의합니다.

---

## 🏗️ 컴포넌트 및 모듈 아키텍처 (Component Architecture)

본 프로그램은 관심사 분리(SoC) 원칙에 따라 사용자 인터페이스 레이어(Cli)와 핵심 도메인 비즈니스 레이어(Core)로 명확히 분리되어 설계되었습니다.

| 컴포넌트 (프로젝트) | 모듈 (클래스/인터페이스) | 주요 아키텍처적 역할 및 기능 |
| :--- | :--- | :--- |
| **SpAnalyzer.Cli**<br/>(TUI/CLI 레이어) | [Program](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Cli/Program.cs) | CLI 아규먼트 파싱, DI(의존성 주입) 구성, 대화형 및 배치 실행 모드 제어, CancellationTokenSource 취소 처리 연동 |
| | [ConsoleUserInteraction](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Cli/ConsoleUserInteraction.cs) | Spectre.Console 기반 TUI 렌더링, L3 인간 개입형 검토 UI 제공, Warnings 경고 패널 렌더링 및 Markup.Escape 예외 방지 |
| | [SessionManager](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Cli/SessionManager.cs) | 직전 로그인 정보 로컬 세션 파일 기억 관리 |
| **SpAnalyzer.Core**<br/>(핵심 비즈니스 레이어) | [DbMetadataService](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Core/Services/DbMetadataService.cs) | 시스템 메타데이터 쿼리, DFS 기반 재귀적 의존성 탐색, 확장 속성 주석 수집, CancellationToken 기반 비동기 취소 지원 및 Warnings 수집 |
| | [AiService](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Core/Services/AiService.cs) | LLM 프롬프트 조립(동적 SQL/Linked Server 가이드라인 포함), 명세서 생성, AI 리뷰(L2), 배치 현대화 설계서 기안, robust한 JSON 추출(`ExtractJson`) |
| | [MechanicalValidator](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Core/Services/MechanicalValidator.cs) | Markdig AST 기반 마크다운 필수 구조 분석(IsConsolidated 분기 검증) 및 mermaid-cli 연동을 통한 다이어그램 문법 실시간 컴파일 검증 |
| | [MetadataExporter](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Core/Services/MetadataExporter.cs) | JSON 덤프, 프롬프트 로그, 개별 개체 파일 트리 내보내기(Export) 제어 |
| | [VerificationPipelineOrchestrator](file:///home/moondae/git-root/sp-reverse-engineering/src/SpAnalyzer.Core/Services/VerificationPipelineOrchestrator.cs) | CancellationToken을 전파하는 L1/L2 자동화 자가 수정 루프 및 L3 인간 개입 워크플로우 오케스트레이션 |

---

## ⚙️ 핵심 아키텍처 메커니즘 (Core Mechanisms)

### 1. 재귀적 의존성 수집 및 예외 격리 (DFS & Soft Fail & Warnings)
* **재귀적 탐색 알고리즘**: 타겟 SP가 참조하는 테이블, UDF, 하위 SP의 의존성을 `sys.sql_expression_dependencies`를 활용하여 **깊이 우선 탐색(DFS)** 방식으로 추적합니다.
* **순환 참조 방지**: 탐색 중인 객체의 전체 이름을 담는 `HashSet<string> (visited)`을 관리하여 무한 루프 및 중복 DB 조회를 원천 차단합니다.
* **소프트 페일 및 경고 누적(Soft Fail & Warnings)**: 특정 의존 테이블의 스키마나 UDF DDL 조회 중 권한 누락이나 존재하지 않는 객체 참조 등의 비치명적 오류가 발생하면, 파이프라인 전체를 중단시키는 대신 해당 내역을 `SpDefinition.Warnings` 리스트에 누적합니다. 이 경고 데이터는 TUI 화면에 명확한 경고 패널(Panel)로 렌더링될 뿐만 아니라 AI 분석 프롬프트에도 포함되어 AI가 누락된 리소스를 감안하여 현실적인 분석 명세서를 쓰도록 돕습니다.

### 2. 비즈니스 뉘앙스 확보를 위한 확장 속성(Extended Properties) 맵핑
* 기술적 메타데이터(컬럼명, 데이터 타입) 수집 단계를 넘어, 데이터베이스의 확장 속성인 **`MS_Description`**에 등록된 테이블 요약과 컬럼별 한글 주석을 실시간 연동합니다.
* 이를 통해 AI 엔진이 `STAT_CD = 'A01'`과 같은 데이터 조작을 분석할 때, 실제 도메인 의미인 `상태코드 (A01: 대기)`를 정확히 인지하여 환각(Hallucination) 현상을 원천 방어합니다.

### 3. 3단계 신뢰성 검증 파이프라인 (Verification Pipeline)
* **대칭형 검증 아키텍처**: 개별 SP 분석서(`_Spec.md`)와 통합 배치 전환 계획서(`_BatchMigrationPlan.md`) 모두에 100% 대칭형 검증 파이프라인이 구동됩니다.
* **L1 (기계 검사 - 정적 Linter)**: 마크다운 구조(AST) 파서인 **`Markdig`**을 사용해 필수 섹션 누락 여부를 구조적으로 정교히 검증하며, 설정에 따라 **`mermaid-cli` 도구 컴파일 검증** 또는 정적 괄호 마스킹 예외 린팅을 선택적으로 가동합니다. L1 검증 실패 시 생성되는 교정 피드백 템플릿(`SuggestedPromptFix`)에서 개별 명세서와 통합 계획서의 요구 명세가 혼선되지 않도록 `IsConsolidated` 모델 분기 플래그를 도입하여 각 문헌 종류에 일치하는 맞춤형 헤더 린팅 가이드를 AI에게 명확히 피드백합니다.
* **L2 (AI 교차 리뷰er)**: 수석 아키텍트 프롬프트로 리뷰어 에이전트를 가동하여 원천 정보와 생성 설계서 간의 불일치를 스크리닝하고 누락 발견 시 설정된 시도 횟수(기본 1회, 또는 검증 완료시까지 무제한)만큼 자가 보완(`Self-Correction`)을 수행합니다.
* **L3 (인간 개입 조율 - HITL)**: TUI 모드에서 렌더링된 결과를 개발자가 직접 프리뷰하고 승인(`Approve`)하거나, 보완 피드백(`Feedback`)을 자연어로 주어 재생성하는 인터랙티브 조율을 지원합니다.

### 4. 현대화 배치 스케줄러 전환 설계 및 아키텍처 분리
* 1차 마이그레이션 타겟인 **SQL Server Agent 배치 작업**의 특성을 극대화하기 위해 **개별 분석과 통합 마이그레이션 계획 프로세스를 이원화**하여 설계했습니다.
  - **1단계: 개별 SP 정적 분석 (`_Spec.md`)**: SP 개별 단위의 로직 설명, 메타데이터 컬럼 맵, 의존 객체 분석에만 집중하여 AI 프롬프트 비용을 낮추고 명세서 문서를 개별 축적합니다.
  - **2단계: 다중 명세서 통합 배치 설계 (`_BatchMigrationPlan.md`)**: 사용자가 TUI/CLI 상에서 수동으로 선택한 복수의 기존 분석 명세서(`_Spec.md`) 파일들을 로드/조합하여, 이를 하나의 유기적인 배치 Job 아키텍처(예: .NET Worker Service 또는 Spring Batch의 Job & Step 구조)로 전환하는 통합 현대화 설계서를 자동 도출합니다.
  - 이를 통해 멀티 스텝 배치 워크플로우 제어(Restartability), 청크(Chunk) 페이징 의사코드, 단계별 예외 처리 및 알림 통합, 그리고 통합 정합성 검증 SQL 세트를 도출합니다.
  - **동적 SQL 및 Linked Server 포팅 설계**: DB 정적 의존성 분석(`sys.sql_expression_dependencies`)이 포착하지 못하는 런타임 동적 SQL문(`EXEC(@sql)`)이나 Linked Server 참조(4파트 식별자)가 사용된 지점을 감지하여 마이그레이션 설계서에 반영합니다. 컴파일 타임에 검증 가능하며 인젝션 위협이 없는 안전한 타겟 언어 파라미터화 쿼리 구조와 멀티 데이터소스 구성/API 대체 등 애플리케이션 배치 환경에 적합한 연동 대안을 제시하도록 지침을 설계했습니다.

### 5. 안전한 AI 응답 처리 및 비동기 작업 취소 메커니즘 (Robustness & Cancel-safety)
* **안전한 JSON 추출 알고리즘 (`ExtractJson`)**: AI가 교차 리뷰 등에서 JSON 결과를 반환할 때, 마크다운 코드 블록(```json)으로 감싸거나 외부에 설명 텍스트를 함께 제공하면 기존 Json 파서는 에러가 발생합니다. 이를 위해 문자열 전체를 분석해 마크다운 블록을 무시하고, 가장 바깥쪽의 `{`와 `}` 한 쌍을 인덱스로 추적해 순수 JSON만 정밀 추출하는 견고한 문자열 전처리 엔진을 `AiService`에 탑재했습니다.
* **비동기 파이프라인의 CancellationToken 전파**: 대량의 SP 데이터 수집이나 원격 AI 응답 대기가 장시간 블로킹되거나 무한 대기가 발생하는 것을 방지합니다. `Program`의 메인 컨트롤러부터 `VerificationPipelineOrchestrator`, `AiService`, `DbMetadataService` 등 모든 비동기 호출 경로로 `CancellationToken`을 전파하였고, CLI 환경에서 `Ctrl+C` 입력 감지 시 `CancellationTokenSource`를 즉시 취소하여 안전하게 예외를 격리하고 메인 루프를 복구합니다.


---

## 📊 프로그램 실행 흐름 (Visual Execution Flow)

아래 다이어그램은 SP Analyzer 프로그램이 기동되어 설정 파싱, 데이터베이스 메타데이터 재귀 수집, AI 분석 및 결과 저장/이탈까지의 거시적인(Macro) 전체 실행 흐름을 시각적으로 나타냅니다.

```mermaid
graph TD
    %% 1단계: 초기화 및 연결
    subgraph Setup ["1. 초기 설정 및 DB 연결 (Setup)"]
        Start["시작 (CLI 실행)"] --> Parse["설정 로드 및 CLI 인자 파싱 (CliArgs)"]
        Parse --> ModeCheck{"배치 모드 여부?"}
        
        ModeCheck -- "아니오 (TUI)" --> TUI["대화형 로그인 입력 (계정 기억 세션 연동)"]
        ModeCheck -- "예 (Batch)" --> Batch["연결 문자열 추출 (인자/환경변수)"]
        
        TUI & Batch --> ConnTest["데이터베이스 연결성 검증"]
        ConnTest --> LoadSps["전체 Stored Procedure 목록 로드"]
    end
    
    %% 2단계: 대상 필터링
    subgraph Selection ["2. 분석 대상 필터링 (Selection)"]
        LoadSps --> TargetCheck{"배치 모드 여부?"}
        
        TargetCheck -- "아니오" --> SelectTUI["실시간 자동완성 검색으로 타겟 SP 선택"]
        TargetCheck -- "예" --> SelectBatch["--all 또는 --sp 기준으로 분석 대상 목록 필터링"]
    end
    
    %% 3단계: 메인 분석 및 검증 파이프라인
    subgraph Pipeline ["3. 분석 및 검증 파이프라인 (Pipeline)"]
        SelectTUI & SelectBatch --> LoopStart["분석 루프 시작 (SP 개별 단위 예외 격리)"]
        
        LoopStart --> QueryMeta["재귀적 의존성 분석 (DbMetadataService)<br/>- 테이블/컬럼 상세 스키마 & 한글 주석 수집<br/>- 참조 UDF/SP SQL 원본 추출"]
        QueryMeta --> GeneratePrompt["AI 프롬프트 컨텍스트 조립 (System 규칙 + 사용자 지침)"]
        
        GeneratePrompt --> VerificationPipeline["3단계 검증 파이프라인 실행<br/>(상세 흐름은 하단 다이어그램 참고)"]
    end
    
    %% 4단계: 산출물 내보내기 및 현대화 전환 설계
    subgraph Save ["4. 결과 저장 및 현대화 설계 (Export)"]
        VerificationPipeline -- "승인 및 완료" --> ExportRaw["원천 데이터 다중 포맷 덤프<br/>(JSON, TXT, 개별 파일 트리)"]
        ExportRaw --> SaveSpec["최종 Markdown 명세서 파일 저장<br/>([Schema].[SP이름]_Spec.md)"]
        SaveSpec --> GenMigrationCheck{"현대화 전환 계획 생성 활성화?"}
        GenMigrationCheck -- "예" --> GenMigration["배치 전환 계획 설계서 작성<br/>([Schema].[SP이름]_BatchMigrationPlan.md)"]
        GenMigrationCheck -- "아니오" --> CheckNext
        GenMigration --> CheckNext
    end
    
    CheckNext -- "예" --> LoopStart
    CheckNext -- "아니오" --> End["종료"]
```

---

## 🔍 3단계 검증 파이프라인 상세 (Verification Pipeline Details)

AI가 생성한 1차 명세서의 신뢰성과 무결성을 검증하고, 오류 발견 시 자가 수정(`Self-Correction`) 및 사용자 피드백을 적용하는 상세 검증(Micro) 흐름도입니다.

```mermaid
graph TD
    StartPipeline["파이프라인 시작<br/>(SpDefinition + Instructions)"] --> InitAttempt["시도 횟수 초기화 (attempt = 1)"]
    InitAttempt --> CallAI["AI 리버스 엔지니어링 요청<br/>(GenerateSpecificationAsync)"]
    
    CallAI --> L1Check{"L1: 기계적 무결성 검증<br/>(Markdig AST 구조 확인 & mmdc 컴파일)?"}
    
    L1Check -- "실패" --> L1FailAttempt{"attempt < maxAttempts?"}
    L1FailAttempt -- "예" --> SetL1Feedback["L1 피드백 세팅 및 시도 횟수 증가"] --> CallAI
    L1FailAttempt -- "아니오" --> L1Abort["L1 검증 최종 실패 알림"] --> L3Check
    
    L1Check -- "성공" --> L2Review["AI 교차 리뷰 분석 요청"]
    L2Review --> L2Check{"L2: AI 리뷰 통과<br/>(결함/누락 없음)?"}
    
    L2Check -- "실패" --> L2FailAttempt{"attempt < maxAttempts?"}
    L2FailAttempt -- "예" --> SetL2Feedback["L2 피드백 세팅 및 시도 횟수 증가"] --> CallAI
    L2FailAttempt -- "아니오" --> L2Abort["L2 검증 최종 실패 알림"] --> L3Check
    
    L2Check -- "성공" --> L3Check{"배치 모드인가?"}
    L1Abort --> L3Check
    L2Abort --> L3Check
    
    L3Check -- "예 (Batch)" --> ReturnSuccess["결과 반환 및 저장 단계로 진행"]
    
    L3Check -- "아니오 (TUI)" --> HumanReview["L3: 사용자 검토 요청<br/>(미리보기 화면 렌더링)"]
    HumanReview --> HumanDecision{"사용자 결정?"}
    
    HumanDecision -- "1. 승인 (Approve)" --> ReturnSuccess
    HumanDecision -- "3. 취소 (Cancel)" --> ReturnCancel["저장 없이 이탈 (분석 건너뛰기)"]
    HumanDecision -- "2. 피드백 (Feedback)" --> RegenerateAI["피드백 반영 AI 재생성 요청"]
    
    RegenerateAI --> L1ReCheck{"L1 정적 검사 통과?"}
    L1ReCheck -- "실패" --> SetL1ReFeedback["L1 피드백 반영 자가 수정 (1회)"] --> HumanReview
    L1ReCheck -- "성공" --> HumanReview
```

---

## 📈 활용 분야 및 기대 효과

* **레거시 시스템 마이그레이션**: 오랜 기간 정비되지 않은 대규모 레거시 Stored Procedure의 비즈니스 로직을 빠르게 문서화하고 도식화함과 동시에 현대화 마이그레이션 계획을 상세히 수립합니다.
* **신입 개발자 온보딩**: 복잡한 데이터베이스 의존 관계를 AI 에이전트가 탐색하여 다이어그램과 함께 구조적으로 해설하고 포팅 가이드라인을 제공하므로 개발 지식 전파 비용을 대폭 낮춥니다.
* **CI/CD 파이프라인 자동화**: 주기적으로 배치 모드를 실행하여 데이터베이스 스키마와 프로시저 변경 이력을 명세서로 자동 추적하고 변경 감지 리포트를 산출할 수 있습니다.
