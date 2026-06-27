using System;

namespace ReSet.Core.Services
{
    public interface IMultiProgressScope : IDisposable
    {
        void AddTask(string taskName, string description);
        void UpdateTask(string taskName, double value, string? description = null);
        void CompleteTask(string taskName);
        void FailTask(string taskName);
    }
}
