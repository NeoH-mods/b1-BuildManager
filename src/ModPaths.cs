using System;
using System.IO;
using System.Reflection;

namespace BuildManager
{
    /// <summary>
    /// 路径解析。注意 Develop=1 时加载器用 Assembly.Load(byte[]) 加载 mod，
    /// 此时 Assembly.Location 为空，不能只依赖它。
    /// </summary>
    internal static class ModPaths
    {
        private const string ModFolderName = "BuildManager";

        private static string _modDir;
        private static string _dataDir;
        private static string _runtimeDir;
        private static string _dumpDir;

        /// <summary>开发标记文件：存在时把 logs/presets/dumps 写到这里，保持 CSharpLoader 干净。</summary>
        private const string DevMarkerName = "BuildManager.dev";

        public static string ModDir
        {
            get
            {
                if (_modDir != null) return _modDir;
                _modDir = ResolveModDir();
                return _modDir;
            }
        }

        /// <summary>
        /// 运行产物根目录：默认就是 mod 目录（发布版行为）。
        /// 开发机上 deploy.ps1 会写 BuildManager.dev 指向 local\artifacts\runtime，
        /// 这样 logs/dumps 不会落进 CSharpLoader；presets 例外，始终放在 DLL 同目录。
        /// </summary>
        public static string RuntimeDir
        {
            get
            {
                if (_runtimeDir != null) return _runtimeDir;
                _runtimeDir = ResolveRuntimeDir();
                return _runtimeDir;
            }
        }

        /// <summary>是否开发模式（存在 BuildManager.dev 标记）：用于只在开发机启用的诊断/生成功能。</summary>
        public static bool IsDev
        {
            get
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string[] markers =
                    {
                        Path.Combine(baseDir ?? ".", DevMarkerName),
                        Path.GetFullPath(Path.Combine(ModDir, "..", "..", "..", DevMarkerName)),
                    };

                    foreach (string marker in markers)
                    {
                        try { if (File.Exists(marker)) return true; }
                        catch { }
                    }
                }
                catch { }
                return false;
            }
        }

        public static string PresetsDir
        {
            get
            {                // 方案是用户数据，固定放在 DLL 同目录的 presets\，不随开发日志重定向。
                string dir = Path.Combine(ModDir, "presets");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string DumpDir
        {
            get
            {
                if (_dumpDir != null) return _dumpDir;
                _dumpDir = Path.Combine(RuntimeDir, "dumps");
                if (!Directory.Exists(_dumpDir)) Directory.CreateDirectory(_dumpDir);
                return _dumpDir;
            }
        }

        /// <summary>
        /// 静态表目录。开发期直接复用 Lua 版的 data（唯一事实源，不复制）。
        /// </summary>
        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;
                _dataDir = ResolveDataDir();
                return _dataDir;
            }
        }

        private static string ResolveModDir()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                {
                    string dir = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string cwd = Directory.GetCurrentDirectory();

            string found = FindModDir(baseDir) ?? FindModDir(cwd);
            if (found != null) return found;

            // 最后兜底：内容根是 Win64 时，exe 目录就是 BaseDirectory
            string guess = Path.Combine(baseDir ?? ".", "CSharpLoader", "Mods", ModFolderName);
            if (Directory.Exists(guess)) return guess;
            guess = Path.Combine(cwd ?? ".", "CSharpLoader", "Mods", ModFolderName);
            if (Directory.Exists(guess)) return guess;

            return Path.Combine(baseDir ?? ".", "CSharpLoader", "Mods", ModFolderName);
        }

        private static string FindModDir(string start)
        {
            if (string.IsNullOrEmpty(start)) return null;
            try
            {
                var dir = new DirectoryInfo(start);
                for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "CSharpLoader", "Mods", ModFolderName);
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            catch { }
            return null;
        }

        private static string ResolveRuntimeDir()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string modDir = ModDir;

                // 标记文件放在 Win64 根（CSharpLoader 外面），避免开发文件混进发布目录。
                string[] markers =
                {
                    Path.Combine(baseDir ?? ".", DevMarkerName),
                    Path.GetFullPath(Path.Combine(modDir, "..", "..", "..", DevMarkerName)),
                };

                foreach (string marker in markers)
                {
                    try
                    {
                        if (!File.Exists(marker)) continue;
                        string line = File.ReadAllText(marker).Trim().Trim('\uFEFF').Trim();
                        if (string.IsNullOrEmpty(line)) continue;

                        string target = Path.IsPathRooted(line)
                            ? line
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(marker) ?? ".", line));

                        Directory.CreateDirectory(target);
                        return target;
                    }
                    catch { }
                }
            }
            catch { }

            return ModDir;
        }

        private static string ResolveDataDir()
        {
            string modDir = ModDir;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string cwd = Directory.GetCurrentDirectory();

            string[] candidates =
            {
                Path.Combine(modDir, "data"),   // 部署脚本复制到 DLL 同目录的 data\
                modDir,                         // 也支持直接放在 DLL 旁边
                Path.Combine(baseDir ?? ".", "ue4ss", "Mods", "BuildManager", "data"),
                Path.Combine(cwd ?? ".", "ue4ss", "Mods", "BuildManager", "data"),
                Path.Combine(modDir, "..", "..", "..", "ue4ss", "Mods", "BuildManager", "data"),
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "EquipDesc.json"))) return Path.GetFullPath(candidate);
            }

            // 从 mod 目录向上逐级找 ue4ss\Mods\BuildManager\data（兼容载入目录与内容根不同）
            try
            {
                var dir = new DirectoryInfo(modDir);
                for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "ue4ss", "Mods", "BuildManager", "data");
                    if (File.Exists(Path.Combine(candidate, "EquipDesc.json"))) return Path.GetFullPath(candidate);
                }
            }
            catch { }

            return Path.GetFullPath(Path.Combine(baseDir ?? ".", "ue4ss", "Mods", "BuildManager", "data"));
        }
    }
}
