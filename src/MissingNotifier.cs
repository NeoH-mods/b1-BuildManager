using System;
using System.Collections.Generic;
using System.Text;
using B1UI.GSUI;
using CSharpModBase;
using UnrealEngine.Runtime;

namespace BuildManager
{
    /// <summary>
    /// 应用配装时，如果当前存档缺少方案里的道具，用游戏自己的 HUD 提示条告知玩家。
    /// 提示只做展示，不参与换装流程；缺失位置的换装步骤不会入队，所以原装备保持不动。
    /// </summary>
    internal static class MissingNotifier
    {
        private const int MaxNamesInTip = 6;

        public static void ShowMissing(List<string> details)
        {
            if (details == null || details.Count == 0) return;

            string message = BuildMessage(details);
            // 控制台可能是 GBK；日志里只写 ASCII 摘要，中文名单只进 HUD。
            Log.Warn("[BuildManager] missing " + details.Count +
                     " preset items; affected slots/positions left unchanged (see HUD tip for names).");

            try
            {
                // 用 FromString 而不是 Create：同一个 namespace/key 的 FText 会被本地化表缓存，
                // 动态拼接的缺失名单会一直显示第一次的内容。
                GSB1UIUtil.ShowCommTips(
                    FText.FromString(message),
                    ETipsType.WarnTips,
                    false,
                    4.0f,
                    false);
            }
            catch (Exception ex)
            {
                // 提示失败不能影响已经排队的换装步骤。
                Log.Warn("[BuildManager] show missing tip failed: " + ex.Message);
            }
        }

        private static string BuildMessage(List<string> details)
        {
            var builder = new StringBuilder();

            int shown = Math.Min(details.Count, MaxNamesInTip);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) builder.Append(L.T("notify.list_sep"));
                builder.Append(details[i]);
            }
            if (details.Count > shown) builder.Append(L.T("notify.more"));

            return L.F("notify.missing", details.Count, builder.ToString());
        }
    }
}
