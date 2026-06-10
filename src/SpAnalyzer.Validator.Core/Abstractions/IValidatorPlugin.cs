using System;
using System.Threading.Tasks;

namespace SpAnalyzer.Validator.Core.Abstractions
{
    public interface IValidatorPlugin
    {
        string SupportedLanguage { get; }
        
        /// <summary>
        /// 설계서 내용과 실제 소스코드 구조를 정적으로 분석하여 매핑 정합성 검사
        /// </summary>
        Task<L1ValidationResult> ValidateStaticAsync(string specContent, string sourceCodeContent);
    }
}
