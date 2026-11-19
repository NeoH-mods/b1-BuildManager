using System;
using System.Threading;
using CSharpModBase;
using CSharpModBase.Input;

namespace BuildManager
{
    /// <summary>
    /// 纯 A..Z 方案热键：
    ///   点一次   = 应用方案 A..Z（延迟 350ms 等第二次点击）
    ///   快速点两次 = 保存当前配装到方案 A..Z
    /// 只使用 26 个字母，无 Ctrl/Shift/F 键。加载器不拦截按键，游戏也会收到同一按键。
    /// </summary>
    internal static class LetterPresetHotkeys
    {
        private const int DoubleTapWindowMs = 600;

        private static readonly object Gate = new object();
        private static Timer _timer;
        private static char _pendingLetter;
        private static bool _hasPending;

        public static void Register()
        {
            for (int i = 0; i < 26; i++)
            {
                char letter = (char)('A' + i);
                Key key = (Key)((int)Key.A + i);
                char captured = letter;
                Utils.RegisterKeyBind(key, () => SafePress(captured));
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                DisposeTimer();
                _hasPending = false;
            }
        }

        private static void SafePress(char letter)
        {
            try
            {
                Press(letter);
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] letter hotkey " + letter + " failed: " + ex);
            }
        }

        private static void Press(char letter)
        {
            bool save = false;
            char applyNow = '\0';
            bool hasApplyNow = false;

            lock (Gate)
            {
                if (_hasPending && _pendingLetter == letter)
                {
                    // 第二次点击：取消延迟应用，改为保存当前配装
                    DisposeTimer();
                    _hasPending = false;
                    save = true;
                }
                else
                {
                    if (_hasPending)
                    {
                        // 换了另一个字母：先把上一个立即应用，再等新字母
                        applyNow = _pendingLetter;
                        hasApplyNow = true;
                        DisposeTimer();
                    }

                    _pendingLetter = letter;
                    _hasPending = true;
                    _timer = new Timer(OnTimer, letter, DoubleTapWindowMs, Timeout.Infinite);
                }
            }

            if (hasApplyNow) ApplyOnGameThread(applyNow);
            if (save) SaveOnGameThread(letter);
        }

        private static void OnTimer(object state)
        {
            char letter = (char)state;

            lock (Gate)
            {
                if (!_hasPending || _pendingLetter != letter) return;
                _hasPending = false;
                DisposeTimer();
            }

            ApplyOnGameThread(letter);
        }

        private static void ApplyOnGameThread(char letter)
        {
            string slot = letter.ToString();
            Utils.TryRunOnGameThread(() =>
            {
                try
                {
                    Log.Info("[BuildManager] hotkey apply preset " + slot);
                    PresetService.ApplyPreset(slot);
                }
                catch (Exception ex)
                {
                    Log.Error("[BuildManager] apply preset " + slot + " failed: " + ex);
                }
            });
        }

        private static void SaveOnGameThread(char letter)
        {
            string slot = letter.ToString();
            try
            {
                Log.Info("[BuildManager] hotkey save preset " + slot);
                PresetService.SavePreset(slot);
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] save preset " + slot + " failed: " + ex);
            }
        }

        private static void DisposeTimer()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }
    }
}
