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
