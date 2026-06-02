using System;
using System.Collections.Generic;
using Xunit;
using SpAnalyzer.Core.Services;

namespace SpAnalyzer.Core.Tests
{
    public class MechanicalValidatorTests
    {
        private readonly MechanicalValidator _validator = new();

        [Fact]
        public void Validate_WithValidMarkdown_ShouldReturnTrue()
        {
            var validMarkdown = @"
# SP 명세서
## 개요
이 프로시저는 사용자를 조회합니다.

## 파라미터 목록
| 이름 | 타입 | 설명 |
| :--- | :--- | :--- |
| @UserId | INT | 사용자 ID |

## CRUD 분석
| 테이블 | CRUD |
| :--- | :---: |
| dbo.Users | R |

## 로직 흐름 요약
1. 사용자를 조회합니다.

## 비즈니스 흐름 시각화 (Mermaid Diagram)
```mermaid
graph TD
    A[""시작""] --> B[""조회""]
```
";
            var result = _validator.Validate(validMarkdown);

            Assert.True(result.IsValid, "Validation failed with errors: " + string.Join(", ", result.Errors));
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void Validate_WithMissingHeaders_ShouldReturnFalse()
        {
            var invalidMarkdown = @"
# SP 명세서
## 개요
이 프로시저는 사용자를 조회합니다.
";
            var result = _validator.Validate(invalidMarkdown);

            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
            Assert.Contains("## 파라미터 목록", result.SuggestedPromptFix);
        }

        [Fact]
        public void Validate_WithInvalidMermaidBrackets_ShouldReturnFalse()
        {
            var invalidMarkdown = @"
# SP 명세서
## 개요
## 파라미터 목록
## CRUD 분석
## 로직 흐름 요약
## 비즈니스 흐름 시각화 (Mermaid Diagram)
```mermaid
graph TD
    A[시작 (사용자ID)] --> B[종료]
```
";
            var result = _validator.Validate(invalidMarkdown);

            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
            Assert.Contains("큰따옴표", result.SuggestedPromptFix);
        }
    }
}
