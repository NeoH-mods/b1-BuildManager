using System;
using System.Collections.Generic;
using System.Threading;
using CSharpModBase;

namespace BuildManager
{
    /// <summary>
    /// 把一串换装动作按固定间隔投递到游戏线程，避免一次协议风暴。
    /// 游戏热重载时 DeInit 会调用 Stop，旧程序集不会留下定时器。
    /// </summary>
    internal static class Sequencer
    {
        private static readonly object Gate = new object();
        private static Timer _timer;
        private static List<Action> _steps;
        private static int _index;
        private static Action _onDone;
        private static string _label;

        public static bool IsRunning
        {
            get { lock (Gate) { return _timer != null; } }
        }

        public static bool Run(string label, List<Action> steps, int intervalMs, Action onDone)
        {
            lock (Gate)
            {
                if (_timer != null)
                {
                    Log.Warn("[BuildManager] previous task (" + _label + ") is still running; ignored.");
                    return false;
                }

                if (steps == null || steps.Count == 0)
                {
                    Log.Info("[BuildManager] " + label + ": nothing to change.");
                    onDone?.Invoke();
                    return true;
                }

                _label = label;
                _steps = steps;
                _index = 0;
                _onDone = onDone;
                _timer = new Timer(Tick, null, intervalMs, intervalMs);
                Log.Info("[BuildManager] " + label + ": running " + steps.Count + " steps, interval " + intervalMs + "ms.");
                return true;
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
                _steps = null;
                _onDone = null;
                _label = null;
            }
        }

        private static void Tick(object state)
        {
            Action step = null;
            Action done = null;
            bool finished = false;

            lock (Gate)
            {
                if (_timer == null) return;

                if (_index >= _steps.Count)
                {
                    _timer.Dispose();
                    _timer = null;
                    done = _onDone;
                    _onDone = null;
                    _steps = null;
                    finished = true;
                }
                else
                {
                    step = _steps[_index++];
                }
            }

            if (finished)
            {
                if (done != null) SafeOnGameThread(done);
                return;
            }

            if (step == null) return;
            SafeOnGameThread(step);
        }

        private static void SafeOnGameThread(Action action)
        {
            try
            {
                Utils.TryRunOnGameThread(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[BuildManager] sequence step failed: " + ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] failed to dispatch to game thread: " + ex);
            }
        }
    }
}
