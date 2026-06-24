using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectre.Console;
using ReSet.Validator.Core.Abstractions;
using ReSet.Validator.Core.Models;
using ValidationResult = ReSet.Validator.Core.Models.ValidationResult;

namespace ReSet.Validator.Cli
{
    public class ConsoleUserInteraction : IValidationUserInterface
    {
        public void ShowL1Result(string specName, L1ValidationResult result)
        {
            var statusStr = result.Passed 
                ? "[green]✅ PASS[/]" 
                : $"[red]❌ FAIL[/] ({Markup.Escape(result.ErrorMessage)})";

            AnsiConsole.MarkupLine($"[bold]Level 1 정적 검증 결과:[/] {statusStr}");
            if (result.Passed)
            {
                AnsiConsole.MarkupLine($"  - 매핑된 클래스/메소드명: [cyan]{Markup.Escape(result.ClassOrMethodName)}[/]");
                foreach (var kvp in result.ExtractedMetadata)
                {
                    AnsiConsole.MarkupLine($"  - {Markup.Escape(kvp.Key)}: [grey]{Markup.Escape(kvp.Value)}[/]");
                }
            }
        }

        public void ShowL2Result(string specName, GapReport report)
        {
            var statusColor = report.OverallStatus switch
            {
                "MATCH" => "green",
                "PARTIAL" => "yellow",
                _ => "red"
            };

            var panel = new Panel(
                new Markup(
                    $"[bold]종합 일치도:[/] [{statusColor}]{report.OverallStatus}[/]\n\n" +
                    $"[bold]1. 입력 파라미터 Gap:[/] {Markup.Escape(string.IsNullOrEmpty(report.InputParametersGap) ? "일치" : report.InputParametersGap)}\n" +
                    $"[bold]2. 출력 데이터셋/DTO Gap:[/] {Markup.Escape(string.IsNullOrEmpty(report.OutputResultSetsGap) ? "일치" : report.OutputResultSetsGap)}\n" +
                    $"[bold]3. 비즈니스 로직 Gap:[/] {Markup.Escape(string.IsNullOrEmpty(report.BusinessLogicGap) ? "일치" : report.BusinessLogicGap)}\n" +
                    $"[bold]4. 예외/트랜잭션 Gap:[/] {Markup.Escape(string.IsNullOrEmpty(report.ExceptionHandlingGap) ? "일치" : report.ExceptionHandlingGap)}\n\n" +
                    $"[bold yellow]💡 코드 수정 제안 사항 (Suggestions):[/]\n{Markup.Escape(report.Suggestions)}"
                )
            )
            {
                Header = new PanelHeader($"[bold blue]Level 2 AI 논리 검증 결과: {Markup.Escape(specName)}[/]"),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(panel);
        }

        public Task<bool> ConfirmValidationAsync(string specName, string codePath, GapReport? gapReport)
        {
            AnsiConsole.WriteLine();
            var prompt = new ConfirmationPrompt($"[bold yellow]❓ '{Markup.Escape(specName)}'의 코드 구현을 최종 승인(Approve)하시겠습니까?[/]");
            bool confirmed = AnsiConsole.Prompt(prompt);
            return Task.FromResult(confirmed);
        }

        public Task<string> PromptFeedbackAsync(string specName)
        {
            var feedback = AnsiConsole.Ask<string>("[bold red]💬 불승인 사유 및 수정 사항 피드백을 입력해 주세요:[/] ");
            return Task.FromResult(feedback);
        }

        public string PromptDirectoryPath(string promptMessage, string defaultPath, List<string> choices)
        {
            var prompt = new TextPrompt<string>($"[bold green]{Markup.Escape(promptMessage)}[/]")
                .DefaultValue(defaultPath)
                .AddChoices(choices) // 탭 자동완성을 위한 후보군 리스트 등록
                .ShowChoices(false)  // 슬래시로 엮여 지저분하게 출력되는 선택지 화면 노출 방지
                .Validate(path =>
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return Spectre.Console.ValidationResult.Error("[red]경로를 입력해야 합니다.[/]");
                    }
                    var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
                    if (!Directory.Exists(fullPath))
                    {
                        return Spectre.Console.ValidationResult.Error($"[red]입력하신 디렉토리가 존재하지 않습니다: {Markup.Escape(fullPath)}[/]");
                    }
                    return Spectre.Console.ValidationResult.Success();
                });

            var chosen = AnsiConsole.Prompt(prompt);
            return Path.IsPathRooted(chosen) ? chosen : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), chosen));
        }

        public void ShowSummary(List<ValidationResult> results)
        {
            AnsiConsole.WriteLine();
            var table = new Table()
                .Title("[bold white]📋 최종 마일스톤 검증 요약 요약 보고서[/]")
                .Border(TableBorder.Rounded)
                .AddColumn("[bold]검증 대상[/]")
                .AddColumn("[bold]L1 정적 검증[/]")
                .AddColumn("[bold]L2 AI 의미 검증[/]")
                .AddColumn("[bold]L3 개발자 승인[/]")
                .AddColumn("[bold]상태[/]");

            foreach (var r in results)
            {
                var l1 = r.L1Passed ? "[green]✅ PASS[/]" : "[red]❌ FAIL[/]";
                var l2 = r.L2Passed ? "[green]✅ MATCH[/]" : "[yellow]⚠️ GAP[/]";
                var l3 = r.IsApproved ? "[green]✅ APPROVED[/]" : "[red]❌ REJECTED[/]";
                
                var displayStatus = r.IsApproved 
                    ? "[green]Approved[/]" 
                    : (r.L1Passed ? "[yellow]Needs Modification[/]" : "[red]Structure Error[/]");

                table.AddRow(
                    Markup.Escape(r.MappedName),
                    l1,
                    l2,
                    l3,
                    displayStatus
                );
            }

            AnsiConsole.Write(table);
        }

        public void ShowWarning(string message)
        {
            AnsiConsole.MarkupLine($"[bold yellow]⚠️ 경고: {Markup.Escape(message)}[/]");
        }

        public void ShowInfo(string message)
        {
            AnsiConsole.MarkupLine($"[blue]ℹ️ {Markup.Escape(message)}[/]");
        }
    }
}
