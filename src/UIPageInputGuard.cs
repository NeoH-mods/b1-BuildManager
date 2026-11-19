using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnrealEngine.SlateCore;

namespace BuildManager
{
    /// <summary>
    /// 配装面板打开时屏蔽页面级的方向键/摇杆输入。
    /// 游戏本体用同一只手柄驱动背景装备页的焦点，不拦住会导致面板里移动时背景选择也跟着动。
    /// 只拦 GSUIPage 的按键/摇杆实现，鼠标（Slate）不受影响。
    /// </summary>
    [HarmonyPatch]
    internal static class UIPageNavBlockPatch
    {
        private static bool _logged;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = Type.GetType("GSE.GSUI.GSUIPage, B1UI_GSE.Script");
            if (type == null) yield break;

            string[] names = { "OnUIKeyDownImpl", "OnUIAnalogValueChangedImpl", "OnUIPreviewKeyDownImpl" };
            foreach (string name in names)
            {
                MethodBase method = type.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null) yield return method;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(MethodBase __originalMethod, object[] __args)
        {
            if (!PresetPanel.IsOpen) return true;

            // 重命名时焦点在输入框里，方向键可能先被输入框吃掉；页面预览阶段先转给重命名导航。
            if (PresetPanel.IsModalOpen && __args != null && __args.Length > 1)
            {
                int direction = -1;
                bool repeat = false;
                try
                {
                    FKeyEvent keyEvent = __args[1] as FKeyEvent;
                    if (keyEvent != null)
                    {
                        direction = UIGamepadInput.ToDirection(
                            b1.UGSE_UINavigationFuncLib.GetNavigationDirectionFromKey(keyEvent));
                        repeat = b1.UGSE_UINavigationFuncLib.IsRepeatAction(keyEvent);
                    }
                    else
                    {
                        FAnalogInputEvent analog = __args[1] as FAnalogInputEvent;
                        if (analog != null)
                        {
                            direction = UIGamepadInput.ToDirection(
                                b1.UGSE_UINavigationFuncLib.GetNavigationDirectionFromAnalogForOP(analog));
                            repeat = true;
                        }
                    }
                }
                catch { }

                if (direction >= 0) PresetPanel.NavigateRenameFromNative(direction, repeat);
            }

            if (!_logged)
            {
                _logged = true;
                Log.Info("[BuildManager] page input blocked while panel open: " + __originalMethod.Name);
            }
            return false;
        }
    }

    /// <summary>
    /// 手柄 A 确认：面板打开时拦住背景装备页所有 BUI_Button 的 ButtonPress 激活，
    /// 避免手柄 A 继续操作“精魄”等原生槽位（甚至打开精魄选择页）。
    /// 配装面板自己的按键是普通 UButton / XInput 轮询，不受影响。
    /// </summary>
    [HarmonyPatch]
    internal static class NativeButtonActivationBlockPatch
    {
        private static bool _logged;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = Type.GetType("b1.UI.Comm.BUI_Button, BtlSvr.Main");
            if (type == null) yield break;
            MethodBase method = type.GetMethod("OnButtonPress",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null) yield return method;
        }

        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (!PresetPanel.IsOpen) return true;
            __result = false;
            if (!_logged)
            {
                _logged = true;
                Log.Info("[BuildManager] native button press blocked while panel open");
            }
            return false;
        }
    }

    /// <summary>
    /// Back（手柄 B / 键盘 Esc）：面板打开时只关闭面板，并吞掉这次 Back，
    /// 避免游戏把装备页一起退回上一层。
    /// </summary>
    [HarmonyPatch]
    internal static class EquipPageBackPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type equip = Type.GetType("B1UI.GSUI.UIEquipMain, B1UI_GSE.Script");
            if (equip != null)
            {
                MethodBase method = equip.GetMethod("DoUIActionBack",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null) yield return method;
            }

            Type page = Type.GetType("GSE.GSUI.GSUIPage, B1UI_GSE.Script");
            if (page != null)
            {
                MethodBase method = page.GetMethod("DoUIBackingImpl",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null) yield return method;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            try
            {
                // 只处理装备页自己的 Back，其它页面保持原逻辑。
                if (!UIEquipPageHook.IsCurrentView(__instance as GSE.GSUI.GSUIView)) return true;

                if (PresetPanel.IsOpen)
                {
                    PresetPanel.CloseFromGameBack();
                    return false;
                }

                // 手柄 B 已由 XInput 关掉面板；同一次按键随后到达的 Back 不再退出装备页。
                return !PresetPanel.BackGuardActive;
            }
            catch
            {
                return true;
            }
        }
    }
}
