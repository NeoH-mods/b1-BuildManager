using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnrealEngine.SlateCore;
using UnrealEngine.UMG;
using UnrealEngine.Runtime;

namespace BuildManager
{
    [HarmonyPatch]
    internal static class UIEquipPagePatch
    {
        private static MethodBase TargetMethod()
        {
            Type type = Type.GetType("B1UI.GSUI.UIEquipMain, B1UI_GSE.Script");
            if (type == null) return null;
            return type.GetMethod("OnUIPageConstructImpl",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            try
            {
                UIEquipPageHook.OnPageConstructed(__instance);
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] equip UI postfix failed: " + ex);
            }
        }
    }

    /// <summary>
    /// C5 UI 第一步：在装备页注入“配装”按钮，验证 Harmony/UMG/焦点链路。
    /// 后续面板在按钮点击后打开。
    /// </summary>
    internal static class UIEquipPageHook
    {
        private const int EquipPageId = 14; // B1GSUIMgr.RegAllUIPage: UIEquipMain -> page 14
        private const float EntryFocusScale = 1.15f; // 与原生槽位焦点放大观感对齐（HoverRoot 330 对槽位 286 ≈ 1.15）
        // 诊断开关：打开后每次构造装备页会把原生槽位控件树和画刷路径打进日志（约 40 行）。
        // 2026-11-15 已用于确认 UGSFocusWidget 无画刷/材质、HoverRoot 330 对槽位 286，结论已落地，默认关闭。
        private static readonly bool DumpNativeVisuals = false;

        private static readonly object Gate = new object();
        private static Harmony _harmony;
        private static Timer _retryTimer;
        private static int _attempts;

        private static UButton _entryButton;
        private static UTextBlock _entryLabel;
        private static UUserWidget _entryClone;
        private static UCanvasPanel _entryFocusLayer;
        private static bool _entryLabelFocused;
        private static GSE.GSUI.GSUIView _view;
        private static UUserWidget _pageWidget;
        private static UWidget _pageRoot;

        public static void Start()
        {
            lock (Gate)
            {
                if (_harmony != null || _retryTimer != null) return;
                _attempts = 0;
                _retryTimer = new Timer(TryInstall, null, 0, 2000);
                Log.Info("[BuildManager] equip UI hook: waiting for UIEquipMain");
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                if (_retryTimer != null)
                {
                    _retryTimer.Dispose();
                    _retryTimer = null;
                }

                if (_harmony != null)
                {
                    try { _harmony.UnpatchAll("BuildManager.EquipUI"); }
                    catch (Exception ex) { Log.Warn("[BuildManager] equip UI unpatch failed: " + ex.Message); }
                    _harmony = null;
                }

                HideEntryButton();
                PresetPanel.Destroy();
                RestorePageFocus();
                _view = null;
                _pageWidget = null;
                _pageRoot = null;
            }
        }

        public static bool IsCurrentView(GSE.GSUI.GSUIView view)
        {
            return view != null && object.ReferenceEquals(_view, view);
        }

        /// <summary>
        /// 原始装备页是否仍在前台。玩家用肩键切到背包/设置等其它页时返回 false，
        /// 配装面板需要据此自动关闭，否则手柄输入会被我们的面板吃掉。
        /// </summary>
        public static bool IsEquipPageActive()
        {
            try
            {
                if (_view == null) return false;

                GSE.GSUI.GSUIPage page = _view.GetOwnerPage();
                if (page == null) return false;

                return page.IsActiveShowing();
            }
            catch
            {
                // 读不到状态时按“不在前台”处理：宁可收起面板，也不让输入被吃掉。
                return false;
            }
        }

        /// <summary>
        /// 把游戏焦点放回“配装”入口。鼠标打开面板时焦点可能停在别的格子，
        /// 之后 D-pad/摇杆的按键事件就不会送到我们的入口按钮。
        /// </summary>
        public static void FocusEntry()
        {
            if (_entryClone == null || _view == null) return;

            try
            {
                // SetFocusOn 会同时登记到 UIMgr 并更新页面的 FocusWidget；
                // 只调页面 SetFocusWidget 会漏掉 FocusWidgetRegDict。
                b1.UI.Comm.BUI_Button button = _entryClone as b1.UI.Comm.BUI_Button;
                if (button != null)
                {
                    button.SetFocusOn(b1.EFocusReason.ManualSet);
                    return;
                }

                GSE.GSUI.GSUIPage page = _view.GetOwnerPage();
                b1.IFocusWidget focus = _entryClone as b1.IFocusWidget;
                if (page == null || focus == null) return;
                page.SetFocusWidget(b1.EFocusReason.ManualSet, focus);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] focus entry failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 立即把 Slate 键盘焦点也移到入口按钮，供重命名窗口“确定/取消”状态使用：
        /// 输入框让出焦点后，左右方向键才能到页面/按钮层做导航。
        /// </summary>
        public static void FocusEntryNow()
        {
            FocusEntry();
            try
            {
                UWidget widget = _entryClone as UWidget;
                if (widget != null) widget.SetFocus();
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] focus entry now failed: " + ex.Message);
            }
        }

