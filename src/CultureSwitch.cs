using System;
using System.Collections.Generic;
using CSharpModBase;

namespace BuildManager
{
    /// <summary>
    /// 临时切换游戏本地化语言：用于在“游戏语言 A、MOD 语言 B”时生成 B 语言的物品快照。
    /// 用完必须 Restore，否则整个游戏 UI 会停在 B 语言。
    /// </summary>
    internal static class CultureSwitch
    {
        /// <summary>游戏当前原始 culture（用于原样恢复）。</summary>
        public static string RawCurrent()
        {
            try
            {
                string value = b1.Localization.GSLocalization.GetCurrentCulture();
                if (!string.IsNullOrEmpty(value)) return value;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] GetCurrentCulture failed: " + ex.Message);
            }

            try { return L.GameCode; }
            catch { return "en"; }
        }

        /// <summary>游戏支持的 culture 列表（Gridly），用于把 "ko" 映射成 "ko-KR" 之类的完整名。</summary>
        public static List<string> Supported()
        {
            try
            {
                List<string> list = b1.UGSE_GridlyFuncLib.GetSupportCultures();
                if (list != null && list.Count > 0) return list;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] GetSupportCultures failed: " + ex.Message);
            }
            return null;
        }

        public static string MapToSupported(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;

            List<string> list = Supported();
            if (list == null) return code;

            foreach (string c in list)
            {
                if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) return c;
            }
            foreach (string c in list)
            {
                if (!string.IsNullOrEmpty(c) &&
                    (c.StartsWith(code, StringComparison.OrdinalIgnoreCase) ||
                     code.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
                    return c;
            }
            return code;
        }

        public static bool Set(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return false;

            string mapped = MapToSupported(culture);
            bool engineOk = false;

            try
            {
                engineOk = UnrealEngine.Engine.UInternationalizationLibrary.SetCurrentCulture(mapped, false);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] engine SetCurrentCulture failed: " + ex.Message);
            }

            try { b1.Localization.GSLocalization.SetCurrentCulture(mapped); }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] GSLocalization SetCurrentCulture failed: " + ex.Message);
            }

            string now = null;
            try { now = b1.Localization.GSLocalization.GetCurrentCulture(); }
            catch { }

            Log.Info("[BuildManager] culture set: request=" + culture + " mapped=" + mapped +
                     " engineOk=" + engineOk + " now=" + (now ?? "?"));

            return engineOk;
        }
    }
}
