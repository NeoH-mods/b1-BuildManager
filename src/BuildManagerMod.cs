using System;
using CSharpModBase;

namespace BuildManager
{
    /// <summary>
    /// 黑神话：悟空 配装管理器（C# 版）。
    /// 加载器约定：CSharpLoader\Mods\BuildManager\BuildManager.dll，存在即启用。
    /// </summary>
    public sealed class BuildManagerMod : ICSharpMod
    {
        public string Name => "BuildManager";

        public string Version => typeof(BuildManagerMod).Assembly.GetName().Version.ToString();

        public void Init()
        {
            Log.StartSession("BuildManager v" + Version);
            Log.Info("[BuildManager] loaded v" + Version);
            Log.Info("[BuildManager] modDir=" + ModPaths.ModDir + " dataDir=" + ModPaths.DataDir +
                     " runtimeDir=" + ModPaths.RuntimeDir);
            Log.Info("[BuildManager] log file: " + Log.FilePath);

            // 语言表要在任何 UI 构建之前就绪（内嵌 JSON + 可选外部覆盖）。
            L.Initialize();
            Log.Info("[BuildManager] locale=" + L.CurrentCode + " override=" + L.OverrideCode);

            UIEquipPageHook.Start();
            GamepadNavigator.Start();

            Log.Info("[BuildManager] UI only: equip page preset button; mouse or Xbox pad (left stick/D-pad + A/B).");
        }

        public void DeInit()
        {
            GamepadNavigator.Stop();
            UIEquipPageHook.Stop();
            Sequencer.Stop();
            Log.Info("[BuildManager] unloaded");
            Log.EndSession();
        }

        private static void Safe(string label, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] " + label + " failed: " + ex);
            }
        }
    }
}
