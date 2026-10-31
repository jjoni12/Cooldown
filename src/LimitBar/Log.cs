using System;
using System.IO;

namespace LimitBar
{
    static class Log
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimitBar");
        static readonly string File = Path.Combine(Dir, "log.txt");
        static readonly object Gate = new object();
        const long MaxBytes = 512 * 1024;

        public static void Info(string area, string message) => Write("INFO ", area, message);
        public static void Error(string area, string message) => Write("ERROR", area, message);
        public static void Error(string area, Exception ex) => Write("ERROR", area, ex.ToString());

        static void Write(string level, string area, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    var info = new FileInfo(File);
                    if (info.Exists && info.Length > MaxBytes) info.Delete();
                    System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} [{area}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }
    }
}
