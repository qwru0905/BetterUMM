using System;
using System.IO;
using System.Text;
using System.Windows;

namespace BetterUMM.Services
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        None = 4
    }

    public static class LoggerService
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", $"log_{DateTime.Now:yyyyMMdd}.txt");
        private static readonly object _lock = new object();

        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        static LoggerService()
        {
            try
            {
                var logDir = Path.GetDirectoryName(LogFilePath);
                if (logDir != null && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
            }
            catch
            {
                // If we can't create the log directory, we'll just fail silently or log to console
            }
        }

        public static void Debug(string message) => Log(message, LogLevel.Debug);
        public static void Info(string message) => Log(message, LogLevel.Info);
        public static void Warning(string message) => Log(message, LogLevel.Warning);
        public static void Error(string message) => Log(message, LogLevel.Error);

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (level < MinimumLevel) return;

            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level.ToString().ToUpper()}] {message}";
            
            // Console output
            Console.WriteLine(logEntry);
            System.Diagnostics.Debug.WriteLine(logEntry);

            // File output
            WriteToFile(logEntry);
        }

        public static void LogException(Exception ex, string context = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- EXCEPTION ---");
            if (!string.IsNullOrEmpty(context))
            {
                sb.AppendLine($"Context: {context}");
            }
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"StackTrace: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                sb.AppendLine($"Inner Exception: {ex.InnerException.Message}");
                sb.AppendLine($"Inner StackTrace: {ex.InnerException.StackTrace}");
            }
            sb.AppendLine($"-----------------");

            Log(sb.ToString(), LogLevel.Error);
        }

        private static void WriteToFile(string entry)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, entry + Environment.NewLine);
                }
            }
            catch
            {
                // Best effort
            }
        }
    }
}
