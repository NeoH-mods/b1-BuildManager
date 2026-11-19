using System;
using System.IO;
using System.Text;

namespace BuildManager
{
    /// <summary>
    /// 控制台 + 文件双写。文件名固定为 <modDir>\logs\BuildManager.log，
    /// 每行带时间戳/级别，每次 Init 写会话头，崩溃前的最后一行也已落盘。
    /// 同名 Log 会遮蔽 CSharpModBase.Log，现有 Log.Info/Warn/Error 调用无需改动。
    /// </summary>
    internal static class Log
    {
        private const long MaxFileBytes = 4 * 1024 * 1024;

        private static readonly object Gate = new object();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static string _path;

        public static string FilePath
        {
            get
            {
                lock (Gate)
                {
                    EnsureFile();
                    return _path;
                }
            }
        }

        public static void StartSession(string title)
        {
            lock (Gate)
            {
                EnsureFile();
                // 每次启动游戏清空上一次日志，避免排查时被旧内容干扰。
                try
                {
                    if (File.Exists(_path))
                    {
                        File.Delete(_path);
                        EnsureFile();
                    }
                }
                catch { }

                AppendRaw("================" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                          " session start " + title + " ================");
                AppendRaw("modDir=" + ModPaths.ModDir);
                AppendRaw("dataDir=" + ModPaths.DataDir);
                AppendRaw("runtimeDir=" + ModPaths.RuntimeDir);
            }
        }

        public static void EndSession()
        {
            lock (Gate)
            {
                EnsureFile();
                AppendRaw("================" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                          " session end ================");
            }
        }

        public static void Info(string message)
        {
            Write('I', message, 0);
        }

        public static void Debug(string message)
        {
            Write('D', message, 1);
        }

        public static void Warn(string message)
        {
            Write('W', message, 2);
        }

        public static void Error(string message)
        {
            Write('E', message, 3);
        }

        public static void Error(Exception exception)
        {
            string text = exception == null ? "null exception" : exception.ToString();
            Write('E', text, 4, exception);
        }

        private static void Write(char level, string message, int consoleKind, Exception exception = null)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + (message ?? string.Empty);

            lock (Gate)
            {
                try
                {
                    switch (consoleKind)
                    {
                        case 0:
                            CSharpModBase.Log.Info(message);
                            break;
                        case 1:
                            CSharpModBase.Log.Debug(message);
                            break;
                        case 2:
                            CSharpModBase.Log.Warn(message);
                            break;
                        case 4:
                            if (exception != null) CSharpModBase.Log.Error(exception);
                            else CSharpModBase.Log.Error(message);
                            break;
                        default:
                            CSharpModBase.Log.Error(message);
                            break;
                    }
                }
                catch
                {
                    // 控制台不可用时不能影响文件日志
                }

                EnsureFile();
                AppendRaw(line);
            }
        }

        private static void EnsureFile()
        {
            if (_path != null) return;

            try
            {
                string dir = Path.Combine(ModPaths.RuntimeDir, "logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "BuildManager.log");

                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxFileBytes)
                    {
                        string backup = path + ".1";
                        try
                        {
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(path, backup);
                        }
                        catch
                        {
                            // 轮转失败就继续追加
                        }
                    }
                }

                _path = path;
            }
            catch
            {
                _path = null;
            }
        }

        private static void AppendRaw(string line)
        {
            if (_path == null) return;
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Utf8);
            }
            catch
            {
                // 日志写失败绝不影响游戏
            }
        }
    }
}
