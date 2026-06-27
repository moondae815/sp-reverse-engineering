using System;

namespace ReSet.Core.Services
{
    public class NullProgressScope : IMultiProgressScope
    {
        public static readonly IMultiProgressScope Instance = new NullProgressScope();

        private NullProgressScope() { }

        public void AddTask(string taskName, string description) { }

        public void UpdateTask(string taskName, double value, string? description = null) { }

        public void CompleteTask(string taskName) { }

        public void FailTask(string taskName) { }

        public void Dispose() { }
    }
}
