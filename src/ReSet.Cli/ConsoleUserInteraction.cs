using System.Collections.Generic;
using System.Threading.Tasks;
using Spectre.Console;
using ReSet.Core.Models;
using ReSet.Core.Services;

namespace ReSet.Cli
{
    public class ConsoleUserInteraction : IVerificationUserInteraction
    {
        public void NotifyStatus(string message)
        {
            AnsiConsole.MarkupLine(message);
        }

        public void NotifyError(string message)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        public void NotifyWarnings(string selectedOption, List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[yellow]Stored Procedure 수집 중 일부 데이터 누락 또는 접근 실패가 감지되었습니다. AI 분석 프롬프트에는 포함되나, 결과물이 불완전할 수 있습니다:[/]");
            sb.AppendLine();
            foreach (var warn in warnings)
            {
                sb.AppendLine($"[grey]- {Markup.Escape(warn)}[/]");
            }

            var panel = new Panel(new Markup(sb.ToString().TrimEnd()))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader($"[yellow] 경고: {Markup.Escape(selectedOption)} 수집 정보 누락 ([bold]{warnings.Count}[/]) [/]"),
                BorderStyle = new Style(Color.Yellow)
            };

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }

        public void NotifyL1Errors(string selectedOption, int attempt, int maxAttempts, List<string> errors)
        {
            var maxStr = maxAttempts == -1 ? "검증 완료까지" : maxAttempts.ToString();
            AnsiConsole.MarkupLine($"[yellow]{selectedOption} - [[L1 기계 검증]] 문법/구조 오류 발견 (시도 {attempt}/{maxStr}):[/]");
            foreach (var err in errors)
            {
                AnsiConsole.MarkupLine($"  [red]=> {Markup.Escape(err)}[/]");
            }
        }

        public void NotifyL2Defects(string selectedOption, int attempt, int maxAttempts, string feedbackComment)
        {
            var maxStr = maxAttempts == -1 ? "검증 완료까지" : maxAttempts.ToString();
            AnsiConsole.MarkupLine($"[yellow]{selectedOption} - [[L2 AI 리뷰]] 결함 및 보완 권고 발견 (시도 {attempt}/{maxStr}):[/]");
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

        public Task<bool> ConfirmMetadataSyncAsync(string selectedOption)
        {
            var result = AnsiConsole.Confirm($"[bold yellow]{selectedOption}[/] - AI가 보완한 설명(Extended Properties) 목록을 실제 데이터베이스에 동기화(Sync)하시겠습니까?", false);
            return Task.FromResult(result);
        }
    }
}
