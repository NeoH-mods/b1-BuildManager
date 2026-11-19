using System;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;

namespace BuildManager
{
    /// <summary>
    /// 配装面板的手柄导航：左摇杆/十字键移动焦点，A 确认，B 返回（Xbox 键位）。
    /// 使用 XInput 轮询；动作只入队，由输入管理器 tick 在游戏线程执行 UMG 操作。
    /// </summary>
    internal static class GamepadNavigator
    {
        private const ushort PadUp = 0x0001;
        private const ushort PadDown = 0x0002;
        private const ushort PadLeft = 0x0004;
        private const ushort PadRight = 0x0008;
        private const ushort PadA = 0x1000;
        private const ushort PadB = 0x2000;
        private const ushort PadY = 0x8000;
        private const short StickThreshold = 16000;

        private const int FirstRepeatMs = 380;
        private const int RepeatMs = 130;

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState14(int userIndex, out XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState910(int userIndex, out XInputState state);

        private static Thread _thread;
        private static volatile bool _running;
        private static volatile bool _xinputBroken;
        private static bool _prefer910;

        private static int _padIndex = -1;
        private static bool _lastA;
        private static bool _lastB;
        private static bool _lastY;
        private static int _heldDirection;
        private static int _nextRepeat;
        private static bool _loggedPad;
        private static bool _loggedNoPad;

        public static void Start()
        {
            if (_thread != null) return;
            _running = true;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "BuildManager.Gamepad"
            };
            _thread.Start();
            Log.Info("[BuildManager] gamepad navigation started (Xbox: left stick/D-pad, A=confirm, B=back)");
        }

        public static void Stop()
        {
            _running = false;
            Thread thread = _thread;
            _thread = null;
            if (thread == null) return;

            try { thread.Join(300); }
            catch { }
        }

        public static void OnPanelOpened()
        {
            // 不要重置 A/B：用 A 打开面板时按键通常还按着，重置会立刻触发一次误确认。
            _heldDirection = 0;
            _nextRepeat = 0;
            _loggedPad = false;
            _loggedNoPad = false;
        }

        public static void OnPanelClosed()
        {
            _heldDirection = 0;
            _nextRepeat = 0;
        }

        private static void Loop()
        {
            while (_running)
            {
                try
                {
                    // 游戏 UI 动作补丁生效后，手柄输入由游戏自己的输入管线驱动；
                    // XInput 只作为补丁未装上的兜底，避免同一次按键触发两遍。
                    if (UIGamepadInput.Installed)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    if (PresetPanel.IsOpen) Poll();
                    else PollClosed();
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] gamepad poll failed: " + ex.Message);
                    Thread.Sleep(500);
                }

                Thread.Sleep(15);
            }
        }

        /// <summary>面板关闭时只关心 A：焦点在装备页的“配装”按钮上就用 A 打开面板。</summary>
        private static void PollClosed()
        {
            XInputState state;
            if (!TryReadPad(out state)) return;

            ushort buttons = state.Gamepad.Buttons;
            NotifyActivity(buttons, state.Gamepad);
            bool a = (buttons & PadA) != 0;
            if (a && !_lastA)
            {
                PresetPanel.EnqueueGamepad(UIEquipPageHook.OnGamepadConfirm);
            }
            _lastA = a;
            _lastB = (buttons & PadB) != 0;
            _lastY = (buttons & PadY) != 0;
            _heldDirection = 0;
            _nextRepeat = 0;
        }

