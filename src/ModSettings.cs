using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CSharpModBase;

namespace BuildManager
{
    /// <summary>
    /// 玩家级设置（目前只有语言），存到 %APPDATA%\BuildManager\settings.json。
    /// 不放进 presets\、也不放进存档：语言是“人”的偏好，换存档/重装 MOD 目录都不该丢。
    /// </summary>
    internal static class ModSettings
    {
        private static string _language = L.Auto;
        private static bool _loaded;

        public static string FilePath
        {
            get
            {
                string root = null;
                try { root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
                catch { }
                if (string.IsNullOrEmpty(root)) root = ModPaths.ModDir;
                return Path.Combine(Path.Combine(root, "BuildManager"), "settings.json");
            }
        }

        public static string Language
        {
            get
            {
                EnsureLoaded();
                return _language;
            }
            set
            {
                EnsureLoaded();
                string code = string.IsNullOrEmpty(value) ? L.Auto : value;
                if (string.Equals(_language, code, StringComparison.OrdinalIgnoreCase)) return;
                _language = code;
                Save();
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;

                Dictionary<string, object> root = Json.AsObject(
                    Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (root == null) return;

                string value = Json.ToStringValue(Json.Get(root, "language"));
                if (!string.IsNullOrEmpty(value)) _language = value;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] settings load failed: " + ex.Message);
                TryBackupBrokenFile();
            }
        }

        private static void Save()
        {
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string text = "{\n  \"version\": 1,\n  \"language\": " + Json.Escape(_language) + "\n}\n";
                string temp = path + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, null); }
                    catch
                    {
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] settings save failed: " + ex.Message);
            }
        }

        private static void TryBackupBrokenFile()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;
                string backup = path + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(path, backup);
                Log.Warn("[BuildManager] settings file moved to " + backup);
            }
            catch { }
        }
    }
}
