using System;
using System.Diagnostics;
using System.IO;

namespace MesProj.Infrastructure
{
    public static class AppLogger
    {
        private static readonly object SyncRoot = new object();

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            var line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2} {3}",
                DateTime.Now,
                level,
                message,
                exception == null ? string.Empty : exception.ToString());

            Debug.WriteLine(line);

            try
            {
                lock (SyncRoot)
                {
                    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MesProj.log");
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                Debug.WriteLine("Failed to write log file.");
            }
        }
    }
}