        /// <summary>入口“配装”按钮当前是否持有游戏焦点（手柄用）。</summary>
        public static bool IsEntryFocused()
        {
            if (_entryClone == null) return false;

            try
            {
                GSE.GSUI.GSUIPage page = _view == null ? null : _view.GetOwnerPage();
                if (page != null)
                {
                    object focusBtn = page.GetFocusWidgetBtn();
                    object focusWidget = page.GetFocusWidget();
                    if (IsEntryWidget(focusBtn) || IsEntryWidget(focusWidget) ||
                        IsUnder(_entryClone, focusBtn as UWidget) ||
                        IsUnder(_entryClone, focusWidget as UWidget))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] entry focus query failed: " + ex.Message);
            }

            return HasFocusedButton(_entryClone);
        }

        private static bool IsUnder(UWidget ancestor, UWidget node)
        {
            for (int i = 0; i < 32 && node != null; i++)
            {
                if (sameWidget(ancestor, node)) return true;
                node = node.GetParent();
            }
            return false;
        }

        /// <summary>同一个 UObject 在托管侧可能是不同包装实例，优先比较底层地址。</summary>
        private static bool sameWidget(UWidget a, UWidget b)
        {
            if (a == null || b == null) return false;
            if (object.ReferenceEquals(a, b)) return true;
            try
            {
                IntPtr left = a.Address;
                return left != IntPtr.Zero && left == b.Address;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>克隆按钮的名字统一带 BI_PresetEntry 前缀，便于识别游戏焦点。</summary>
        private static bool IsEntryWidget(object obj)
        {
            UWidget widget = obj as UWidget;
            if (widget == null) return false;
            if (sameWidget(_entryClone, widget) || sameWidget(_entryButton, widget)) return true;

            try
            {
                string name = widget.GetFName().ToString();
                return name.StartsWith("BI_PresetEntry", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasFocusedButton(UWidget widget)
        {
            if (widget == null) return false;

            try
            {
                b1.UI.Comm.BUI_Button button = widget as b1.UI.Comm.BUI_Button;
                if (button != null && (button.GetIsFocused() || button.HasRealFocus())) return true;
            }
            catch { }

            UPanelWidget panel = widget as UPanelWidget;
            if (panel == null) return false;
            int count = panel.GetChildrenCount();
            for (int i = 0; i < count; i++)
            {
                if (HasFocusedButton(panel.GetChildAt(i))) return true;
            }
            return false;
        }

        /// <summary>手柄 A：焦点在入口按钮上时打开配装面板（必须运行在游戏线程）。</summary>
        public static void OnGamepadConfirm()
        {
            if (PresetPanel.IsOpen) return;

            // 装备页已经关掉时不要误触发（_entryClone 在页面销毁后仍可能被引用）。
            try
            {
                GSE.GSUI.GSUIPage page = _view == null ? null : _view.GetOwnerPage();
                if (page == null || !page.IsActiveShowing()) return;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] equip page state query failed: " + ex.Message);
                return;
            }

            if (!IsEntryFocused()) return;
            Log.Info("[BuildManager] gamepad A on entry button");
            OnEntryPressed();
        }

        private static void TryInstall(object state)
        {
            lock (Gate)
            {
                if (_harmony != null || _retryTimer == null) return;
                _attempts++;

                try
                {
                    Type type = Type.GetType("B1UI.GSUI.UIEquipMain, B1UI_GSE.Script");
                    if (type == null)
                    {
                        if (_attempts % 5 == 1) Log.Info("[BuildManager] equip UI hook: UIEquipMain not loaded yet");
                        if (_attempts >= 30) StopTimerOnly();
                        return;
                    }

                    var harmony = new Harmony("BuildManager.EquipUI");
                    harmony.PatchAll(typeof(UIEquipPagePatch).Assembly);
                    _harmony = harmony;
                    StopTimerOnly();
                    // 游戏 UI 动作补丁已生效：之后手柄输入不再走 XInput 轮询。
                    UIGamepadInput.MarkInstalled();
                    Log.Info("[BuildManager] equip UI hook installed");

                    // 热重载时装备页可能已经构造过，postfix 不会再触发；主动找当前页补一次按钮。
                    TryAttachCurrentPage();
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] equip UI hook install failed (attempt " + _attempts + "): " + ex.Message);
                    if (_attempts >= 30) StopTimerOnly();
                }
            }
        }

        private static void StopTimerOnly()
        {
            if (_retryTimer != null)
            {
                _retryTimer.Dispose();
                _retryTimer = null;
            }
        }

        private static void TryAttachCurrentPage()
        {
            try
            {
                GSE.GSUI.GSUIPageOP pageOp = B1UI.GSG.GSPageOP;
                if (pageOp == null)
                {
                    Log.Info("[BuildManager] equip UI hook: GSPageOP is null; wait for page construct.");
                    return;
                }

                GSE.GSUI.GSUIPage page = pageOp.FindUIPage(EquipPageId);
                if (page == null)
                {
                    Log.Info("[BuildManager] equip UI hook: page " + EquipPageId + " not created yet.");
                    return;
                }

                Log.Info("[BuildManager] equip UI hook: attaching to existing page " + page.GetType().FullName);
                OnPageConstructed(page);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] equip UI hook: attach existing page failed: " + ex);
            }
        }

        public static void OnPageConstructed(object instance)
        {
            GSE.GSUI.GSUIView view = instance as GSE.GSUI.GSUIView;
            if (view == null)
            {
                Log.Warn("[BuildManager] equip UI hook: instance is not GSUIView: " +
                         (instance == null ? "null" : instance.GetType().FullName));
                return;
            }

            _view = view;

            UUserWidget page = view.GetRootUserWidget();
            if (page == null)
            {
                Log.Warn("[BuildManager] equip UI hook: GetRootUserWidget returned null for " +
                         view.GetType().FullName);
                return;
            }

            UWidget root = b1.UGSE_UMGFuncLib.GetRootWidget(page);
            Log.Info("[BuildManager] equip page constructed; view=" + view.GetType().FullName +
                     " rootWidget=" + (root == null ? "null" : root.GetType().FullName));

            if (root == null) return;

            // 页面关闭再打开会创建新的 UUserWidget；旧面板还挂在旧 root 上，必须重建，
            // 否则 Toggle 会把“已可见”的旧面板当成打开状态直接收起。
            if (!object.ReferenceEquals(_pageWidget, page))
            {
                PresetPanel.Destroy();
            }

            _pageWidget = page;
            _pageRoot = root;

            // 优先克隆第一行“精魄”槽位按钮，保持与武器/法宝/精魄一致的尺寸、样式和横向排列。
            if (!CreateNativeEntryButton(instance, root))
            {
                Log.Warn("[BuildManager] native entry clone failed; falling back to simple overlay button.");
                CreateEntryButton(page, root);
            }

            RegisterConfirmAction(view);
        }

        private static GSE.GSUI.FGSOnInputAction _confirmListener;

        /// <summary>
        /// 在页面根控件上显式注册 IA_GSUIConfirm。焦点落在克隆出来的“配装”入口上时，
        /// 原生按钮不一定还持有 ButtonPress 绑定；不注册的话 A 根本不会进入 UI 动作分发。
        /// 实际处理在 UIActionTrackerPatch 的前缀里，这里只负责把动作挂上。
        /// </summary>
        private static void RegisterConfirmAction(GSE.GSUI.GSUIView view)
        {
            if (view == null) return;

            try
            {
                if (_confirmListener == null) _confirmListener = OnConfirmAction;
                view.ListenToInputAction(b1.GSUIActionEn.Confirm, _confirmListener,
                    false, false, UnrealEngine.Plugins.EnhancedInput.ETriggerEvent.Started);
                Log.Info("[BuildManager] confirm input action registered on equip page");
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] register confirm input action failed: " + ex.Message);
            }
        }

        private static void OnConfirmAction()
        {
            // 动作注册用；打开/确认由 UIActionTrackerPatch 统一处理，避免重复触发。
        }

        private static bool CreateNativeEntryButton(object instance, UWidget root)
        {
            try
            {
                RemoveStaleEntryClones(root);

                FieldInfo field = instance.GetType().GetField("BIEquipSlot",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Array slots = field == null ? null : field.GetValue(instance) as Array;
                if (slots == null || slots.Length <= 3)
                {
                    Log.Warn("[BuildManager] native entry: BIEquipSlot missing/too short");
                    return false;
                }

                // 第一行（武器/法宝/精魄）的父容器只有这 3 个槽位；其它行是 4（护甲）、
                // 5（珍玩）、2（酒/泡酒物）等。按“父容器包含几个槽位”分组，可避免热重载
                // 残留的第 4 个子控件让“子控件数==3”的判断失效。
                var groups = new List<KeyValuePair<UPanelWidget, List<UWidget>>>();
                foreach (object obj in slots)
                {
                    UWidget widget = obj as UWidget;
                    if (widget == null) continue;
                    UPanelWidget widgetParent = widget.GetParent();
                    if (widgetParent == null) continue;

                    int found = -1;
                    for (int g = 0; g < groups.Count; g++)
                    {
                        if (object.ReferenceEquals(groups[g].Key, widgetParent))
                        {
                            found = g;
                            break;
                        }
                    }
                    if (found < 0)
                    {
                        groups.Add(new KeyValuePair<UPanelWidget, List<UWidget>>(
                            widgetParent, new List<UWidget>()));
                        found = groups.Count - 1;
                    }
                    if (!groups[found].Value.Contains(widget)) groups[found].Value.Add(widget);
                }

                UPanelWidget parent = null;
                UWidget reference = null;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (groups[g].Value.Count == 3)
                    {
                        parent = groups[g].Key;
                        reference = groups[g].Value[groups[g].Value.Count - 1];
                        break;
                    }
                }

                if (parent == null)
                {
                    reference = slots.GetValue(3) as UWidget;
                    parent = reference == null ? null : reference.GetParent();
                }

                if (reference == null || parent == null)
                {
                    Log.Warn("[BuildManager] native entry: no reference slot");
                    return false;
                }

                // 清掉上次加载残留的“配装”按钮：第一行原生只有 3 个槽位，其余子控件都是本 mod 加的。
                var nativeSlots = new HashSet<UWidget>();
                for (int g = 0; g < groups.Count; g++)
                {
                    if (!object.ReferenceEquals(groups[g].Key, parent)) continue;
                    foreach (UWidget w in groups[g].Value) nativeSlots.Add(w);
                }
                for (int i = parent.GetChildrenCount() - 1; i >= 0; i--)
                {
                    UWidget child = parent.GetChildAt(i);
                    if (child == null || nativeSlots.Contains(child)) continue;
                    Log.Info("[BuildManager] native entry: removing stale child " + child.GetType().Name +
                             " name=" + SafeName(child));
                    try { child.RemoveFromParent(); }
                    catch { }
                }

                Log.Info("[BuildManager] native entry row: children=" + parent.GetChildrenCount() +
                         " nativeSlots=" + nativeSlots.Count + " ref=" + reference.GetType().Name +
                         " size=" + reference.GetDesiredSize().X + "x" + reference.GetDesiredSize().Y);

                // 一次性 dump：把原生槽位的控件树和画刷资源路径打进日志，
                // 用来找出原生选中渐变的贴图/材质到底叫什么。默认关闭，需要时把开关改 true。
                if (DumpNativeVisuals)
                {
                    try { DumpWidgetVisuals(reference, 0); }
                    catch (Exception ex) { Log.Warn("[DUMP] failed: " + ex.Message); }
                }
                UUserWidget referenceWidget = reference as UUserWidget;
                b1.UI.BUI_Widget referenceBui = reference as b1.UI.BUI_Widget;
                if (parent == null || referenceWidget == null || referenceBui == null)
                {
                    Log.Warn("[BuildManager] native entry: slot3 parent/widget/bui is null");
                    return false;
                }

                // 名字必须每次唯一：CloneUserWidget 用同一个 FName 时会复用已存在的控件，
                // 热重载后会把背景/文字/按钮重复叠加到同一个克隆上。
                string cloneName = "BI_PresetEntry_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                UUserWidget clone = b1.UGSE_UMGFuncLib.CloneUserWidget(
                    referenceWidget, new FName(cloneName));
                if (clone == null)
                {
                    Log.Warn("[BuildManager] native entry: CloneUserWidget returned null");
                    return false;
                }
                _entryClone = clone;
                parent.AddChild(clone);
                // 焦点放大以中心为轴，否则会向右下角撑开。
                try { clone.RenderTransformPivot = new FVector2D(0.5f, 0.5f); }
                catch { }
                // 鼠标 hover 不要抢焦点：否则移开后焦点仍留在“配装”上，
                // 渐变会一直挂着。手柄导航设置的焦点不受影响。
                b1.UI.Comm.BUI_Button nativeButton = clone as b1.UI.Comm.BUI_Button;
                if (nativeButton != null) nativeButton.SetIsMouseEnterSetFocus(false);

                // 原生槽位之间的均匀空隙来自各自的 HorizontalBoxSlot.Padding；
                // 克隆默认 padding=0，会紧贴“精魄”。直接复制参考槽位的 padding。
                UHorizontalBoxSlot cloneSlot = clone.Slot as UHorizontalBoxSlot;
                UHorizontalBoxSlot refSlot = reference.Slot as UHorizontalBoxSlot;
                if (cloneSlot != null)
                {
                    FMargin pad = refSlot != null ? refSlot.Padding : default(FMargin);
                    // 原生空隙若不在 slot padding 上，补一个与视觉间距一致的最小间距；
                    // 底板本身再内缩，避免看起来比精魄/下一行更挤。
                    if (pad.Left < 30f) pad.Left = 30f;
                    if (pad.Right < 30f) pad.Right = 30f;
                    cloneSlot.SetPadding(pad);
                    Log.Info("[BuildManager] native entry padding: refSlot=" + (refSlot != null) +
                             " L=" + pad.Left + " R=" + pad.Right);
                }
                else
                {
                    Log.Warn("[BuildManager] native entry: clone slot is not UHorizontalBoxSlot");
                }

                UWidget cloneRoot = b1.UGSE_UMGFuncLib.GetRootWidget(clone);
                Log.Info("[BuildManager] native entry clone: type=" + clone.GetType().FullName +
                         " parent=" + parent.GetType().FullName +
                         " root=" + (cloneRoot == null ? "null" : cloneRoot.GetType().FullName));

                UCanvasPanel cloneCanvas = cloneRoot as UCanvasPanel;
                if (cloneCanvas == null)
                {
                    Log.Warn("[BuildManager] native entry: clone root is not UCanvasPanel");
                    clone.RemoveFromParent();
                    return false;
                }

                FVector2D size = referenceWidget.GetDesiredSize();
                // 新建页面时布局尚未计算，DesiredSize 会是 0；第一行槽位本地尺寸为 286x206。
                if (size.X < 1f || size.Y < 1f) size = new FVector2D(286f, 206f);

                // 去掉克隆里精魄的图标，换成和原生槽位一致的纯黑底板。
                // 底板四周内缩，避免视觉上比原生格子更贴近“精魄”和下一行防具。
                UWidget itemImage = FindWidgetByName(cloneRoot, "ImgItem");
                if (itemImage != null) itemImage.SetVisibility(ESlateVisibility.Collapsed);

                // 克隆自精魄槽的悬浮图标原生在 ResizeCon 里，层级低于我们加的纯黑底板；
                // 把 ResizeCon 提到黑底之上（仍低于“配装”文字），图标就不会被方框遮住上半部分。
                UWidget nativeRoot = FindWidgetByName(cloneRoot, "ResizeCon");
                UCanvasPanelSlot nativeRootSlot = nativeRoot == null ? null : nativeRoot.Slot as UCanvasPanelSlot;
                if (nativeRootSlot != null)
                {
                    nativeRootSlot.SetZOrder(5);
                    Log.Info("[BuildManager] native entry ResizeCon z=5");
                }

                // 悬浮圆标（MarkerBase）锚在槽位底边上，视觉偏低；上移 11 个单位，
                // 让圆心对齐“配装”方框的底边。
                UWidget markerBase = FindWidgetByName(cloneRoot, "MarkerBase");
                UCanvasPanelSlot markerSlot = markerBase == null ? null : markerBase.Slot as UCanvasPanelSlot;
                if (markerSlot != null)
                {
                    markerSlot.SetPosition(new FVector2D(0f, -80f));
                    Log.Info("[BuildManager] native entry MarkerBase y=-80");
                }

                // 底板与原生槽位同尺寸（原来内缩 8/6 会显得比同级图标小一号）。
                const float InsetX = 0f;
                const float InsetY = 0f;
                UBorder background = UObject.NewObject<UBorder>(clone);
                background.SetBrushColor(new FLinearColor(0.012f, 0.011f, 0.010f, 1f));
                UCanvasPanelSlot backgroundSlot = cloneCanvas.AddChildToCanvas(background);
                backgroundSlot.SetPosition(new FVector2D(InsetX, InsetY));
                backgroundSlot.SetSize(new FVector2D(size.X - InsetX * 2f, size.Y - InsetY * 2f));
                // 黑底放到原生内容（含 UGSFocusWidget 焦点渐变）下面：
                // 未聚焦时看到黑底，聚焦时原生焦点层正常盖上来。
                backgroundSlot.SetZOrder(-10);

                // 原版虎头牌同款的蓝白渐变层。原生 UGSFocusWidget 依赖各自蓝图里的
                // hover/focus 动画才绘制，新建实例不出像素；这里按采样色生成条带渐变。
                // 层序：黑底(-10) < 渐变(0) < 配装文字(0, 后加入) < 蓝条(1)。
                _entryFocusLayer = UObject.NewObject<UCanvasPanel>(clone);
                _entryFocusLayer.SetVisibility(ESlateVisibility.Collapsed);
                UCanvasPanelSlot focusLayerSlot = cloneCanvas.AddChildToCanvas(_entryFocusLayer);
                focusLayerSlot.SetPosition(new FVector2D(InsetX, InsetY));
                focusLayerSlot.SetSize(new FVector2D(size.X - InsetX * 2f, size.Y - InsetY * 2f));
                focusLayerSlot.SetZOrder(0);

                const int GradientSteps = 28;
                float gradientW = size.X - InsetX * 2f;
                float gradientH = size.Y - InsetY * 2f;
                var gradientTop = new FLinearColor(0.172f, 0.235f, 0.402f, 1f);
                var gradientBottom = new FLinearColor(0.687f, 0.687f, 0.631f, 1f);
                float gradientStepH = gradientH / GradientSteps;
                for (int step = 0; step < GradientSteps; step++)
                {
                    float t = GradientSteps <= 1 ? 0f : (float)step / (GradientSteps - 1);
                    var color = new FLinearColor(
                        gradientTop.R + (gradientBottom.R - gradientTop.R) * t,
                        gradientTop.G + (gradientBottom.G - gradientTop.G) * t,
                        gradientTop.B + (gradientBottom.B - gradientTop.B) * t,
                        1f);
                    UBorder strip = UObject.NewObject<UBorder>(_entryFocusLayer);
                    strip.SetBrushColor(color);
                    UCanvasPanelSlot stripSlot = _entryFocusLayer.AddChildToCanvas(strip);
                    stripSlot.SetPosition(new FVector2D(0f, step * gradientStepH));
                    stripSlot.SetSize(new FVector2D(gradientW, gradientStepH + 0.75f));
                    stripSlot.SetZOrder(0);
                }

                UTextBlock label = UObject.NewObject<UTextBlock>(clone);
                bool styleCopied = TryCopyTextStyle(root, label);
                label.SetText(FText.FromString(L.T("entry.button")));
                label.SetJustification(UnrealEngine.Slate.ETextJustify.Center);
                try
                {
                    UnrealEngine.SlateCore.FSlateFontInfo font = label.Font;
                    // LetterSpacing 单位是 1/1000 em（0.06em ≈ 视觉上略宽松）。
                    font.Size = 44;
                    font.LetterSpacing = 60;
                    label.Font = font;
                    label.OverrideLetterSpacing = 60f;
                    var textColor = new FSlateColor();
                    textColor.SpecifiedColor = new FLinearColor(0.92f, 0.88f, 0.80f, 1f);
                    label.SetColorAndOpacity(textColor);
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] native entry font failed: " + ex.Message);
                }
                UCanvasPanelSlot textSlot = cloneCanvas.AddChildToCanvas(label);
                textSlot.SetPosition(new FVector2D(0f, (size.Y - 72f) * 0.5f));
                textSlot.SetSize(new FVector2D(size.X, 72f));
                // 和原生贴图层同级（ResizeCon 也是 z=5），后加入，绘制顺序在它之上；
                // 渐变层在 z=0，天然在文字/贴图下面。
                textSlot.SetZOrder(5);

                // 原版物品格底部的稀有度条：取虎头牌那条蓝色（Quality3），
                // 压在高亮层之上，保证获得焦点时仍能看到。
                const float BarInset = 8f;
                const float BarHeight = 5f;
                UBorder qualityBar = UObject.NewObject<UBorder>(clone);
                qualityBar.SetBrushColor(PresetPanel.Quality3);
                UCanvasPanelSlot barSlot = cloneCanvas.AddChildToCanvas(qualityBar);
                barSlot.SetPosition(new FVector2D(BarInset, size.Y - 10f));
                barSlot.SetSize(new FVector2D(size.X - BarInset * 2f, BarHeight));
                barSlot.SetZOrder(1);

                // 透明 UButton 盖在克隆槽位上，负责鼠标点击；视觉仍用原生槽位。
                UButton button = UObject.NewObject<UButton>(clone);
                button.BackgroundColor = new FLinearColor(0f, 0f, 0f, 0f);
                button.ColorAndOpacity = new FLinearColor(1f, 1f, 1f, 0f);
                // 焦点交给克隆出来的原生 BUI_Button（手柄导航/按键事件都由它接收），
                // 透明命中层只负责鼠标点击，不参与键盘/手柄焦点。
                button.IsFocusable = false;
                button.OnPressed.Bind(new FOnButtonPressedEvent.Signature(OnEntryPressed));
                UCanvasPanelSlot buttonSlot = cloneCanvas.AddChildToCanvas(button);
                buttonSlot.SetPosition(new FVector2D(0f, 0f));
                buttonSlot.SetSize(size);
                buttonSlot.SetZOrder(20);

                _entryButton = button;
                _entryLabel = label;
                RefreshEntryText();
                Log.Info("[BuildManager] native entry button bound: size=" + size.X + "x" + size.Y +
                         " styleCopied=" + styleCopied);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] native entry clone failed: " + ex);
                return false;
            }
        }

        private static void RemoveStaleEntryClones(UWidget root)
        {
            var stale = new List<UWidget>();
            CollectEntryClones(root, stale);
            foreach (UWidget widget in stale)
            {
                try { widget.RemoveFromParent(); }
                catch { }
            }
        }

        private static void CollectEntryClones(UWidget widget, List<UWidget> stale)
        {
            if (widget == null) return;
            try
            {
                if (widget.GetFName().ToString().StartsWith("BI_PresetEntry", StringComparison.Ordinal))
                {
                    stale.Add(widget);
                    return;
                }
            }
            catch { }

            UPanelWidget panel = widget as UPanelWidget;
            if (panel == null) return;
            int count = panel.GetChildrenCount();
            for (int i = 0; i < count; i++)
            {
                CollectEntryClones(panel.GetChildAt(i), stale);
            }
        }

        private static string SafeName(UWidget widget)
        {
            try { return widget.GetFName().ToString(); }
            catch { return "?"; }
        }

        private static UWidget FindWidgetByName(UWidget widget, string name)
        {
            if (widget == null) return null;
            try
            {
                if (widget.GetFName().ToString() == name) return widget;
            }
            catch { }

            UPanelWidget panel = widget as UPanelWidget;
            if (panel == null) return null;
            int count = panel.GetChildrenCount();
            for (int i = 0; i < count; i++)
            {
                UWidget found = FindWidgetByName(panel.GetChildAt(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void CreateEntryButton(UUserWidget page, UWidget root)
        {
            HideEntryButton();

            UButton button = UObject.NewObject<UButton>(page);
            UBorder border = UObject.NewObject<UBorder>(button);
            border.SetBrushColor(new FLinearColor(0.02f, 0.02f, 0.02f, 0.75f));

            UTextBlock label = UObject.NewObject<UTextBlock>(border);
            bool styleCopied = TryCopyTextStyle(root, label);
            label.SetText(FText.FromString(L.T("entry.button")));
            border.SetContent(label);
            button.SetContent(border);
            button.IsFocusable = true;
            // 动态创建的 UButton 在游戏里 OnClicked 不触发（OnPressed 可以），
            // 先用 OnPressed 作为入口；后续原生按钮再做一次点击语义。
            button.OnPressed.Bind(new FOnButtonPressedEvent.Signature(OnEntryPressed));

            UCanvasPanel canvas = root as UCanvasPanel;
            if (canvas != null)
            {
                UCanvasPanelSlot slot = canvas.AddChildToCanvas(button);
                slot.SetPosition(new FVector2D(80.0, 180.0));
                slot.SetSize(new FVector2D(160.0, 48.0));
                slot.SetZOrder(100);
            }
            else
            {
                UPanelWidget panel = root as UPanelWidget;
                if (panel == null)
                {
                    Log.Warn("[BuildManager] equip UI root is not a panel: " + root.GetType().FullName);
                    return;
                }
                panel.AddChild(button);
            }

            _entryButton = button;
            _entryLabel = label;
            RefreshEntryText();
            Log.Info("[BuildManager] equip UI entry button created; styleCopied=" + styleCopied);

            RestorePageFocus();
        }

        /// <summary>
        /// 语言切换后刷新入口按钮文字。按钮只有 160×48，英文/日文比中文长，
        /// 按字符数收缩字号，避免文字撑出黑底。
        /// </summary>
        public static void RefreshEntryText()
        {
            if (_entryLabel == null) return;
            try
            {
                L.RefreshGameLanguage();
                string text = L.T("entry.button");
                _entryLabel.SetText(FText.FromString(text));

                UnrealEngine.SlateCore.FSlateFontInfo font = _entryLabel.Font;
                int scaled = text.Length <= 2 ? 44 : (text.Length <= 4 ? 38 : 30);
                if (font.Size != scaled)
                {
                    font.Size = scaled;
                    _entryLabel.Font = font;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] entry label refresh failed: " + ex.Message);
            }
        }

        private static void RestorePageFocus()
        {
            try
            {
                if (_view == null) return;

                GSE.GSUI.GSUIPage page = _view.GetOwnerPage();
                if (page != null)
                {
                    MethodInfo reset = page.GetType().GetMethod("ResetFocus",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (reset != null)
                    {
                        reset.Invoke(page, null);
                        return;
                    }
                }

                MethodInfo focusOn = _view.GetType().GetMethod("SetFocusOn",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (focusOn != null) focusOn.Invoke(_view, null);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] restore page focus failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 复制页面里已有 TextBlock 的字体/颜色。动态 new 出来的 TextBlock 如果没有字体资源，
        /// 在游戏里可能完全不渲染。
        /// </summary>
        internal static bool TryCopyTextStyle(UWidget source, UTextBlock target)
        {
            if (source == null || target == null) return false;

            UTextBlock text = source as UTextBlock;
            if (text != null)
            {
                try
                {
                    UnrealEngine.SlateCore.FSlateFontInfo font = text.Font;
                    target.Font = font;
                    target.ColorAndOpacity = text.ColorAndOpacity;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] copy text style failed: " + ex.Message);
                }
            }

            UPanelWidget panel = source as UPanelWidget;
            if (panel != null)
            {
                int count = panel.GetChildrenCount();
                for (int i = 0; i < count; i++)
                {
                    if (TryCopyTextStyle(panel.GetChildAt(i), target)) return true;
                }
            }
            return false;
        }

        private static void HideEntryButton()
        {
            try
            {
                if (_entryClone != null)
                {
                    _entryClone.RemoveFromParent();
                    _entryClone = null;
                }
                if (_entryButton != null)
                {
                    _entryButton.SetVisibility(ESlateVisibility.Collapsed);
                    _entryButton = null;
                }
                _entryLabel = null;
                _entryFocusLayer = null;
                _entryLabelFocused = false;
            }
            catch { }
        }

        /// <summary>
        /// 每帧由输入管理器 tick 调用：原生 hover 成立时显示蓝白渐变 + 黑字，
        /// 移开后恢复黑底白字。鼠标 hover 不会抢焦点（创建时关掉了 IsMouseEnterSetFocus），
        /// 所以这里只看 hover，不把页面焦点算进来，避免渐变挂住。
        /// </summary>
        public static void UpdateEntryFocusStyle()
        {
            if (_entryClone == null || _entryLabel == null) return;

            bool hovered = false;
            b1.UI.Comm.BUI_Button nativeButton = _entryClone as b1.UI.Comm.BUI_Button;
            if (nativeButton != null)
            {
                try { hovered = nativeButton.GetIsHovered(); }
                catch { }
            }

            // 手柄导航的焦点也走同一套渐变，但只在当前输入设备是手柄时生效：
            // 鼠标移开后页面焦点可能还留在入口上，不能让它把渐变挂住。
            bool gamepadFocus = false;
            if (!hovered && IsEntryFocused())
            {
                try
                {
                    gamepadFocus = b1.BGW_EnhancedInputMgrV2.GetCurrentInputType()
                        .ToString() == "Gamepad";
                }
                catch { }
            }

            // 面板打开时入口被遮罩挡住，没必要再保留背后的渐变。
            bool focused = !PresetPanel.IsOpen && (hovered || gamepadFocus);
            if (focused == _entryLabelFocused) return;
            _entryLabelFocused = focused;
            if (_entryFocusLayer != null)
            {
                _entryFocusLayer.SetVisibility(focused
                    ? ESlateVisibility.SelfHitTestInvisible
                    : ESlateVisibility.Collapsed);
            }
            SetEntryLabelFocused(focused);
            SetEntryScale(focused);
        }

        /// <summary>模拟原生槽位的焦点放大：手柄/鼠标选中时整体放大一点。</summary>
        private static void SetEntryScale(bool focused)
        {
            if (_entryClone == null) return;
            try
            {
                var transform = new FWidgetTransform();
                float scale = focused ? EntryFocusScale : 1f;
                transform.Scale = new FVector2D(scale, scale);
                _entryClone.RenderTransform = transform;
                Log.Info("[BuildManager] entry scale=" + scale);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] entry scale failed: " + ex.Message);
            }
        }

        private static void SetEntryLabelFocused(bool focused)
        {
            if (_entryLabel == null) return;
            try
            {
                var color = new FSlateColor();
                color.SpecifiedColor = focused
                    ? new FLinearColor(0f, 0f, 0f, 1f)
                    : new FLinearColor(0.92f, 0.88f, 0.80f, 1f);
                _entryLabel.SetColorAndOpacity(color);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] entry label color failed: " + ex.Message);
            }
        }

        private static void OnEntryClicked()
        {
            Log.Info("[BuildManager] equip UI entry button clicked");
            if (_pageWidget == null || _pageRoot == null)
            {
                Log.Warn("[BuildManager] preset panel: page is not ready");
                return;
            }
            PresetPanel.Toggle(_pageWidget, _pageRoot);
        }

        private static void OnEntryPressed()
        {
            try
            {
                OnEntryClicked();
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] open preset panel failed: " + ex);
            }
        }

        /// <summary>
        /// 给按钮文字加软阴影：只向下偏移，字形本身仍由外层 UBorder 居中，
        /// 所以阴影往下拖而黑字/白字保持在框的正中。
        /// </summary>
        public static void ApplyDownwardTextShadow(UTextBlock label)
        {
            if (label == null) return;
            try
            {
                FSlateFontInfo font = label.Font;
                FFontOutlineSettings outline = font.OutlineSettings;
                FFontGSSoftShadowCfg shadow = outline.GSSoftShadowCfg;
                shadow.GSSoftShadowOffset = new FIntPoint(0, 8); // 纯向下
                shadow.GSSoftShadowSize = 3;
                outline.GSSoftShadowCfg = shadow;
                font.OutlineSettings = outline;
                label.Font = font;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] text shadow failed: " + ex.Message);
            }
        }

        // ---------------- 原生控件/画刷诊断 ----------------

        /// <summary>反射拿对象名/路径，避免依赖具体绑定 API 名。</summary>
        private static string ObjRef(object o)
        {
            if (o == null) return "null";
            try
            {
                MethodInfo m = o.GetType().GetMethod("GetPathName", Type.EmptyTypes);
                if (m != null)
                {
                    string s = m.Invoke(o, null) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                m = o.GetType().GetMethod("GetName", Type.EmptyTypes);
                if (m != null)
                {
                    string s = m.Invoke(o, null) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            try { return o.ToString(); } catch { }
            return o.GetType().Name;
        }

        private static void DumpWidgetVisuals(object node, int depth)
        {
            if (node == null || depth > 4) return;
            string indent = new string(' ', depth * 2);
            UWidget w = node as UWidget;

            string line = indent + node.GetType().Name + " name=" + ObjRef(node);
            if (w != null)
            {
                try { FVector2D s = w.GetDesiredSize(); line += " size=" + s.X + "x" + s.Y; }
                catch { }
                try { line += " vis=" + w.GetVisibility(); }
                catch { }
                try { FWidgetTransform t = w.RenderTransform; line += " scale=" + t.Scale.X + "," + t.Scale.Y; }
                catch { }
            }
            Log.Info("[DUMP] " + line);

            DumpBrushMembers(node, indent + "  ");

            // UGSFocusWidget 是原生焦点渐变，没有 FSlateBrush，参数都在成员里。
            if (node.GetType().Name.IndexOf("Focus", StringComparison.Ordinal) >= 0)
                DumpAllMembers(node, indent + "  ");

            // UUserWidget 的子树在它自己的 WidgetTree 里，要从 RootWidget 进去。
            UUserWidget userWidget = node as UUserWidget;
            if (userWidget != null)
            {
                UWidget inner = null;
                try { inner = b1.UGSE_UMGFuncLib.GetRootWidget(userWidget); }
                catch { }
                if (inner != null && !object.ReferenceEquals(inner, node))
                    DumpWidgetVisuals(inner, depth + 1);
            }

            UPanelWidget panel = node as UPanelWidget;
            if (panel == null) return;
            int count = 0;
            try { count = panel.GetChildrenCount(); }
            catch { return; }
            for (int i = 0; i < count; i++)
            {
                object child = null;
                try { child = panel.GetChildAt(i); }
                catch { }
                DumpWidgetVisuals(child, depth + 1);
            }
        }

        private static void DumpAllMembers(object node, string indent)
        {
            Type t = node.GetType();
            var members = new List<MemberInfo>();
            try { members.AddRange(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)); }
            catch { }
            try { members.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)); }
            catch { }

            foreach (MemberInfo mi in members)
            {
                PropertyInfo pi = mi as PropertyInfo;
                FieldInfo fi = mi as FieldInfo;
                Type vt = pi != null ? pi.PropertyType : fi.FieldType;
                object v = null;
                try { v = pi != null ? pi.GetValue(node, null) : fi.GetValue(node); }
                catch { continue; }

                string text;
                try
                {
                    if (v == null) text = "null";
                    else if (vt.IsPrimitive || vt == typeof(string) || vt.IsEnum) text = v.ToString();
                    else if (vt.IsValueType) text = v.ToString();
                    else text = ObjRef(v);
                }
                catch { text = "?"; }

                Log.Info("[DUMP] " + indent + mi.Name + " (" + vt.Name + ") = " + text);
            }
        }

        private static void DumpBrushMembers(object node, string indent)
        {
            if (node == null) return;
            Type t = node.GetType();

            var members = new List<MemberInfo>();
            try { members.AddRange(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)); }
            catch { }
            try { members.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)); }
            catch { }

            foreach (MemberInfo mi in members)
            {
                PropertyInfo pi = mi as PropertyInfo;
                FieldInfo fi = mi as FieldInfo;
                Type vt = pi != null ? pi.PropertyType : fi.FieldType;
                if (vt == null) continue;

                string tn = vt.Name;
                if (tn != "FSlateBrush" && tn.IndexOf("Style", StringComparison.Ordinal) < 0) continue;

                object v = null;
                try { v = pi != null ? pi.GetValue(node, null) : fi.GetValue(node); }
                catch { continue; }
                DumpBrushValue(mi.Name, v, indent);
            }
        }

        private static void DumpBrushValue(string label, object v, string indent)
        {
            if (v == null) return;
            Type t = v.GetType();

            if (t.Name == "FSlateBrush")
            {
                object res = null;
                try
                {
                    PropertyInfo rp = t.GetProperty("ResourceObject");
                    if (rp != null) res = rp.GetValue(v, null);
                }
                catch { }
                Log.Info("[DUMP] " + indent + label + " -> " + ObjRef(res));
                return;
            }

            PropertyInfo[] props;
            try { props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance); }
            catch { return; }
            foreach (PropertyInfo p in props)
            {
                if (p.PropertyType.Name != "FSlateBrush") continue;
                object bv = null;
                try { bv = p.GetValue(v, null); }
                catch { continue; }
                DumpBrushValue(label + "." + p.Name, bv, indent);
            }
        }
    }
}
