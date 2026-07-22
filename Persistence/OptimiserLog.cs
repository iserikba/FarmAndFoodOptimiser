using System;
using System.Collections.Generic;

namespace Iserik.FaFOptimiser.Persistence
{
    /// <summary>
    /// Centralized logging system for the Optimiser.
    /// Stores recent logs and notifies the UI when updates occur.
    /// </summary>
    public class OptimiserLog
    {
        private readonly List<string> _logLines = new List<string>();
        private const int MaxLines = 150; // Keep memory footprint small

        // Event triggered whenever a new log is added
        public event Action<string> OnLogAdded;

        public void AddMessage(string message)
        {
#if !PUBLIC_BUILD
            // Format with a timestamp
            string formattedMsg = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logLines.Add(formattedMsg);

            // Prevent the log from growing indefinitely
            if (_logLines.Count > MaxLines)
            {
                _logLines.RemoveAt(0);
            }

            // Notify any listeners (like our UI window)
            OnLogAdded?.Invoke(formattedMsg);
#endif
        }

        public string GetAllLogs()
        {
            return string.Join("\n", _logLines);
        }

        public void Clear()
        {

            _logLines.Clear();
            OnLogAdded?.Invoke(string.Empty);
        }
    }
}