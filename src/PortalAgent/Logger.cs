using System;
using System.IO;

namespace PortalAgent
{
    public sealed class Logger
    {
        private const long MaxBytes = 5 * 1024 * 1024;
        private readonly object sync = new object();
        private readonly string filePath;

        public Logger(string filePath)
        {
            this.filePath = Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);

            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(this.filePath));
                if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
            }
            catch { }
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
            lock (sync)
            {
                Console.WriteLine(line);
                try
                {
                    FileInfo info = new FileInfo(filePath);
                    if (info.Exists && info.Length >= MaxBytes)
                    {
                        string backup = filePath + ".1";
                        if (File.Exists(backup)) { File.Delete(backup); }
                        File.Move(filePath, backup);
                    }
                    File.AppendAllText(filePath, line + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
