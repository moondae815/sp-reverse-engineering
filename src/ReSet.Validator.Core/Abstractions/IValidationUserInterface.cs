using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ReSet.Validator.Core.Models;

namespace ReSet.Validator.Core.Abstractions
{
    public interface IValidationUserInterface
    {
        void ShowL1Result(string specName, L1ValidationResult result);
        void ShowL2Result(string specName, GapReport report);
        Task<bool> ConfirmValidationAsync(string specName, string codePath, GapReport? gapReport);
        Task<string> PromptFeedbackAsync(string specName);
        string PromptDirectoryPath(string promptMessage, string defaultPath, List<string> choices);
        void ShowSummary(List<ValidationResult> results);
        void ShowWarning(string message);
        void ShowInfo(string message);
    }
}
