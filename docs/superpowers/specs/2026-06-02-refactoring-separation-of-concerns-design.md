# 설계 명세서: Core와 CLI 계층 분리 및 모듈화 리팩토링
- **작성일:** 2026-06-02
- **작성자:** Antigravity (AI 어시스턴트)
- **상태:** 승인 완료

---

## 1. 개요 (Overview)

본 문서는 `SpAnalyzer` 솔루션 내의 핵심 비즈니스 로직(`SpAnalyzer.Core`)과 사용자 콘솔 인터페이스(`SpAnalyzer.Cli`) 간의 강한 결합을 끊어내고 모듈형 아키텍처로 개선하기 위한 상세 설계 사양을 정의합니다.

기존 구조에서는 터미널 UI 프레임워크인 `Spectre.Console`의 제어 흐름과 로깅 코드가 비즈니스 분석 파이프라인과 결합되어 있어 핵심 로직의 재사용이 불가능하고 테스트 작성이 까다로웠습니다. 이를 해결하기 위해 중계 역할을 담당하는 오케스트레이터를 Core 영역에 두고, UI 상호작용은 인터페이스로 추상화하여 주입하는 설계(DI)를 구현합니다.

---

## 2. 아키텍처 및 인터페이스 설계 (Architecture & Interfaces)

Core 프로젝트 내에 화면 출력을 포함하여 사용자와 상호작용하기 위한 명세를 인터페이스로 정의하고, CLI 프로젝트가 이를 구체적으로 구현하도록 설계합니다.

### 2.1. `IVerificationUserInteraction` 인터페이스
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using SpAnalyzer.Core.Models;

namespace SpAnalyzer.Core.Services
{
    public interface IVerificationUserInteraction
    {
        // 일반 진행 상태 및 안내 메시지 출력
        void NotifyStatus(string message);

        // 예외 메시지 및 경고 출력
        void NotifyError(string message);

        // L1 기계 검증 단계의 오류 정보 출력
        void NotifyL1Errors(string selectedOption, int attempt, List<string> errors);

        // L2 AI 리뷰의 결함 피드백 코멘트 출력
        void NotifyL2Defects(string selectedOption, int attempt, string feedbackComment);

        // 검증 파이프라인 단계 성공 알림
        void NotifyValidationSuccess(string selectedOption);

        // L3 인간 개입형 검증 화면 제공 및 승인/피드백 결과 대기
        Task<HumanReviewResult> RequestHumanReviewAsync(string selectedOption, string specificationMarkdown);
    }
}
```

### 2.2. 상호작용 결과 데이터 모델
```csharp
namespace SpAnalyzer.Core.Models
{
    public enum UserDecision
    {
        Approve,          // 승인 및 최종 저장
        ProvideFeedback,    // 추가 보완 요청 피드백 입력
        Cancel            // 저장 없이 이탈
    }

    public class HumanReviewResult
    {
        public UserDecision Decision { get; set; }
        public string? UserFeedback { get; set; }
    }
}
```

---

## 3. 핵심 컴포넌트 설계 (Component Design)

### 3.1. `VerificationPipelineOrchestrator` 클래스
`SpAnalyzer.Core`의 `Services` 디렉터리에 새로 추가되며, 전체 검증 파이프라인 흐름을 총괄 오케스트레이션합니다. 
기존 `Program.cs` 내의 `RunVerificationPipelineAsync` 로직을 그대로 이전하되, `AnsiConsole` 직접 호출 대신 생성자로 주입된 `IVerificationUserInteraction`의 추상화된 메서드를 사용합니다.

```csharp
namespace SpAnalyzer.Core.Services
{
    public class VerificationPipelineOrchestrator
    {
        private readonly IDbMetadataService _dbService;
        private readonly IAiService _aiService;
        private readonly MechanicalValidator _validator;
        private readonly IVerificationUserInteraction _userInteraction;

        public VerificationPipelineOrchestrator(
            IDbMetadataService dbService,
            IAiService aiService,
            MechanicalValidator validator,
            IVerificationUserInteraction userInteraction)
        {
            _dbService = dbService;
            _aiService = aiService;
            _validator = validator;
            _userInteraction = userInteraction;
        }

