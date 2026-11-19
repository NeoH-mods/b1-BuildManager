using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CSharpModBase;

namespace BuildManager
{
    /// <summary>
    /// 极简多语言表。
    /// 文本来源优先级：外部 lang/&lt;code&gt;.json &gt; 内嵌资源 &gt; 英文内嵌回退 &gt; key 本身。
    /// 语言来源优先级：玩家在设置里手动选择 &gt; 游戏当前 culture &gt; 英语。
    /// </summary>
    internal static class L
    {
        public const string Auto = "auto";

        /// <summary>支持的语言（顺序即设置面板里的显示顺序）。</summary>
        public static readonly string[] Supported = { "zh-Hans", "zh-Hant", "en", "ja", "ko" };

        private const string FallbackCode = "en";

        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static string _override = Auto;
        private static string _current = FallbackCode;
        private static string _gameCode = FallbackCode;
        private static string _lastRawCulture;
        private static string _lastRawLanguage;
        private static bool _initialized;

        /// <summary>语言生效结果发生变化时触发（UI 用它来刷新文案）。</summary>
        public static event Action Changed;

        public static string OverrideCode { get { return _override; } }
        public static string CurrentCode { get { return _current; } }

        /// <summary>游戏自身语言（忽略玩家覆盖），用于导出分语言数据。</summary>
        public static string GameCode { get { return _gameCode; } }

        public static bool IsSupported(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            for (int i = 0; i < Supported.Length; i++)
            {
                if (string.Equals(Supported[i], code, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try { _override = ModSettings.Language; }
            catch (Exception ex) { Log.Warn("[BuildManager] locale override load failed: " + ex.Message); }

            if (!IsSupported(_override) && _override != Auto) _override = Auto;
            Reload();
        }

        /// <summary>设置面板点击语言：写入配置并立即生效。</summary>
        public static void SetOverride(string code)
        {
            if (code != Auto && !IsSupported(code)) code = Auto;
            if (string.Equals(_override, code, StringComparison.OrdinalIgnoreCase)) return;

            _override = code;
            try { ModSettings.Language = code; }
            catch (Exception ex) { Log.Warn("[BuildManager] locale save failed: " + ex.Message); }

            Reload();
            Log.Info("[BuildManager] locale override -> " + code + " (resolved " + _current + ")");
        }

        /// <summary>重新解析当前语言并加载文本表。</summary>
        public static void Reload()
        {
            _gameCode = ResolveGame();

            string resolved = (_override != Auto && IsSupported(_override))
                ? Normalize(_override)
                : _gameCode;

            if (!Tables.ContainsKey(resolved)) LoadTable(resolved);
            if (!Tables.ContainsKey(FallbackCode)) LoadTable(FallbackCode);
            _current = resolved;

            Log.Info("[BuildManager] locale=" + _current + " game=" + (_lastRawCulture ?? "<null>") +
                     " language=" + (_lastRawLanguage ?? "<null>") + " override=" + _override);

            Action handler = Changed;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception ex) { Log.Warn("[BuildManager] locale changed handler failed: " + ex.Message); }
            }
        }

        /// <summary>
        /// 玩家在游戏里切换语言后，自动模式要跟着变。
        /// 面板每次打开时调一次（只在 override=auto 时生效）。
        /// </summary>
        public static void RefreshGameLanguage()
        {
            if (!_initialized) { Initialize(); return; }
            if (_override != Auto) return;

            string next = ResolveGame();
            if (string.Equals(next, _current, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(next, _gameCode, StringComparison.OrdinalIgnoreCase)) return;
            Reload();
        }

        private static string ResolveGame()
        {
            string rawCulture = GetGameCulture();
            string rawLanguage = GetGameLanguage();
            _lastRawCulture = rawCulture;
            _lastRawLanguage = rawLanguage;

            string fromCulture = Normalize(rawCulture);
            string fromLanguage = Normalize(rawLanguage);

            // 游戏的“语言”和 “culture” 偶尔不同步（改语言后 culture 可能还是旧值）。
            // 语言明确落在我们支持的中/日/韩上时，以语言为准。
            if (fromLanguage != FallbackCode && fromLanguage != fromCulture && IsSupported(fromLanguage))
                return fromLanguage;

            if (IsSupported(fromCulture)) return fromCulture;

            return FallbackCode;
        }

        /// <summary>把游戏/系统 culture 归一到我们支持的五个代码。</summary>
        public static string Normalize(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return FallbackCode;

            string c = culture.Trim().Replace('_', '-');
            if (c.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                if (c.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    c.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("zh-MO", StringComparison.OrdinalIgnoreCase))
                    return "zh-Hant";
                return "zh-Hans";
            }
            if (c.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (c.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
            if (c.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
            return FallbackCode;
        }

        private static string GetGameCulture()
        {
            string culture = InvokeEngineString("GetCurrentCulture");
            if (!string.IsNullOrEmpty(culture)) return culture;

            try { return CultureInfo.CurrentUICulture.Name; }
            catch { return null; }
        }

        private static string GetGameLanguage()
        {
            return InvokeEngineString("GetCurrentLanguage");
        }

        /// <summary>调用 UE 绑定库里的无参静态方法并取回字符串（找不到就返回 null）。</summary>
        private static string InvokeEngineString(string methodName)
        {
            try
            {
                Type type = FindEngineType();
                if (type == null)
                {
                    Log.Warn("[BuildManager] internationalization library type not found");
                    return null;
                }

                MethodInfo method = type.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method == null) return null;

                object result = method.Invoke(null, null);
                return result as string;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] " + methodName + " failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// UE 的绑定类在不同版本/导出器下命名不一致：
        /// 实测本作是 UnrealEngine.Engine.UInternationalizationLibrary。
        /// 先按已知全名找，找不到再按类型名后缀扫一遍所有已加载程序集。
        /// </summary>
        private static Type FindEngineType()
        {
            string[] candidates =
            {
                "UnrealEngine.Engine.UInternationalizationLibrary",
                "UnrealEngine.Engine.KismetInternationalizationLibrary",
                "UnrealEngine.UInternationalizationLibrary",
                "UnrealEngine.KismetInternationalizationLibrary",
            };

            var assemblies = new List<Assembly>(AppDomain.CurrentDomain.GetAssemblies());

            // Init 阶段绑定程序集可能还没被 CLR 加载，显式拉一次。
            foreach (string name in new[] { "UnrealEngine", "UnrealEngine.Runtime" })
            {
                try
                {
                    Assembly loaded = Assembly.Load(name);
                    if (loaded != null && !assemblies.Contains(loaded)) assemblies.Add(loaded);
                }
                catch { }
            }

            foreach (Assembly assembly in assemblies)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    Type direct = assembly.GetType(candidates[i], false);
                    if (direct != null) return direct;
                }
            }

            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                for (int i = 0; i < types.Length; i++)
                {
                    Type t = types[i];
                    if (t != null && t.Name.EndsWith("InternationalizationLibrary", StringComparison.Ordinal))
                        return t;
                }
            }

            return null;
        }

        // ---------------- 文本表 ----------------

        private static void LoadTable(string code)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            MergeTable(table, ReadEmbedded(code));
            MergeTable(table, ReadExternal(code));
            Tables[code] = table;
        }

        private static void MergeTable(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            if (source == null) return;
            foreach (KeyValuePair<string, string> pair in source)
            {
                if (pair.Key == null || pair.Value == null) continue;
                target[pair.Key] = pair.Value;
            }
        }

        private static Dictionary<string, string> ReadEmbedded(string code)
        {
            try
            {
                Assembly assembly = typeof(L).Assembly;
                string suffix = ".lang." + code + ".json";
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    using (Stream stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null) continue;
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            return ParseTable(reader.ReadToEnd());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] embedded locale read failed (" + code + "): " + ex.Message);
            }
            return null;
        }

