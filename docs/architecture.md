# ReSet (**RE**verse engineering **SET**tlement) 시스템 아키텍처 정의서 (System Architecture Definition)

본 문서는 SQL Server Stored Procedure(SP)를 자율적으로 분석하고 신규 시스템으로의 전환 설계서를 도출하는 **ReSet (REverse engineering SETtlement) 에이전트** 프로그램의 모듈 설계, 구성 요소 간의 데이터 흐름, 핵심 알고리즘 및 검증 파이프라인의 구조적 아키텍처를 정의합니다.

---

## 🏗️ 컴포넌트 및 모듈 아키텍처 (Component Architecture)

본 프로그램은 관심사 분리(SoC) 원칙에 따라 사용자 인터페이스 레이어(Cli)와 핵심 도메인 비즈니스 레이어(Core)로 명확히 분리되어 설계되었습니다.

| 컴포넌트 (프로젝트) | 모듈 (클래스/인터페이스) | 주요 아키텍처적 역할 및 기능 |
| :--- | :--- | :--- |
| **ReSet.Cli**<br/>(TUI/CLI 레이어) | [Program](file:///home/moondae/git-root/ReSet/src/ReSet.Cli/Program.cs) | CLI 아규먼트 파싱, DI 구성, 대화형 및 배치 실행 모드 제어, Multi-SP 물리 선택 순서 보장 순차 선택 루프 흐름 및 CancellationTokenSource 연동 |
| | [ConsoleUserInteraction](file:///home/moondae/git-root/ReSet/src/ReSet.Cli/ConsoleUserInteraction.cs) | Spectre.Console 기반 TUI 렌더링, L3 인간 개입형 검토 UI 제공, Warnings 경고 패널 렌더링 및 Markup.Escape 예외 방지 |
| | [SessionManager](file:///home/moondae/git-root/ReSet/src/ReSet.Cli/SessionManager.cs) | 직전 로그인 정보 로컬 세션 파일 기억 관리 및 즉각적인 연결 정보(서버, DB명) 수정 기능 제공 |
| | [CodingEngineFactory](file:///home/moondae/git-root/ReSet/src/ReSet.Cli/CodingEngineFactory.cs) | 설정 파일에 기반해 다형적 외부 코딩 에이전트(`ICodingEngine`)를 구성하고 생성하는 팩토리 클래스 |
| **ReSet.Core**<br/>(핵심 비즈니스 레이어) | [DbMetadataService](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/DbMetadataService.cs) | 시스템 메타데이터 쿼리, DFS 기반 재귀적 의존성 탐색, 확장 속성 주석 수집, CancellationToken 기반 비동기 취소 지원 및 Warnings 수집 |
| | [AiService](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/AiService.cs) | LLM 프롬프트 조립(동적 SQL/Linked Server 가이드라인 포함) 및 결과 분석 오케스트레이션. 주입받은 `IAiClient`를 사용해 AI API 호출 수행, robust한 JSON 추출(`ExtractJson`) |
| | [IAiClient](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/IAiClient.cs) | AI 모델 간의 공통 텍스트 통신 계약 정의 |
| | [Clients (OpenAi, Claude, Google, Ollama)](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/Clients/) | 각 프로바이더(OpenAI, Claude, Google, Ollama)의 네이티브 REST 규격에 맞춰 제작된 HttpClient 통신 모듈 |
| | [AiClientFactory](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/Clients/AiClientFactory.cs) | 제공자 문자열에 맞춰 적절한 `IAiClient` 구현체를 생성하여 반환하는 팩토리 클래스 |
| | [MechanicalValidator](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/MechanicalValidator.cs) | Markdig AST 기반 마크다운 필수 구조 분석(IsConsolidated 분기 검증) 및 mermaid-cli 연동을 통한 다이어그램 문법 실시간 컴파일 검증 |
| | [MetadataExporter](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/MetadataExporter.cs) | JSON 덤프, 프롬프트 로그, 개별 개체 파일 트리 내보내기(Export) 및 외부 코딩 에이전트용 가이드라인 번들(`*_MigrationInstructions.md`) 생성 제어 |
| | [VerificationPipelineOrchestrator](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/VerificationPipelineOrchestrator.cs) | CancellationToken을 전파하는 L1/L2 자동화 자가 수정 루프 및 L3 인간 개입 워크플로우 오케스트레이션 |
| | [CacheManager](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/CacheManager.cs) | SHA-256 해시 기반 로컬 증분 분석 캐싱 및 색인(`.sp_cache_index.json`) 보존/조회 관리 |
| | [ICodingEngine](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/ICodingEngine.cs) | 외부 코딩 에이전트 연동용 마이그레이션 생성기 추상 인터페이스 |
| | [ExternalCliCodingEngine](file:///home/moondae/git-root/ReSet/src/ReSet.Core/Services/ExternalCliCodingEngine.cs) | CLI 기반 외부 에이전트 프로세스(Claude, agy, codex 등) 기동 및 콘솔 상속 연동 구현체 |
| **ReSet.Validator.Cli**<br/>(TUI/CLI 레이어) | [Program](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Cli/Program.cs) | 검증기 CLI 진입점. 디렉토리 사전 유효성 확인, 솔루션 루트 스캔, Ctrl+C 취소 연동 및 무인 배치 검증 흐름 제어 |
| | [ConsoleUserInteraction](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Cli/ConsoleUserInteraction.cs) | Spectre.Console 기반 TUI 렌더링. 탭(Tab) 자동완성 디렉토리 입력창(`ShowChoices(false)` 제어) 및 Gap 분석 결과 패널 렌더링 |
| **ReSet.Validator.Core**<br/>(검증 비즈니스 레이어) | [FileMappingService](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/FileMappingService.cs) | 명세서 파일명/YAML Front Matter 기반 구현 소스 매핑 및 경로 중복 자동 보정 |
| | [IValidatorPlugin](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Abstractions/IValidatorPlugin.cs) | C#([CsValidatorPlugin](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Plugins/CsValidatorPlugin.cs)), Java([JavaValidatorPlugin](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Plugins/JavaValidatorPlugin.cs)) 등 언어별 정적 구조 린터 플러그인 인터페이스 |
| | [IRuntimeRunner](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Abstractions/IRuntimeRunner.cs) | C# 및 Java 등 언어별 타겟 런타임 결과 수집 러너 표준 추상 인터페이스 |
| | [MockDataDto](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Models/MockDataDto.cs) | 기획된 관계형 모의 데이터를 로컬 및 메모리에 들고 있기 위한 데이터 모델 |
| | [CSharpReflectionRunner](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/CSharpReflectionRunner.cs) | C# 프로젝트 DLL 동적 로딩 및 리플렉션 호출, DbTransaction 강제 롤백을 활용한 DB 격리 실행기 |
| | [JavaProcessRunner](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/JavaProcessRunner.cs) | Java JAR/클래스를 외부 프로세스로 기동하여 stdin/stdout JSON 통신을 수행하는 격리 실행기 |
| | [ValidatorAiService](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/ValidatorAiService.cs) | AI 공급자(`IAiClient`)를 통해 입출력 및 비즈니스 로직에 대한 의미론적 Gap 분석 요청 및 역직렬화 수행. 추가로 데이터 정합성 검증용 테스트 파라미터 JSON 및 모의 테이블 데이터 생성 기능 탑재 |
| | [SpExecutionService](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/SpExecutionService.cs) | 입력받은 테스트 케이스 파라미터를 활용해 Legacy DB에서 Stored Procedure를 실행하고 결과를 다중 ResultSet 구조의 JSON 문자열로 직렬화하여 반환. Soft Fail 기반 예외 격리 적용 |
| | [SandboxSeedingService](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/SandboxSeedingService.cs) | 캐시된 모의 데이터를 샌드박스 DB에 자동 적재(Seed)하고 검증 완료 후 강제 제거(Cleanup)하는 라이프사이클 관리 서비스 |
| | [DataComparisonService](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/DataComparisonService.cs) | 수집된 레거시 결과 JSON과 신규 타겟 결과 JSON 데이터를 로드해, 행 수, 데이터 타입, 개별 컬럼의 동등성(실수 정밀도 및 날짜 정형화 적용)을 1:1 비교 검증해 상세 보고서 마크다운 생성 |
| | [CodeVerificationOrchestrator](file:///home/moondae/git-root/ReSet/src/ReSet.Validator.Core/Services/CodeVerificationOrchestrator.cs) | L1 정적 검사 -> L2 AI 논리 검증 및 자체 교정 -> L3 개발자 승인 총괄 오케스트레이터 및 결과 보고서 내보내기 |

---

## ⚙️ 핵심 아키텍처 메커니즘 (Core Mechanisms)

### 1. 재귀적 의존성 수집 및 예외 격리 (DFS & Soft Fail & Warnings)
* **하이브리드 재귀 탐색 알고리즘**: 타겟 SP가 참조하는 테이블, UDF, 하위 SP의 의존성을 `sys.sql_expression_dependencies`를 활용해 깊이 우선 탐색(DFS) 방식으로 수집합니다. 여기에 더하여, **DDL 소스 텍스트 내에서 동적 SQL 구문(EXEC, sp_executesql)을 2차 Regex 스캔하여 정적 의존성 뷰에서 누락되는 동적 참조 테이블들의 실시간 컬럼 스키마 및 주석 정보까지 강제 병합 수집**하는 하이브리드 탐색 매커니즘이 탑재되어 있습니다.
* **순환 참조 방지**:  탐색 중인 객체의 전체 이름을 담는 `HashSet<string> (visited)`을 관리하여 무한 루프 및 중복 DB 조회를 원천 차단합니다.
* **소프트 페일 및 경고 누적(Soft Fail & Warnings)**: 특정 의존 테이블의 스키마나 UDF DDL 조회 중 권한 누락이나 존재하지 않는 객체 참조 등의 비치명적 오류가 발생하면, 파이프라인 전체를 중단시키는 대신 해당 내역을 `SpDefinition.Warnings` 리스트에 누적합니다. 이 경고 데이터는 TUI 화면에 명확한 경고 패널(Panel)로 렌더링될 뿐만 아니라 AI 분석 프롬프트에도 포함되어 AI가 누락된 리소스를 감안하여 현실적인 분석 명세서를 쓰도록 돕습니다.

### 2. 비즈니스 뉘앙스 확보를 위한 확장 속성(Extended Properties) 맵핑
* 기술적 메타데이터(컬럼명, 데이터 타입) 수집 단계를 넘어, 데이터베이스의 확장 속성인 **`MS_Description`**에 등록된 테이블 요약과 컬럼별 한글 주석을 실시간 연동합니다.
* 이를 통해 AI 엔진이 `STAT_CD = 'A01'`과 같은 데이터 조작을 분석할 때, 실제 도메인 의미인 `상태코드 (A01: 대기)`를 정확히 인지하여 환각(Hallucination) 현상을 원천 방어합니다.

### 3. 3단계 신뢰성 검증 파이프라인 (Verification Pipeline)
* **다단계 검증 구조**: AI 분석을 거친 명세서의 완성도를 보장하기 위해 Level 1(기계적 정적 검사), Level 2(AI 리뷰어 상호 검토 및 자가 교정), Level 3(개발자 검토 및 승인)의 3단계 파이프라인을 활용하여 신뢰성 있는 비즈니스 기능 명세서를 확보합니다.
* **CancellationToken 전파 및 취소 안전성**: 대량의 SP 데이터 수집이나 원격 AI 응답 대기가 장시간 블로킹되거나 무한 대기가 발생하는 것을 방지합니다. `Program`의 메인 컨트롤러부터 `VerificationPipelineOrchestrator`, `AiService`, `DbMetadataService` 등 모든 비동기 호출 경로로 `CancellationToken`을 전파하였고, CLI 환경에서 `Ctrl+C` 입력 감지 시 `CancellationTokenSource`를 즉시 취소하여 안전하게 예외를 격리하고 메인 루프를 복구합니다.

### 4. 다중 AI 공급자 지원을 위한 추상화 아키텍처 (IAiClient)
* **결합도 분리(Decoupling)**: 기존에 `AiService` 내부에서 직접 HTTP Client를 구성하여 OpenAI 규격만을 강제해 사용하던 아키텍처를 전면 리팩토링하여, 구체적인 모델 통신 세부 사항을 `IAiClient` 인터페이스 뒤로 감췄습니다.
* **프로바이더별 전용 클라이언트 독립화**:
  * **OpenAiClient**: OpenAI 공식 채팅 엔드포인트에 대응.
  * **ClaudeClient**: Claude API(Anthropic Messages API) 형식에 맞춘 System Instruction 및 페이로드 구성.
  * **GoogleClient**: Google AI Studio 규격에 적합한 URI Query API Key 주입 및 SystemInstruction 구조 대응.
  * **OllamaClient**: 로컬 Ollama 환경이 제공하는 OpenAI 호환 API 엔드포인트에 맞춰 `OpenAiClient`를 대리자로 활용.
* **설정 파일 기반의 동적 다형성**: `appsettings.json` 내에 신설된 `Providers` 하위 설정들(ApiKey 및 Endpoint)을 기반으로, `AiClientFactory`가 지정된 활성 제공자(`Provider`)에 최적화된 `IAiClient` 인스턴스를 동적으로 생성하여 `AiService`에 주입(Dependency Injection)합니다.

### 5. 마이그레이션 소스코드 일치성 검증 메커니즘 (Validator)
* **설정 파일 기반 경로 절대화 보정**:
  - 설정 파일이나 CLI 인자로부터 수신한 상대 경로(`SpecDirectory`, `SourceCodeDirectory`, `OutputDirectory`)는 프로세스 구동 시 `Directory.GetCurrentDirectory()`를 결합하여 즉시 **절대 경로로 전환 보정** 처리됩니다. 이는 실행 시점의 워킹 디렉토리 차이로 발생 가능한 디렉토리 미조회 장애(IO Error)를 사전에 통제합니다.
* **공통 AI 공급자 클라이언트 다형성 공유**:
  - `ValidatorAiService` 또한 별도의 검증용 AI 라이브러리를 종속시키지 않고, `ReSet.Core`가 정의한 `IAiClient` 추상화 계약을 그대로 수신합니다. 이에 따라 `AiClientFactory`를 통해 빌드된 다중 AI 공급자(OpenAI, Claude, Google, Ollama) 객체를 다형적으로 재사용하며, 동일한 API 키 관리 전략을 일관되게 공유합니다.
* **스마트 설계서-코드 매핑 (Resolving Mappings)**:
  - 파일명 매칭 규칙(예: `dbo.CustOrderHist_Spec.md` -> `CustOrderHist.cs` / `CustOrderHist.java`)을 기반으로 연관 관계를 자동 탐색합니다.
  - 마크다운 설계서 상단에 YAML Front Matter(`TargetCode: ...`)로 명확한 목적 경로가 명시된 경우 우선순위를 부여하며, 실행 경로에 따라 중복된 접두사 경로(예: `src/` 중복)를 자동 슬라이싱해 보정해 주는 장애 방지 로직이 탑재되어 있습니다.
* **언어별 정적 구문 분석 플러그인 (L1 Linter)**:
  - `IValidatorPlugin` 인터페이스를 통해 C# 및 Java 구문 검증기를 다형적으로 운영합니다.
  - 정교한 컴파일러 대신 경량화된 중괄호 쌍 일치 분석 및 Regex 클래스/메소드 식별자 스캔 방식을 채택하여 라이브러리 의존성 충돌 없는 고속 L1 정적 검사를 지원합니다.
* **AI 의미론적 Gap 분석 (L2 Semantic Check)**:
  - `ValidatorAiService`가 검토 에이전트를 가동해 입력 파라미터, 출력셋, 핵심 비즈니스 로직, 예외/트랜잭션 세부 처리를 설계서와 비교 검증합니다.
  - 비교 분석 결과는 사전 약속된 JSON 규격(`GapReport`)으로만 수집하여 파싱 안전성을 확보하였으며, 불일치 식별 시 **이전 시도의 구체적인 불일치 Gap 항목 및 Suggestions 수정 지침을 프롬프트 피드백(Feedback Log)으로 전달하는 지능형 자체 교정(`MaxL2Attempts`)**을 구동해 정합성을 극대화합니다.
* **사용자 친화적 대화형 경로 확보 (TUI UX)**:
  - 실행 설정된 폴더가 유효하지 않을 경우 `DirectoryNotFoundException`으로 튕기지 않고, TUI 화면에서 사용자에게 유효 경로를 다시 입력하도록 루프형 재요청을 지원합니다.
  - 탭(Tab) 키 및 화살표 키를 이용해 로컬 디렉토리 후보군을 자동 완성(AutoComplete)하도록 설계했으며, 슬래시(`/`) 구분자 충돌로 인해 지저분하게 렌더링되던 선택지 출력을 `ShowChoices(false)` 옵션을 통해 시각적으로 제어하여 사용성을 향상했습니다.

### 6. 데이터 정합성 검증 엔진 (Data Verification Runner)
비즈니스 로직에 대한 AI 검토를 마친 뒤, 실제 기존 Stored Procedure와 마이그레이션된 타겟 소스코드를 런타임 상에서 구동해 결과 데이터를 대조하는 검증 메커니즘입니다.
* **AI 기반 동적 테스트 케이스 설계 (`ValidatorAiService.GenerateTestParametersAsync`)**:
  - 작성된 기능 명세서(`*_Spec.md`)의 입출력 정보를 AI 분석가에게 제공하여, 정상 호출 조건뿐만 아니라 경계값(Boundary), 예외/오류 유발 조건까지 망라하는 다양한 테스트 케이스와 파라미터 세트를 기획하여 `*_test_inputs.json` 형태로 출력합니다.
* **레거시 DB 실시간 실행 데이터 수집 (`SpExecutionService`)**:
  - 수집된 테스트 케이스 JSON을 파싱하여, 실제 SQL Server 데이터베이스에 연결한 뒤 Stored Procedure를 직접 실행합니다.
  - SP가 반환하는 다중 결과 세트(Multiple Result Sets)를 `SqlDataReader`를 통해 누락 없이 추적하며, DB 상의 Null 값은 JSON 직렬화가 가능하게 안전하게 변환 처리하여 `*_legacy_results.json`으로 물리 덤프합니다.
  - 데이터베이스의 일시적 장애나 자격 증명 오류가 빌드 전체를 실패시키지 않도록 **연결 및 실행 예외를 격리(Soft Fail)**하여 결과 파일 내부의 각 테스트 케이스에 `FAIL` 상태를 기록하고 정상 리턴합니다.
* **유연한 1:1 데이터 동등성 비교 알고리즘 (`DataComparisonService`)**:
  - 레거시 덤프 JSON과 신규 타겟 결과 덤프 JSON(`*_target_results.json`)을 동적으로 역직렬화하여 구조 대조를 실시합니다.
  - **3차원 대조 매트릭스**: 테스트 케이스 단위 ➔ 결과 세트(Result Set) 인덱스 단위 ➔ 행(Row) 및 컬럼 값 단위까지 계층적으로 1:1 대조합니다.
  - **데이터 값 포맷 정규화**: 실수의 부동 소수점 자릿수 표현 차이나 날짜/시간(DateTime) 문자 포맷 차이로 인한 미세한 문자열 불일치(False Positives)를 방지하기 위해, 타입 감지 시 `NormalizeValueString`을 통해 정규화 처리를 거친 후 비교를 실행합니다.
  - 불일치 항목들이 한눈에 파악되도록 불일치율, 행 수 미스매치, 컬럼 값 차이를 비교 요약 표와 상세 목록으로 작성한 검증 보고서 마크다운 `*_CompareReport.md`를 자동으로 출력 디렉토리에 내보냅니다.

### 7. SHA-256 해시 기반 로컬 증분 캐싱 (Caching Infrastructure)
* **Composite Signature Hash**: 대상 SP의 SQL 본문 텍스트와 모든 하위 의존성 객체의 DDL 원문을 각각 SHA-256으로 해싱한 뒤, 키로 정렬 및 병합하여 복합 해시값을 산출합니다.
* **증분 스킵 (Cache Hit)**: `./output/.sp_cache_index.json` 파일에 저장된 기존 캐시값과 비교하여 대조 결과가 동일하고, 물리적인 마크다운 명세서 파일이 손실되지 않았다면 LLM 분석 및 3단계 검증 전체 루프를 건너뜁니다.
* **안전한 격리**: 로컬 인덱스에 쓰기 실패하거나 손상된 경우에도 Soft Fail 형태로 예외가 무시되어, 원본 비즈니스 분석 전체 흐름에 영향을 끼치지 않습니다.

### 8. 하이브리드 타겟 런타임 결과 수집 및 DB 격리
* **C# DLL 리플렉션 기동**: 마이그레이션된 C# 타겟 코드의 경우, 빌드 디렉토리의 DLL을 검색하여 로드한 뒤 생성자 분석을 통해 `SqlConnection` 및 `SqlTransaction`을 리플렉션 주입하여 실행합니다.
* **Java 외부 프로세스 구동**: JAR 또는 클래스 파일을 외부 Java 런타임 프로세스로 실행하고, 입력 페이로드를 표준 입력(stdin)으로 공급하여 실행 완료된 결과를 표준 출력(stdout)으로 파싱해 수집합니다. 30초 타임아웃을 두어 CLI 교착을 방지합니다.
* **DbTransaction 강제 롤백**: 로직 수행 완료 성공 여부에 관계없이 항상 `Rollback()`을 호출하여 DB 데이터 수정을 예방하는 격리 전략을 지원합니다.

### 9. TUI 로그인 세션 및 연결 정보 실시간 변경
* **연결 정보 즉석 수정**: 로컬 세션 파일(`.session.json`)에서 직전 로그인 성공 계정 정보를 성공적으로 불러온 경우에도, 사용자가 설정 파일(`appsettings.json`)을 열어 수정할 필요 없이 TUI 화면에서 즉시 서버 주소 및 데이터베이스 이름을 확인하고 직접 수정하여 다른 DB 인스턴스에 안전하게 교체 연결할 수 있습니다.
* **사용성 개선**: 연결 정보 재입력 번거로움을 줄이면서도 동적으로 타겟 데이터베이스를 손쉽게 오갈 수 있는 편리한 접속 환경을 유지합니다.

### 10. Multi-SP 전환 계획을 위한 순서 보장형 TUI 수집 메커니즘
* **물리 선택 순서 보장**: 다중 선택(Multi-select) UI 컴포넌트는 사용자가 스페이스바로 항목을 선택하더라도 최종 선택 반환 결과에서 사용자가 누른 순서(Sequence)를 보장하지 않고 알파벳 순서 등으로 정렬되는 한계가 있습니다.
* **순차 단일 선택 루프**: 배치 작업 설계 시 각 SP 마이그레이션 스텝의 논리적 실행 순서를 엄격히 반영할 수 있도록, TUI 화면에서 사용자가 목록을 보며 원하는 순서대로 하나씩 SP 명세서를 선택하여 큐(Queue)에 적재하고, 최종 `[-- 완료 및 배치 계획 수립 --]` 메뉴를 선택하여 처리를 마치는 단일 순차 선택 루프 흐름을 제공합니다.

### 11. 외부 코딩 에이전트 연동용 마이그레이션 지시서 번들링 및 자동 기동 브릿지 (Migration Instructions & Codegen Bridge)
* **단절 없는 개발 경험 제공**: SP 분석 ➔ 통합 배치 전환 계획 수립 및 통합 지시서 작성 ➔ 코드 마이그레이션 ➔ 코드 검증의 전체 흐름에서, 마이그레이션 소스 코드를 생성하는 주체를 외부의 전문 코딩 에이전트(Claude Code, agy, codex 등)로 위임하되 결합도 및 사용자 단절을 최소화하기 위한 브릿지 구조를 탑재했습니다.
* **무인 배치 통합 계획 수립 자동화**: TUI 메뉴뿐만 아니라 무인 배치 자동화 실행 시에도 `--job-name` 인자가 공급되면 `RunConsolidatedPipelineAsync`가 L3 대화형 단계를 건너뛰고 자동으로 통합 계획 및 지시서 번들을 생성해 외부 에이전트 프로세스 기동까지 연속 수행하는 CI/CD 무인 파이프라인을 온전히 지원합니다.
* **구조화된 컨텍스트 패키징**: 사용자가 직접 DDL/계획서 등을 코딩 에이전트에 공급하는 번거로움을 해결하기 위해, 최종 승인된 **통합 배치 전환 계획서(Plan)**와 이에 매핑되는 각 SP들의 **설계 명세서(Spec)**, **레거시 DDL 원본**, 그리고 **참조하는 모든 외부 스키마 및 DDL** 정보들을 하나의 마이그레이션 지시서 문서(`{JobName}_MigrationInstructions.md`)로 빌드하여 자동 추출합니다.
* **즉시 사용 가능한 프롬프트 내장**: 문서 말단에는 사용자가 복사하여 곧바로 사용할 수 있는 표준화된 마이그레이션 명령 프롬프트 템플릿을 명시해 둠으로써 프롬프팅 누락 및 실수 가능성을 원천 제거합니다.
* **대화형 콘솔 상속 및 양방향 제어 (Terminal Stream Sharing)**:
  - CLI 연동 구동 시 자식 프로세스의 입출력 스트림을 리다이렉션하여 가두지 않고, 현재 작동 중인 부모 콘솔 스트림을 직접 상속 공유(`RedirectStandardInput/Output = false`)하도록 구현했습니다.
  - 이를 통해 Claude Code 등 대화형 에이전트가 실행되는 도중 발생할 수 있는 사용자의 수동 승인 요청(Y/N)이나 자연어 질의응답 등의 양방향 인터랙션을 동일한 콘솔에서 매끄럽게 처리할 수 있습니다.
* **취소 안전성 및 프로세스 격리 (Cancellation Safety & Process Isolation)**:
  - 마이그레이션 전체 파이프라인에서 전파되는 `CancellationToken`과 외부 기동 프로세스의 수명 주기를 연결했습니다.
  - CLI 상에서 사용자가 `Ctrl+C` 등을 통해 마이그레이션 분석 프로세스를 중단시키면, 토큰 해제 이벤트 핸들러가 가동 중인 하위 코딩 에이전트 프로세스 트리 전체를 강제 종료(`process.Kill(true)`)하여 유령 백그라운드 프로세스가 리소스를 독점하는 좀비 현상을 예방합니다.
* **프롬프트 단일 인자 패키징 (Query Parameter Wrapping)**:
  - 띄어쓰기가 포함된 여러 단어로 조립된 프롬프트가 외부 에이전트 CLI 구동 시 여러 개의 인자(Subcommands)로 쪼개져 인식되는 파싱 에러를 차단하기 위해, 전체 프롬프트 구문을 이스케이프된 쌍따옴표(`\"...\"`)로 감싸 단일 문자열 매개변수로 대상 CLI에 안전하게 공급하는 템플릿 바인딩 전략을 적용했습니다.

### 12. 모의 데이터(Mock Data) 자동 생성 및 격리 적재 라이프사이클 (Sandbox Seeding & Lifecycle)
* **관계지향 모의 데이터 자동 생성 (`--gen-mock-data`)**:
  - 보안 규정 및 사내 정책상 실제 운영/개발 데이터를 활용할 수 없는 환경에 대응하여, AI 분석을 통해 물리적 FK가 없더라도 JOIN문과 컬럼 메타데이터를 분석하여 상호 연결성을 가진 고품질 모의 데이터를 `MockDataDto` 형태로 생성 및 로컬 캐싱합니다.
* **Sandbox Seeding 메커니즘**:
  - 데이터 정합성 검증 실행(Legacy DB SP 호출 및 Target 소스코드 구동) 직전에, `SandboxSeedingService`가 기동되어 캐싱된 관계형 모의 데이터를 대상 샌드박스 데이터베이스에 적재(Seed)합니다.
* **수명주기 소거(Clean-up) 및 상태 격리**:
  - 레거시 및 타겟 런타임 결과 데이터 수집 작업이 종료되는 즉시, 데이터 정합성 대조를 진행하기 이전에 적재했던 모의 테이블들의 데이터를 강제로 소거(Delete/Truncate)하여 샌드박스 데이터베이스의 원래 무결 상태를 완벽하게 원복시킵니다.
  - 이를 통해 로컬/샌드박스 DB의 타 테스트 케이스나 프로세스에 부작용(Side Effect)이 전파되지 않도록 완벽한 생명주기 격리를 수행합니다.

### 13. 정합성 검증 실패 시의 3단계 복구 피드백 루프 (Failure Recovery Feedback Loops)
데이터 정합성 대조 및 검증 작업이 실패(`FAIL`)하였을 때, 전체 작업을 원점(1단계 분석)에서부터 비효율적으로 재실행하는 대신 결함의 유형에 맞추어 점진적이고 효율적으로 피드백 루프를 분기하는 예외 대응 아키텍처입니다.
* **루프 A (설계 재수립 - Loop A: Spec Feedback)**:
  - **원인**: 레거시 SQL의 비즈니스 규칙 해석 오류 등 **비즈니스 설계 명세서(`*_Spec.md`) 자체에 결함**이 식별된 경우입니다.
  * **흐름**: Level 3 인간 피드백 콘솔에 실패한 비교 분석 리포트(`*_CompareReport.md`) 및 `GapReport`를 입력 피드백으로 주입하여, 기능 설계서를 보완/재생성하고 이에 맞추어 코드를 재생성하도록 오케스트레이션합니다.
* **루프 B (소스코드 보완 - Loop B: Code Refactoring)**:
  - **원인**: 설계 명세서는 완벽하지만, **마이그레이션된 소스코드 구현부에 로직 누락이나 코딩 버그**가 존재하는 경우입니다.
  * **흐름**: 설계서 재생성 과정을 전면 스킵하고, 불일치 명세(`GapReport`)만 외부 코딩 에이전트 CLI 프로세스에 주입하여 소스코드만 수정 및 부분 리팩토링하도록 유도합니다. (2단계 생성 브릿지로 바로 점프)
* **루프 C (테스트 튜닝 - Loop C: Param Tuning)**:
  - **원인**: 날짜 포맷 표현 차이나 부동 소수점 자릿수 정밀도 오차 등 **환경적 차이로 인해 미세한 덤프 데이터 불일치(False Positive)**가 발생한 경우입니다.
  * **흐름**: 설계서와 소스코드는 건드리지 않고, 테스트 케이스 매개변수(`*_test_inputs.json`)의 경계 조건을 보완하거나 `DataComparisonService` 내 정규화 함수(`NormalizeValueString`)의 정밀도 비교 조건을 조정하여 덤프 비교 수집을 재구동합니다.

---

## 📊 프로그램 실행 흐름 (Visual Execution Flow)

아래 다이어그램은 ReSet 프로그램이 기동되어 설정 파싱, 데이터베이스 메타데이터 재귀 수집, AI 분석 및 결과 저장/이탈까지의 거시적인(Macro) 전체 실행 흐름을 시각적으로 나타냅니다.

```mermaid
graph TD
    %% 1단계: 초기화 및 연결
    subgraph Setup ["1. 초기 설정 및 DB 연결 (Setup)"]
        Start["시작 (CLI 실행)"] --> Parse["설정 로드 및 CLI 인자 파싱 (CliArgs)"]
        Parse --> ModeCheck{"배치 모드 여부?"}
        
        ModeCheck -- "아니오 (TUI)" --> TUI["대화형 로그인 입력<br/>(세션 복구 및 실시간 연결 정보 수정)"]
        ModeCheck -- "예 (Batch)" --> Batch["연결 문자열 추출 (인자/환경변수)"]
        
        TUI & Batch --> ConnTest["데이터베이스 연결성 검증"]
        ConnTest --> LoadSps["전체 Stored Procedure 목록 로드"]
    end
    
    %% 2단계: 대상 필터링
    subgraph Selection ["2. 분석 대상 필터링 (Selection)"]
        LoadSps --> TargetCheck{"배치 모드 여부?"}
        
        TargetCheck -- "아니오" --> SelectTUI["개별 SP 선택 / Multi-SP 순차 선택 루프<br/>(물리 선택 순서 보장)"]
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

## ⚙️ 데이터 정합성 검증 실행 흐름 (Data Verification Workflow)

아래 다이어그램은 마이그레이션된 소스코드와 레거시 DB SP 간의 데이터 정합성을 확인하기 위해 설계된 테스트 케이스 기획, 모의 데이터(Mock Data) 자동 생성 및 Sandbox DB 적재/소거, 그리고 실행 결과 데이터를 1:1 대조하는 전체 검증 실행 흐름입니다.

```mermaid
graph TD
    %% 입력 자료
    Spec["비즈니스 기능 명세서<br/>(*_Spec.md)"] --> GenInputs["1. 테스트 케이스 자동 설계 (AI)<br/>(ValidatorAiService)"]
    
    %% 테스트 파라미터 파일
    GenInputs --> InputJson["테스트 파라미터 파일<br/>(*_test_inputs.json)"]
    
    %% 모의 데이터 생성
    Spec --> GenMock["1-2. 모의 데이터 생성 (AI)<br/>(ValidatorAiService --gen-mock-data)"]
    GenMock --> MockJson["모의 데이터 파일<br/>(*_mock_data.json)"]
    
    %% Sandbox Seeding (적재)
    MockJson --> SeedDB["2-1. Sandbox DB Seeding (적재)<br/>(SandboxSeedingService.SeedAsync)"]
    
    %% Legacy 수집
    InputJson & SeedDB --> ExecLegacy["2-2. 레거시 DB 실행 (수집)<br/>(SpExecutionService)"]
    ExecLegacy --> LegacyJson["레거시 결과 덤프<br/>(*_legacy_results.json)"]
    
    %% Target 수집
    InputJson & SeedDB --> ExecTarget["3. 마이그레이션 프로그램 실행<br/>(타겟 배치/클래스 구동 - 트랜잭션 격리)"]
    ExecTarget --> TargetJson["신규 타겟 결과 덤프<br/>(*_target_results.json / *_new_results.json)"]
    
    %% Sandbox Cleanup (소거)
    LegacyJson & TargetJson --> CleanupDB["3-2. Sandbox DB Cleanup (소거)<br/>(SandboxSeedingService.CleanupAsync)"]
    
    %% 비교 대조
    CleanupDB --> CompareData["4. 데이터 1:1 대조 및 요약 표 작성<br/>(DataComparisonService)"]
    CompareData --> Report["데이터 정합성 보고서 생성<br/>(*_CompareReport.md)"]
```

---

## 📈 활용 분야 및 기대 효과

* **레거시 시스템 마이그레이션**: 오랜 기간 정비되지 않은 대규모 레거시 Stored Procedure의 비즈니스 로직을 빠르게 문서화하고 도식화함과 동시에 현대화 마이그레이션 계획을 상세히 수립합니다.
* **운영 환경 격리 및 회귀 테스트 자동화**: 보안상 실제 운영 데이터를 외부 망으로 유출할 수 없는 엄격한 컴플라이언스 환경에서도, AI가 DDL과 비즈니스 설계를 파싱해 유의미하게 연관된 관계형 모의 데이터(Mock Data)를 자동 생성하고 샌드박스에서 완벽한 격리 수명주기 하에 테스트를 자동 회귀(Regression Test) 수행합니다.
* **마이그레이션 신뢰성 보장**: 단순 코드 로직의 정적/논리적 일치성을 검사하는 수준을 넘어, 실제 데이터베이스에 쿼리를 실행해보고 그 출력값의 정밀도까지 1:1로 비교함으로써 마이그레이션 과정에서의 무결성을 실시간으로 확인하고 보장합니다.
* **신입 개발자 온보딩**: 복잡한 데이터베이스 의존 관계를 AI 에이전트가 탐색하여 다이어그램과 함께 구조적으로 해설하고 포팅 가이드라인을 제공하므로 개발 지식 전파 비용을 대폭 낮춥니다.
* **CI/CD 파이프라인 자동화**: 주기적으로 배치 모드를 실행하여 데이터베이스 스키마와 프로시저 변경 이력을 명세서로 자동 추적하고 변경 감지 리포트를 산출할 수 있습니다.