        public async Task<(string? SpecMarkdown, SpDefinition? SpDef)> RunPipelineAsync(
            string connectionString,
            string schema,
            string name,
            int maxDepth,
            string provider,
            string instructions,
            bool isBatchMode)
        {
            // 전체 파이프라인 워크플로우 제어
            // 1. GetSpDetailsAsync 호출
            // 2. GenerateSpecificationAsync 호출 (L1/L2 검증 루프)
            // 3. (isBatchMode가 아닐 시) RequestHumanReviewAsync 호출을 통한 L3 피드백/재생성 제어
        }
    }
}
```

---

## 4. 데이터 흐름 및 클래스 통합 (Data Flow & CLI Integration)

### 4.1. CLI 프레젠테이션 결합 (`ConsoleUserInteraction`)
`SpAnalyzer.Cli` 프로젝트 내에 `IVerificationUserInteraction` 인터페이스를 상속받는 구체 클래스를 선언합니다.

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectre.Console;
using SpAnalyzer.Core.Models;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Cli
{
    public class ConsoleUserInteraction : IVerificationUserInteraction
    {
        public void NotifyStatus(string message)
        {
            AnsiConsole.MarkupLine(message);
        }

        public void NotifyError(string message)
        {
            AnsiConsole.MarkupLine($"[red]{message}[/]");
        }

        public void NotifyL1Errors(string selectedOption, int attempt, List<string> errors)
        {
            AnsiConsole.MarkupLine($"[yellow]{selectedOption} - [[L1 기계 검증]] 문법/구조 오류 발견 (시도 {attempt}/2):[/]");
            foreach (var err in errors)
            {
                AnsiConsole.MarkupLine($"  [red]=> {Markup.Escape(err)}[/]");
            }
        }

        public void NotifyL2Defects(string selectedOption, int attempt, string feedbackComment)
        {
            AnsiConsole.MarkupLine($"[yellow]{selectedOption} - [[L2 AI 리뷰]] 결함 및 보완 권고 발견 (시도 {attempt}/2):[/]");
            AnsiConsole.MarkupLine($"  [red]=> {Markup.Escape(feedbackComment)}[/]");
        }

        public void NotifyValidationSuccess(string selectedOption)
        {
            AnsiConsole.MarkupLine($"[green]{selectedOption} - [[L1/L2 자동 검증]] 모두 통과![/]");
        }

        public async Task<HumanReviewResult> RequestHumanReviewAsync(string selectedOption, string specificationMarkdown)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[yellow]{selectedOption} 생성된 기능 명세서[/]") { Justification = Justify.Left });
            AnsiConsole.Write(new Text(specificationMarkdown));
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.WriteLine();

            var menuChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold blue]{selectedOption} 명세서 검증 완료.[/] 다음 작업을 선택하세요:")
                    .AddChoices(new[] { "1. 승인 및 최종 저장 (Approve)", "2. 추가 보완 요청 피드백 입력 (Feedback)", "3. 저장 없이 이탈 (Cancel)" })
            );

            if (menuChoice.StartsWith("1"))
            {
                return new HumanReviewResult { Decision = UserDecision.Approve };
            }
            if (menuChoice.StartsWith("3"))
            {
                return new HumanReviewResult { Decision = UserDecision.Cancel };
            }

            var userFeedback = AnsiConsole.Prompt(
                new TextPrompt<string>("보완할 피드백 내용을 구체적으로 기재해 주십시오:")
            );

            if (string.IsNullOrWhiteSpace(userFeedback))
            {
                AnsiConsole.MarkupLine("[yellow]피드백이 비어있어 승인 여부 선택 메뉴로 복귀합니다.[/]");
                return new HumanReviewResult { Decision = UserDecision.ProvideFeedback, UserFeedback = null };
            }

            AnsiConsole.MarkupLine("[blue]사용자 피드백을 적용하여 보완 분석 프로세스를 재가동합니다...[/]");
            return new HumanReviewResult
            {
                Decision = UserDecision.ProvideFeedback,
                UserFeedback = userFeedback
            };
        }
    }
}
```

### 4.2. `Program.cs` 메인 진입점 리팩토링
`Program.cs`는 DB 입력 수집, `appsettings.json` 설정 파일 바인딩, 그리고 최종 완성된 명세서 및 덤프 데이터를 로컬 파일로 디스크에 쓰는 최종 저장소(I/O) 역할만 관리하며 비즈니스 로직 제어 루프는 최소화됩니다.

---

## 5. 테스트 및 검증 전략 (Testing & Verification Strategy)

* **단위 테스트 구축:** `IVerificationUserInteraction`을 가상(Mocking)으로 모방하여, UI 실행 없이 단위 테스트 코드 내에서 AI 2회차 보완 성공, L3 피드백 루프 정상 분기, 데이터베이스 조회 에러 처리 등의 핵심 파이프라인의 제어 흐름 동작을 무결하게 테스트할 수 있습니다.
* **통합 빌드 테스트:** CLI 빌드 후 대화형 TUI 모드 및 배치 모드 구동을 직접 실행하여 화면상의 렌더링이 깨짐 없이 잘 흘러가는지 최종 수동 테스트를 병행합니다.