        private static Dictionary<string, string> ReadExternal(string code)
        {
            try
            {
                string path = Path.Combine(Path.Combine(ModPaths.ModDir, "lang"), code + ".json");
                if (!File.Exists(path)) return null;
                return ParseTable(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] external locale read failed (" + code + "): " + ex.Message);
                return null;
            }
        }

        private static Dictionary<string, string> ParseTable(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            Dictionary<string, object> root = Json.AsObject(Json.Parse(text));
            if (root == null) return null;

            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in root)
            {
                string value = Json.ToStringValue(pair.Value);
                if (value != null) table[pair.Key] = value;
            }
            return table;
        }

        private static string Lookup(string code, string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (code == null) code = _current;

            if (!Tables.ContainsKey(code)) LoadTable(code);

            Dictionary<string, string> table;
            if (Tables.TryGetValue(code, out table))
            {
                string value;
                if (table.TryGetValue(key, out value) && value != null) return value;
            }
            return null;
        }

        /// <summary>取文本；缺失时回退英文，再缺失时返回 key（便于开发期发现漏翻）。</summary>
        public static string T(string key)
        {
            if (!_initialized) Initialize();
            return Lookup(_current, key)
                ?? Lookup(FallbackCode, key)
                ?? key;
        }

        /// <summary>带占位符的文本：T 里用 {0}/{1}；格式化失败时返回未格式化文本。</summary>
        public static string F(string key, params object[] args)
        {
            string template = T(key);
            if (args == null || args.Length == 0) return template;
            try { return string.Format(template, args); }
            catch { return template; }
        }

        /// <summary>语言自称（设置面板里按母语显示，不随界面语言变化）。</summary>
        public static string DisplayName(string code)
        {
            string text = Lookup(code, "lang." + code);
            return string.IsNullOrEmpty(text) ? code : text;
        }
    }
}
