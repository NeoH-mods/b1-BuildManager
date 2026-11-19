using System;
using System.Collections.Generic;
using HarmonyLib;
using UnrealEngine.Plugins.EnhancedInput;
using UnrealEngine.SlateCore;
using UnrealEngine.UMG;

namespace BuildManager
{
    /// <summary>
    /// 手柄输入改走游戏自己的 UI 输入管线，不再依赖 XInput 轮询：
    /// A/确认来自 FInputWidgetTracker 的 ButtonPress/Confirm 动作；
    /// 方向键/左摇杆来自焦点 BUI_Button 的 Slate 按键与模拟量事件。
    /// 这样任何游戏能识别的手柄（含 Steam Input / DualSense）都有效。
    /// </summary>
    internal static class UIGamepadInput
    {
        private static bool _installed;
        private static string _pressName;
        private static string _releaseName;
        private static string _confirmName;
        private static string _backName;
        private static string _tipsName;
        private static string _sortName;
        private static string _compareName;
        private static string _storyName;
        private static string _switchPageName;
        private static string _upName;
        private static string _downName;
        private static string _leftName;
        private static string _rightName;
        private static readonly HashSet<string> _loggedPanelActions = new HashSet<string>();

        public static bool Installed { get { return _installed; } }

        /// <summary>PatchAll 成功、输入补丁生效后调用；此时停用 XInput 轮询，避免同一次按键触发两遍。</summary>
        public static void MarkInstalled()
        {
            _installed = true;
            try
            {
                _pressName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.ButtonPress);
                _releaseName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.ButtonRelease);
                _confirmName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Confirm);
                _backName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Back);
                _tipsName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Tips);
                _sortName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Sort);
                _compareName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Compare);
                _storyName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Story);
                _switchPageName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.SwitchPage);
                _upName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Up);
                _downName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Down);
                _leftName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Left);
                _rightName = b1.ActionNameFuncLib.GetName(b1.GSUIActionEn.Right);
                Log.Info("[BuildManager] game ui actions: press=" + Safe(_pressName) +
                         " release=" + Safe(_releaseName) +
                         " confirm=" + Safe(_confirmName) +
                         " back=" + Safe(_backName) +
                         " tips=" + Safe(_tipsName) +
                         " sort=" + Safe(_sortName) +
                         " compare=" + Safe(_compareName) +
                         " story=" + Safe(_storyName) +
                         " switchPage=" + Safe(_switchPageName) +
                         " up=" + Safe(_upName) +
                         " down=" + Safe(_downName) +
                         " left=" + Safe(_leftName) +
                         " right=" + Safe(_rightName));
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] read game ui action names failed: " + ex.Message);
            }
        }

        public static bool IsPress(string action)
        {
            return NameIs(action, _pressName) || NameIs(action, _confirmName);
        }

        public static bool IsRelease(string action)
        {
            return NameIs(action, _releaseName);
        }

        public static bool IsBack(string action)
        {
            return NameIs(action, _backName);
        }

        /// <summary>
        /// 手柄 Y 在装备页实际触发 Story / SwitchPage（日志实测），另外保留
        /// Tips/Sort/Compare 以兼容不同按键绑定。面板打开时这些都视为“重命名”，
        /// 并吞掉事件，避免 Y 穿透到背景装备页。
        /// </summary>
        public static bool IsRenameAction(string action)
        {
            return NameIs(action, _tipsName)
                || NameIs(action, _sortName)
                || NameIs(action, _compareName)
                || NameIs(action, _storyName)
                || NameIs(action, _switchPageName);
        }

        /// <summary>把 UI 方向动作名转成 0=上 1=下 2=左 3=右；不是方向返回 false。</summary>
        public static bool TryGetDirection(string action, out int direction)
        {
            direction = -1;
            if (NameIs(action, _upName)) { direction = 0; return true; }
            if (NameIs(action, _downName)) { direction = 1; return true; }
            if (NameIs(action, _leftName)) { direction = 2; return true; }
            if (NameIs(action, _rightName)) { direction = 3; return true; }
            return false;
        }

        /// <summary>诊断用：记录面板打开时出现过的不同 UI 动作，避免每帧刷屏。</summary>
        public static void LogPanelAction(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (!_loggedPanelActions.Add(action)) return;
            Log.Info("[BuildManager] panel ui action: " + action);
        }

        /// <summary>EUINavigation 转面板方向：0=上 1=下 2=左 3=右，-1=不是方向。</summary>
        public static int ToDirection(EUINavigation navigation)
        {
            switch (navigation)
            {
                case EUINavigation.Up: return 0;
                case EUINavigation.Down: return 1;
                case EUINavigation.Left: return 2;
                case EUINavigation.Right: return 3;
                default: return -1;
            }
        }

        private static bool NameIs(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && left == right;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "<null>" : value;
        }
    }

    /// <summary>
    /// 确认键：面板打开时拦截 ButtonPress/Confirm（以及用来挡住原生按钮的 ButtonRelease），
    /// 面板关闭时只在焦点位于“配装”入口时打开面板。返回 false 表示吞掉这次 UI 动作。
    /// </summary>
    [HarmonyPatch(typeof(b1.FInputWidgetTracker), "InputActionTrigger")]
    internal static class UIActionTrackerPatch
    {
        private static bool _loggedOpen;

        private static bool Prefix(string ActionName, ETriggerEvent TriggerEvent)
        {
            if (!UIGamepadInput.Installed) return true;

            bool pressed = TriggerEvent == ETriggerEvent.Started || TriggerEvent == ETriggerEvent.Triggered;
            bool released = TriggerEvent == ETriggerEvent.Completed || TriggerEvent == ETriggerEvent.Canceled;

            if (PresetPanel.IsOpen)
            {
                PresetPanel.NotifyGamepadInput();
                UIGamepadInput.LogPanelAction(ActionName);

                // 重命名/设置窗口打开时，方向动作在弹窗控件之间导航。
                if (PresetPanel.IsModalOpen)
                {
                    int renameDir;
                    if (UIGamepadInput.TryGetDirection(ActionName, out renameDir))
                    {
                        if (pressed) PresetPanel.NavigateRenameFromNative(renameDir, false);
                        return false;
                    }
                }

                if (UIGamepadInput.IsPress(ActionName))
                {
                    if (released) return false;
                    if (!pressed) return true;
                    PresetPanel.Confirm();
                    return false;
                }

                if (UIGamepadInput.IsRelease(ActionName)) return false;
                if (UIGamepadInput.IsBack(ActionName))
                {
                    if (!pressed) return true;
                    PresetPanel.BackKey();
                    return false;
                }

                // Y：打开重命名。按下/抬起/持续事件全部吞掉，否则 Tips/Sort/Compare
                // 会落到背景的原始装备页，表现为“按 Y 操作了背后的页面”。
                if (UIGamepadInput.IsRenameAction(ActionName))
                {
                    if (pressed) PresetPanel.RenameFromGamepad();
                    return false;
                }

                return true;
            }

            if (pressed && UIGamepadInput.IsPress(ActionName) && UIEquipPageHook.IsEntryFocused())
            {
                if (!_loggedOpen)
                {
                    _loggedOpen = true;
                    Log.Info("[BuildManager] gamepad confirm on entry (game ui action)");
                }
                UIEquipPageHook.OnGamepadConfirm();
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 方向键（D-pad / 键盘方向键）：焦点 BUI_Button 的 Slate 按键事件里取方向，
    /// 面板打开时转给面板并标记为已处理，背景装备页的焦点就不再跟着移动。
    /// </summary>
    [HarmonyPatch(typeof(b1.UI.Comm.BUI_Button), "OnKeyDown_Implementation")]
    internal static class BUIButtonKeyNavPatch
    {
        private static bool Prefix(FKeyEvent InKeyEvent, ref FEventReply __result)
        {
            if (!UIGamepadInput.Installed || !PresetPanel.IsOpen) return true;

            // 手柄 Y 的 Slate 键事件也要在面板打开时吞掉（UI 动作层另有 Story/SwitchPage 拦截）。
            // 重命名中跳过，避免影响向输入框里打字。
            if (!PresetPanel.IsRenaming)
            {
                try
                {
                    string key = b1.Plugins.GSInput.UGSE_InputFuncLib
                        .GetKeyByKeyEvent(InKeyEvent).ToString();
                    if (!string.IsNullOrEmpty(key) &&
                        key.IndexOf("FaceButton_Top", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        PresetPanel.NotifyGamepadInput();
                        PresetPanel.RenameFromGamepad();
                        __result = b1.UGSE_UMGFuncLib.MakeEventReply(true);
                        return false;
                    }
                }
                catch { }
            }

            int direction;
            try
            {
                direction = UIGamepadInput.ToDirection(
                    b1.UGSE_UINavigationFuncLib.GetNavigationDirectionFromKey(InKeyEvent));
            }
            catch
            {
                return true;
            }

            if (direction < 0) return true;

            bool repeat = false;
            try { repeat = b1.UGSE_UINavigationFuncLib.IsRepeatAction(InKeyEvent); }
            catch { }

            // 弹窗期间方向键用于在弹窗控件之间切换，同时挡住背景焦点。
            if (PresetPanel.IsModalOpen)
            {
                PresetPanel.NavigateRenameFromNative(direction, repeat);
                __result = b1.UGSE_UMGFuncLib.MakeEventReply(true);
                return false;
            }

            PresetPanel.NotifyGamepadInput();
            PresetPanel.NavigateFromNative(direction, repeat);
            __result = b1.UGSE_UMGFuncLib.MakeEventReply(true);
            return false;
        }
    }

    /// <summary>
    /// 重命名对话框打开时，原生 BUI_Button 的延迟聚焦会把键盘焦点从输入框抢走，
    /// 导致手柄打开的重命名框不能直接打字。这里只在该对话框显示期间跳过延迟聚焦。
    /// </summary>
    [HarmonyPatch(typeof(b1.UI.Comm.BUI_Button), "TickSetFocus")]
    internal static class BUIButtonFocusBlockPatch
    {
        private static bool Prefix()
        {
            // 只有重命名输入框持有焦点时才需要阻止原生按钮抢焦点；
            // 切到确定/取消后反而需要原生入口获得焦点，左右键才能导航。
            return !(UIGamepadInput.Installed && PresetPanel.IsOpen && PresetPanel.IsRenaming
                     && PresetPanel.RenameFocusIsInput);
        }
    }

    /// <summary>
    /// 左摇杆：焦点 BUI_Button 的模拟量事件里取方向；面板打开时转给面板并吞掉事件。
    /// </summary>
    [HarmonyPatch(typeof(b1.UI.Comm.BUI_Button), "OnAnalogValueChanged_Implementation")]
    internal static class BUIButtonAnalogNavPatch
    {
        private static bool Prefix(FAnalogInputEvent InAnalogInputEvent, ref FEventReply __result)
        {
            if (!UIGamepadInput.Installed || !PresetPanel.IsOpen) return true;

            int direction;
            try
            {
                direction = UIGamepadInput.ToDirection(
                    b1.UGSE_UINavigationFuncLib.GetNavigationDirectionFromAnalogForOP(InAnalogInputEvent));
            }
            catch
            {
                return true;
            }

            if (direction < 0) return true;

            if (PresetPanel.IsModalOpen)
            {
                PresetPanel.NavigateRenameFromNative(direction, true);
                __result = b1.UGSE_UMGFuncLib.MakeEventReply(true);
                return false;
            }

            PresetPanel.NotifyGamepadInput();
            PresetPanel.NavigateFromNative(direction, true);
            __result = b1.UGSE_UMGFuncLib.MakeEventReply(true);
            return false;
        }
    }
}