        private static void Poll()
        {
            XInputState state;
            if (!TryReadPad(out state)) return;

            if (!_loggedPad)
            {
                _loggedPad = true;
                Log.Info("[BuildManager] gamepad detected index=" + _padIndex);
            }

            ushort buttons = state.Gamepad.Buttons;
            NotifyActivity(buttons, state.Gamepad);
            bool a = (buttons & PadA) != 0;
            bool b = (buttons & PadB) != 0;
            bool y = (buttons & PadY) != 0;

            if (a && !_lastA)
            {
                Log.Info("[BuildManager] gamepad A (confirm)");
                PresetPanel.EnqueueGamepad(PresetPanel.Confirm);
            }
            if (b && !_lastB)
            {
                Log.Info("[BuildManager] gamepad B (back)");
                PresetPanel.EnqueueGamepad(PresetPanel.BackKey);
            }
            if (y && !_lastY)
            {
                Log.Info("[BuildManager] gamepad Y (rename)");
                PresetPanel.EnqueueGamepad(PresetPanel.RenameFromGamepad);
            }
            _lastA = a;
            _lastB = b;
            _lastY = y;

            bool up = (buttons & PadUp) != 0 || state.Gamepad.ThumbLY > StickThreshold;
            bool down = (buttons & PadDown) != 0 || state.Gamepad.ThumbLY < -StickThreshold;
            bool left = (buttons & PadLeft) != 0 || state.Gamepad.ThumbLX < -StickThreshold;
            bool right = (buttons & PadRight) != 0 || state.Gamepad.ThumbLX > StickThreshold;

            int direction = up ? 1 : down ? 2 : left ? 3 : right ? 4 : 0;
            int now = Environment.TickCount;

            if (direction == 0)
            {
                _heldDirection = 0;
                return;
            }

            if (direction != _heldDirection)
            {
                _heldDirection = direction;
                _nextRepeat = now + FirstRepeatMs;
                FireDirection(direction);
            }
            else if (now >= _nextRepeat)
            {
                _nextRepeat = now + RepeatMs;
                FireDirection(direction);
            }
        }

        private static void FireDirection(int direction)
        {
            Log.Debug("[BuildManager] gamepad direction " + direction);
            PresetPanel.EnqueueGamepad(() => PresetPanel.Navigate(direction - 1));
        }

        /// <summary>手柄有任何按钮/摇杆活动时，把面板提示切到“手柄键位”。</summary>
        private static void NotifyActivity(ushort buttons, XInputGamepad pad)
        {
            if (buttons != 0
                || Math.Abs((int)pad.ThumbLX) > 8000
                || Math.Abs((int)pad.ThumbLY) > 8000)
            {
                PresetPanel.NotifyGamepadInput();
            }
        }

        private static bool TryReadPad(out XInputState state)
        {
            state = default(XInputState);
            if (_xinputBroken)
            {
                WarnNoPadOnce();
                return false;
            }

            if (_padIndex >= 0 && Read(_padIndex, out state)) return true;

            for (int i = 0; i < 4; i++)
            {
                if (!Read(i, out state)) continue;
                _padIndex = i;
                return true;
            }

            _padIndex = -1;
            WarnNoPadOnce();
            return false;
        }

        private static bool Read(int index, out XInputState state)
        {
            state = default(XInputState);
            if (!_prefer910)
            {
                try { return XInputGetState14(index, out state) == 0; }
                catch (DllNotFoundException) { _prefer910 = true; }
                catch (EntryPointNotFoundException) { _prefer910 = true; }
            }

            try { return XInputGetState910(index, out state) == 0; }
            catch (DllNotFoundException)
            {
                _xinputBroken = true;
                Log.Warn("[BuildManager] XInput DLL not found; gamepad navigation disabled.");
            }
            catch (EntryPointNotFoundException)
            {
                _xinputBroken = true;
                Log.Warn("[BuildManager] XInputGetState not found; gamepad navigation disabled.");
            }
            return false;
        }

        private static void WarnNoPadOnce()
        {
            if (_loggedNoPad || _xinputBroken) return;
            _loggedNoPad = true;
            Log.Info("[BuildManager] no XInput gamepad detected while panel is open.");
        }
    }

    /// <summary>
    /// 游戏线程派发器：手柄线程只入队，UMG 操作在输入管理器 tick 里执行。
    /// </summary>
    [HarmonyPatch(typeof(b1.BGW_EnhancedInputMgrV2), "OnTickWithGroup")]
    internal static class InputManagerTickPatch
    {
        private static void Postfix()
        {
            // 面板关闭时也要处理打开请求；打开后再处理悬停与面板导航。
            PresetPanel.ProcessPending();
            UIEquipPageHook.UpdateEntryFocusStyle();
            if (!PresetPanel.IsOpen) return;
            PresetPanel.ProcessMouseHover();
            PresetPanel.ProcessIconFit();
        }
    }
}
