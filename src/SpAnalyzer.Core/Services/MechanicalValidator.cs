using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SpAnalyzer.Core.Services
{
    public class MechanicalValidator
    {
        private static readonly string[] RequiredHeaders = new[]
        {
            "## 개요",
            "## 파라미터 목록",
            "## CRUD 분석",
            "## 로직 흐름 요약",
            "## 비즈니스 흐름 시각화"
        };

        public ValidationResult Validate(string markdown)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                result.IsValid = false;
                result.Errors.Add("명세서 내용이 비어있습니다.");
                return result;
            }

            try
            {
                // 1. 필수 헤더 검증
                foreach (var header in RequiredHeaders)
                {
                    if (!markdown.Contains(header))
                    {
                        result.Errors.Add($"필수 섹션 헤더 '{header}'가 누락되었습니다.");
                    }
                }

                // 2. Mermaid 문법 간이 검증
                var mermaidRegex = new Regex(@"```mermaid\r?\n(.*?)\r?\n```", RegexOptions.Singleline);
                var matches = mermaidRegex.Matches(markdown);

                foreach (Match match in matches)
                {
                    var mermaidContent = match.Groups[1].Value;
                    var lines = mermaidContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("%%")) continue;

                        // 노드 정의 식별 정규식 (대시가 화살표와 겹치는 문제를 방지하기 위해 ID 패턴에서 대시를 제외)
                        var nodeRegex = new Regex(@"([a-zA-Z0-9_]+)([\[\(\{>])(.*?)([\]\)\}>])");
                        var nodeMatches = nodeRegex.Matches(trimmedLine);

                        foreach (Match nodeMatch in nodeMatches)
                        {
                            var nodeId = nodeMatch.Groups[1].Value;
                            var labelText = nodeMatch.Groups[3].Value.Trim();

                            // 예약어 제외
                            if (nodeId.Equals("graph", StringComparison.OrdinalIgnoreCase) ||
                                nodeId.Equals("flowchart", StringComparison.OrdinalIgnoreCase) ||
                                nodeId.Equals("subgraph", StringComparison.OrdinalIgnoreCase) ||
                                nodeId.Equals("end", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // 괄호나 특수문자가 들어있는데 큰따옴표로 감싸지지 않은 구조 검출
                            if (labelText.Contains("(") || labelText.Contains(")") || 
                                labelText.Contains("[") || labelText.Contains("]") ||
                                labelText.Contains("{") || labelText.Contains("}") ||
                                labelText.Contains(",") || labelText.Contains("'") ||
                                labelText.Contains(":") || labelText.Contains("-"))
                            {
                                if (!(labelText.StartsWith("\"") && labelText.EndsWith("\"")))
                                {
                                    result.Errors.Add($"Mermaid 다이어그램 내 노드 '{nodeId}'의 텍스트 '{labelText}'에 괄호나 특수문자가 포함되어 있으나 큰따옴표(\"\")로 감싸지지 않았습니다. 문법 오류를 막기 위해 '\"{labelText}\"' 형태로 큰따옴표를 감싸서 출력해 주십시오.");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 소프트 페일 처리 (검증기 자체 오류 시 툴 중단 방지)
                result.Errors.Clear();
                result.IsValid = true;
                return result;
            }

            result.IsValid = (result.Errors.Count == 0);
            return result;
        }

        private static readonly string[] RequiredConsolidatedHeaders = new[]
        {
            "## 통합 배치 아키텍처 개요",
            "## Mermaid 기반 통합 흐름도",
            "## 단계별 이행 상세 및 의사코드",
            "## 통합 데이터 정합성 검증 SQL 세트"
        };

        public ValidationResult ValidateConsolidated(string markdown)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                result.IsValid = false;
                result.Errors.Add("계획서 내용이 비어있습니다.");
                return result;
            }

            try
            {
                // 1. 필수 헤더 검증
                foreach (var header in RequiredConsolidatedHeaders)
                {
                    if (!markdown.Contains(header))
                    {
                        result.Errors.Add($"필수 섹션 헤더 '{header}'가 누락되었습니다.");
                    }
                }

                // 2. Mermaid 문법 간이 검증
                var mermaidRegex = new Regex(@"```mermaid\r?\n(.*?)\r?\n```", RegexOptions.Singleline);
                var matches = mermaidRegex.Matches(markdown);

                foreach (Match match in matches)
                {
                    var mermaidContent = match.Groups[1].Value;
                    var lines = mermaidContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("%%")) continue;

                        var nodeRegex = new Regex(@"([a-zA-Z0-9_]+)([\[\(\{>])(.*?)([\]\)\}>])");
                        var nodeMatches = nodeRegex.Matches(trimmedLine);

                        foreach (Match nodeMatch in nodeMatches)
                        {
                            var nodeId = nodeMatch.Groups[1].Value;
                            var labelText = nodeMatch.Groups[3].Value.Trim();

                            if (nodeId.Equals("graph", StringComparison.OrdinalIgnoreCase) ||
                                nodeId.Equals("flowchart", StringComparison.OrdinalIgnoreCase) ||
                                nodeId.Equals("subgraph", StringComparison.OrdinalIgnoreCase) ||
                                nodeId.Equals("end", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (labelText.Contains("(") || labelText.Contains(")") || 
                                labelText.Contains("[") || labelText.Contains("]") ||
                                labelText.Contains("{") || labelText.Contains("}") ||
                                labelText.Contains(",") || labelText.Contains("'") ||
                                labelText.Contains(":") || labelText.Contains("-"))
                            {
                                if (!(labelText.StartsWith("\"") && labelText.EndsWith("\"")))
                                {
                                    result.Errors.Add($"Mermaid 다이어그램 내 노드 '{nodeId}'의 텍스트 '{labelText}'에 괄호나 특수문자가 포함되어 있으나 큰따옴표(\"\")로 감싸지지 않았습니다. 문법 오류를 막기 위해 '\"{labelText}\"' 형태로 큰따옴표를 감싸서 출력해 주십시오.");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                result.Errors.Clear();
                result.IsValid = true;
                return result;
            }

            result.IsValid = (result.Errors.Count == 0);
            return result;
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public string? SuggestedPromptFix => IsValid ? null : "[L1 기계 검사 피드백]: 다음 명세서 포맷 및 Mermaid 오류를 반영하여 전면 수정해서 다시 출력해 주세요:\n" + string.Join("\n", Errors);
    }
}
