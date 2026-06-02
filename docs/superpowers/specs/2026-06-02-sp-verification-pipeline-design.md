# [설계서] SP Reverse-Engineering 명세서 3단계 검증 파이프라인 (Verification Pipeline)

본 설계서는 SQL Server Stored Procedure 기능 명세서 자동 생성 과정에서 산출물의 무결성과 신뢰성을 높이기 위한 3단계 검증(정적 검증, AI 교차 검증, 인간 개입형 승인) 파이프라인의 설계 및 명세입니다.

---

## 1. 아키텍처 및 신규 컴포넌트 (Architecture & Components)

3단계 검증 아키텍처는 독립된 각 검사 단계들이 분리된 책임을 가지고 유기적으로 결합되어 동작합니다.

### 1.1 `MechanicalValidator` (신설, `SpAnalyzer.Core` 프로젝트)
* **역할**: AI가 생성한 마크다운 문서의 정적 형식 및 문법 규칙을 정적으로 린팅(Linting)합니다.
* **주요 기능**:
  * **섹션 구성 검사**: 마크다운 텍스트 내에서 필수 섹션 헤더(`## 개요`, `## 파라미터 목록`, `## CRUD 분석`, `## 로직 흐름 요약`, `## 비즈니스 흐름 시각화 (Mermaid Diagram)`)의 유무를 확인합니다.
  * **Mermaid 간이 Linter (Regex)**: ````mermaid ```` 코드 블록 내에서 괄호(`(`, `)`, `[`, `]`)나 특수문자가 들어간 노드가 문법 에러를 방지하기 위해 큰따옴표(`"`)로 알맞게 래핑되어 있는지 검사합니다.
* **인터페이스**:
  ```csharp
  namespace SpAnalyzer.Core.Services
  {
      public class MechanicalValidator
      {
          public ValidationResult Validate(string markdown);
      }

      public class ValidationResult
      {
          public bool IsValid { get; set; }
          public List<string> Errors { get; set; } = new();
          public string? SuggestedPromptFix => IsValid ? null : string.Join("\n", Errors);
      }
  }
  ```

### 1.2 `IAiService` 및 `AiService` (기존 클래스 확장, `SpAnalyzer.Core` 프로젝트)
* **역할**: 교차 리뷰어 에이전트 역할을 대행하고, 자가 수정 흐름을 지원합니다.
* **주요 변경 사양**:
  * **기존 생성 API 확장**: `feedbackLog` 파라미터를 추가 주입받아, 이전 검증 오류나 사용자 피드백을 프롬프트 하단에 덧붙여 재생성을 요청할 수 있도록 지원합니다.
    ```csharp
    Task<string> GenerateSpecificationAsync(SpDefinition spDef, string userInstructions, string? feedbackLog = null);
    ```
  * **AI 교차 리뷰 추가**: 별도 리뷰어용 System Prompt를 호출하여 명세서의 완성도 및 누락사항을 검사하는 API를 제공합니다.
    ```csharp
    Task<ReviewResult> ReviewSpecificationAsync(SpDefinition spDef, string specMarkdown);
    ```
    ```csharp
    public class ReviewResult
    {
        public bool HasDefects { get; set; }
        public string? FeedbackComment { get; set; }
    }
    ```

---

## 2. 상세 데이터 흐름 및 예외 처리 (Data Flow & Error Handling)

### 2.1 검증 단계별 데이터 흐름
1. **1차 생성**: `SpDefinition`과 `instructions.txt`를 바탕으로 최초 명세서를 작성합니다.
2. **L1 검증 (기계적)**: 
   * `MechanicalValidator.Validate()`를 수행합니다. 
   * 실패 시, 검출된 오류 피드백 로그를 지참하여 `GenerateSpecificationAsync`로 N=1회 재생성을 요청합니다.
3. **L2 검증 (AI 교차)**: 
   * L1을 성공한 문서를 대상으로 `ReviewSpecificationAsync()`를 호출합니다.
   * 결함 발견 시, AI 리뷰 내용을 `feedbackLog`에 지참하여 `GenerateSpecificationAsync`로 N=1회 재생성을 요청합니다.
4. **L3 검증 (인간 개입)**:
   * TUI 모드 실행 시에만 적용하며, 최종 검증된 문서를 개발자에게 제시하고 다음 메뉴를 제안합니다.
     * `[Approve]` -> 명세서 및 덤프를 저장하고 루프 완료.
     * `[Feedback]` -> 보완 의견을 텍스트로 받아 처음 1단계부터 재생성 루프 진입.
     * `[Cancel]` -> 저장하지 않고 메인 SP 검색 화면으로 이탈.
   * 무인 처리되는 배치 모드에서는 L3를 무시하고 즉시 저장 완료합니다.

### 2.2 예외 처리 및 견고성 (Robustness)
* **LLM 통신 장애**: API Key 누락, 타임아웃, 속도 제한 예외 발생 시 `try-catch`로 포착합니다. 배치 모드 실행 중에는 개별 SP의 장애가 다른 SP 배치 처리에 영향이 없도록 에러 로그 출력 후 다음 SP로 즉시 건너뜁니다.
* **정적 검증 장애**: `MechanicalValidator` 내부 파싱 도중 발생하는 뜻하지 않은 예외는 `try-catch`로 잡아서 소프트 페일(`IsValid = true`)로 처리하여 툴의 기동성을 저해하지 않도록 합니다.

---

## 3. 테스트 및 검증 방안 (Testing & Verification)

### 3.1 단위 테스트 설계 (`tests/SpAnalyzer.Core.Tests/MechanicalValidatorTests.cs` 신설)
1. **`Validate_WithValidMarkdown_ShouldReturnTrue`**:
   * 모든 필수 헤더가 완벽히 존재하고, Mermaid 노드 내 괄호가 큰따옴표로 올바르게 감싸진 마크다운 전달 시 검증 통과를 검증합니다.
2. **`Validate_WithMissingHeaders_ShouldReturnFalse`**:
   * 필수 헤더(`## CRUD 분석` 등)가 누락되었을 때 `IsValid == false` 및 누락 헤더 에러 메시지 검출을 검증합니다.
3. **`Validate_WithInvalidMermaidBrackets_ShouldReturnFalse`**:
   * Mermaid 블록 내 노드 텍스트에 큰따옴표 없이 괄호(`node[Label (Brackets)]`)가 쓰였을 때 린팅 에러 검출을 검증합니다.

### 3.2 AI 교차 리뷰 모킹 테스트
* `AiServiceTests.cs`를 업데이트하여 OpenAI API Key가 비어있을 때 `ReviewSpecificationAsync`가 적절히 ArgumentException 예외를 발생시키는지 검증하고, 리뷰 프롬프트 구성 무결성을 확인합니다.
