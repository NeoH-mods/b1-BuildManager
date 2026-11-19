using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using CSharpModBase;
using UnrealEngine.UMG;
using UnrealEngine.Runtime;
using UnrealEngine.Slate;
using UnrealEngine.SlateCore;

namespace BuildManager
{
    /// <summary>
    /// 装备页“配装”按钮打开的配装方案页。
    /// 左列：方案列表（默认 10 个，可新增/清空/删除）；右列：选中方案的装备/珍玩/精魄/酒/法术图标与名称。
    /// </summary>
    internal static class PresetPanel
    {
        private const int DefaultSlots = 10;
        private const int MaxSlots = 14;

        private const float PanelWidth = 3840f;
        private const float PanelHeight = 2160f;

        // 左列：右列 = 1:5。
        private const float LeftX = 210f;
        private const float LeftWidth = 560f;
        private const float RightX = 830f;
        private const float RightWidth = 2800f;

        private const float RowTop = 470f;
        private const float RowHeight = 90f;
        private const float RowPitch = 96f;

        // 整页内容整体下移，避免“配装方案”贴住顶部页签栏。
        private const float ContentShiftY = 80f;

        // 右列卡片：四行，格子放大；图标区域按贴图宽高比 contain 缩放后居中。
        private const float CardW = 400f;
        private const float CardH = 300f;
        private const float CardStepX = 430f;
        private const float CardStepY = 370f;
        private const float IconAreaX = 30f;
        private const float IconAreaY = 10f;
        private const float IconAreaW = 340f;
        private const float IconAreaH = 190f;

        // 图片下方的稀有度颜色条（对应原生装备页格子下方的色条）。
        private const float QualityBarX = 10f;
        private const float QualityBarY = 202f;
        private const float QualityBarW = CardW - 20f;
        private const float QualityBarH = 5f;

        private static readonly FLinearColor Dark = new FLinearColor(0.045f, 0.04f, 0.035f, 0.94f);
        private static readonly FLinearColor CardDark = new FLinearColor(0.022f, 0.022f, 0.022f, 1f);
        private static readonly FLinearColor Gold = new FLinearColor(0.72f, 0.55f, 0.28f, 1f);
        private static readonly FLinearColor Selected = new FLinearColor(0.18f, 0.13f, 0.06f, 0.98f);
        private static readonly FLinearColor FocusColor = new FLinearColor(0.95f, 0.78f, 0.35f, 1f);
        private static readonly FLinearColor NavColor = new FLinearColor(0.92f, 0.9f, 0.84f, 0.5f);

        // 稀有度色：按原生装备页色条采样到的 sRGB 反推为 UMG 线性色。
        private static readonly FLinearColor Quality1 = new FLinearColor(0.60f, 0.59f, 0.55f, 1f);
        private static readonly FLinearColor Quality2 = new FLinearColor(0.13f, 0.34f, 0.14f, 1f);
        internal static readonly FLinearColor Quality3 = new FLinearColor(0.058f, 0.105f, 0.138f, 1f);
        private static readonly FLinearColor Quality4 = new FLinearColor(0.141f, 0.084f, 0.352f, 1f);
        private static readonly FLinearColor Quality5 = new FLinearColor(0.68f, 0.44f, 0.165f, 1f);
        private static readonly FLinearColor Quality6 = new FLinearColor(0.93f, 0.225f, 0.115f, 1f);

        private sealed class NavItem
        {
            public string Name;
            public float X, Y, W, H;
            public Action Action;
            public int Slot;
            public Card Card;
            public int ActionIndex = -1;
        }

        /// <summary>shrine 风格的按钮/行：黑底（行）或无底（功能键），焦点时整块白色 + 原生笔刷纹理。</summary>
        private sealed class StyledButton
        {
            public UCanvasPanel Root;
            public UButton Button;
            public UBorder Back;
            public UBorder Glow;
            public UImage Ink;
            public UCanvasPanel Gradient;
            public UBorder[] GradientStrips;
            public FLinearColor[] GradientColors;
            public float W, H;
            public UTextBlock Label;
            public UCanvasPanelSlot Slot;
            public FSlateColor NormalColor;
            /// <summary>焦点金框：四条 3px 细边，默认隐藏，SetStyleHighlight 时显示。</summary>
            public UBorder[] GoldFrame;
        }

        private sealed class SlotRow
        {
            public int Slot;
            public StyledButton Style;
        }

        private sealed class Card
        {
            public UBorder Frame;
            public UCanvasPanelSlot FrameSlot;
            public UBorder Border;
            public UCanvasPanelSlot BorderSlot;
            public UImage Icon;
            public UCanvasPanelSlot IconSlot;
            public UBorder Quality;
            public UCanvasPanelSlot QualitySlot;
            public UTextBlock Label;
            public UCanvasPanelSlot LabelSlot;
            public string LastPath;
            public float IconAreaX;
            public float IconAreaY;
            public float IconAreaW;
            public float IconAreaH;
            public bool NeedsFit;

            // 详情浮框用：当前物品、类别和格子位置。
            public int ItemId;
            public string Kind;
            public float X, Y;
        }

        private static UCanvasPanel _overlay;
        private static UCanvasPanel _content;
        private static UUserWidget _owner;
        private static UWidget _styleRoot;
        private static UTextBlock _headerName;
        private static UTextBlock _headerTime;
        private static UTextBlock _emptyText;
        private static UTextBlock _statusText;
        private static UTextBlock _sectionEquip;
        private static UTextBlock _sectionFabao;
        private static UTextBlock _sectionCurio;
        private static UTextBlock _sectionHulu;
        private static UButton _addButton;
        private static StyledButton _addStyle;
        private static StyledButton _returnStyle;
        private static UTextBlock _hintRename;
        private static UTextBlock _hintBack;
        private static int _inputMode;                 // 0 = 鼠标, 1 = 手柄
        private static volatile int _padModeRequest;   // 后台 XInput 线程置 1
        private static int _lastMouseX = int.MinValue;
        private static int _lastMouseY = int.MinValue;
        private static bool _rightButtonDown;
        private static int _lastHoverSlot;
        private static int _hoverSlot;
        private static int _previewSlot;
        private static int _hoverAction = -1;
        private static int _backGuardUntil;
        private static List<int> _detailEquipIds;

        private static readonly SlotRow[] _rows = new SlotRow[MaxSlots + 1];
        private static readonly Card[] _equipCards = new Card[5];
        private static readonly Card[] _fabaoCards = new Card[2];
        private static readonly Card[] _curioCards = new Card[5];
        private static Card _gourdCard;
        private static Card _wineCard;
        private static readonly Card[] _partnerCards = new Card[4];
        private static readonly Card[] _spellCards = new Card[4];

        private static readonly List<NavItem> _nav = new List<NavItem>();
        private static readonly Queue<Action> _pending = new Queue<Action>();
        private static readonly object _pendingLock = new object();
        private static readonly UBorder[] _focusBars = new UBorder[4];
        private static readonly UCanvasPanelSlot[] _focusBarSlots = new UCanvasPanelSlot[4];
        private static readonly UBorder[] _lockBars = new UBorder[4];
        private static readonly UCanvasPanelSlot[] _lockBarSlots = new UCanvasPanelSlot[4];

        // 动作按钮：清空/删除/重命名/保存当前/应用，未选中方案时全部禁用。
        private static readonly UButton[] _actionButtons = new UButton[5];
        private static readonly UTextBlock[] _actionLabels = new UTextBlock[5];
        private static readonly StyledButton[] _actionStyles = new StyledButton[5];
        private static readonly FSlateColor[] _actionLabelColors = new FSlateColor[5];
        private static readonly FSlateColor DisabledText = MakeSlateColor(0.13f, 0.12f, 0.115f, 1f);
        private static readonly FSlateColor RowText = MakeSlateColor(0.80f, 0.76f, 0.68f, 1f);
        private static readonly FSlateColor ActionText = MakeSlateColor(0.78f, 0.66f, 0.38f, 1f);
        private static readonly FSlateColor RenameText = MakeSlateColor(1f, 1f, 1f, 1f);
        private static readonly FSlateColor CardFocusText = MakeSlateColor(0.95f, 0.78f, 0.35f, 1f);
        private static readonly FSlateColor HighlightText = MakeSlateColor(0.06f, 0.055f, 0.05f, 1f);
        private static UCanvasPanel _renameRoot;
        private static UTextBlock _renameTitle;
        private static UEditableTextBox _renameBox;
        private static StyledButton _renameConfirmStyle;
        private static StyledButton _renameCancelStyle;
        private static bool _renaming;
        private static int _renameSlot;
        private static int _renameFocus; // 0=输入框 1=确定 2=取消
        private static int _renameHover; // 0=无 1=确定 2=取消
        private static int _lastRenameNavTick;
        private static int _lastRenameNavDir = -1;
        private static int _renameFocusLogTick;
        private static int _lastClickSlot;
        private static int _lastClickTick;

        // ---------------- 多语言 / 设置面板 ----------------
        private static readonly string[] _actionKeys =
            { "action.clear", "action.delete", "action.rename", "action.save", "action.apply" };

        private static UTextBlock _titleText;
        private static StyledButton _settingsButtonStyle;
        private static bool _settingsOpen;
        private static UCanvasPanel _settingsRoot;
        private static UTextBlock _settingsTitleText;
        private static UTextBlock _settingsLangText;
        private static UTextBlock _settingsCurrentText;
        private static UTextBlock _settingsNoteText;
        private static StyledButton _settingsCloseStyle;
        private static UTextBlock _settingsStatusText;
        private static readonly StyledButton[] _settingsRows = new StyledButton[6];
        private static readonly string[] _settingsCodes = { L.Auto, "zh-Hans", "zh-Hant", "en", "ja", "ko" };
        private static int _settingsFocus;
        private static int _settingsHover;
        private static int _lastSettingsNavTick;
        private static int _lastSettingsNavDir = -1;

        public static bool IsSettingsOpen { get { return _settingsOpen; } }

        /// <summary>重命名与设置都是模态：方向键/确认/返回要优先送给它们。</summary>
        public static bool IsModalOpen { get { return _renaming || _settingsOpen; } }

        private static readonly Dictionary<int, string> _slotNames = new Dictionary<int, string>();

        private static int _focus = -1;
        private static Card _hoverCard;
        private static int _selected;
        private static int _slotCount = DefaultSlots;
        private static volatile bool _visible;
        private static int _lastConfirmTick;
        private static int _lastNavTick;
        private static int _lastNavDir = -1;

        public static bool IsOpen { get { return _visible; } }

        public static void Toggle(UUserWidget owner, UWidget styleRoot)
        {
            // 玩家可能在标题页改过游戏语言，自动模式下每次打开都重新对一次。
            L.RefreshGameLanguage();

            // 开发模式：首次打开自动补齐其它语种快照（生成一次即可随包分发）。
            MaybeGenerateAllSnapshots();
            if (_overlay == null) Build(owner, styleRoot);
            if (_overlay == null) return;

            if (_visible)
            {
                ClosePanel();
                return;
            }

            _overlay.SetVisibility(ESlateVisibility.Visible);
            GamepadNavigator.OnPanelOpened();
            _visible = true;
            _lastHoverSlot = 0;
            _hoverSlot = 0;
            _previewSlot = 0;
            // 打开面板的那次 A 可能同时触发 ButtonPress 和 Confirm 两个动作；
            // 从当前时间起算，第二次动作在 250ms 内会被 Confirm 的防抖吃掉。
            _lastConfirmTick = Environment.TickCount;
            _lastNavTick = 0;
            _lastNavDir = -1;
            // 每次打开都从“未选中”开始：必须先点击方案或按 A 确认，动作按钮才可用。
            _selected = 0;
            _renaming = false;
            _settingsOpen = false;
            _settingsHover = 0;
            if (_settingsRoot != null) _settingsRoot.SetVisibility(ESlateVisibility.Collapsed);
            // 鼠标打开时焦点可能停在别的格子上；把游戏焦点放回入口，
            // 面板的方向键/摇杆事件才会继续送到 BUI_Button。
            UIEquipPageHook.FocusEntry();
            // 每次打开都把导航焦点清掉：否则上次关面板时停在右侧格子上，
            // 重新进入后手柄上下左右会因焦点仍在格子区而无反应。
            // 清空后第一次方向键会从第一个方案行开始。
            _focus = -1;
            _hoverCard = null;
            RefreshAll();
            RefreshSelectionState();
        }

        public static void Destroy()
        {
            ClearPending();
            _visible = false;
            if (_overlay != null)
            {
                try { _overlay.RemoveFromParent(); }
                catch { }
                _overlay = null;
            }

            _owner = null;
            _styleRoot = null;
            _content = null;
            _headerName = null;
            _headerTime = null;
            _emptyText = null;
            _statusText = null;
            _sectionEquip = null;
            _sectionFabao = null;
            _sectionCurio = null;
            _sectionHulu = null;
            _addButton = null;
            _addStyle = null;
            _returnStyle = null;
            _hintRename = null;
            _hintBack = null;
            for (int i = 0; i < _actionStyles.Length; i++) _actionStyles[i] = null;
            _lastHoverSlot = 0;
            _hoverSlot = 0;
            _previewSlot = 0;
            _hoverCard = null;
            _wineCard = null;
            _gourdCard = null;
            _renaming = false;
            _renameSlot = 0;
            _renameRoot = null;
            _renameTitle = null;
            _renameBox = null;
            _renameConfirmStyle = null;
            _renameCancelStyle = null;
            _renameFocus = 0;
            _renameHover = 0;
            _lastRenameNavTick = 0;
            _lastRenameNavDir = -1;
            _settingsOpen = false;
            _settingsRoot = null;
            _settingsTitleText = null;
            _settingsLangText = null;
            _settingsCurrentText = null;
            _settingsNoteText = null;
            _settingsCloseStyle = null;
            _settingsStatusText = null;
            _settingsButtonStyle = null;
            if (_snapshotRunning) CancelSnapshot(true);
            _titleText = null;
            for (int i = 0; i < _settingsRows.Length; i++) _settingsRows[i] = null;
            try { L.Changed -= OnLanguageChanged; } catch { }
            ItemDetailPopup.Destroy();
            for (int i = 0; i < _actionButtons.Length; i++) _actionButtons[i] = null;
            for (int i = 0; i < _rows.Length; i++) _rows[i] = null;
            for (int i = 0; i < _equipCards.Length; i++) _equipCards[i] = null;
            for (int i = 0; i < _fabaoCards.Length; i++) _fabaoCards[i] = null;
            for (int i = 0; i < _curioCards.Length; i++) _curioCards[i] = null;
            for (int i = 0; i < _partnerCards.Length; i++) _partnerCards[i] = null;
            for (int i = 0; i < _spellCards.Length; i++) _spellCards[i] = null;
            for (int i = 0; i < _focusBars.Length; i++)
            {
                _focusBars[i] = null;
                _focusBarSlots[i] = null;
                _lockBars[i] = null;
                _lockBarSlots[i] = null;
            }
            _focus = -1;
            _nav.Clear();
        }

        private static void Build(UUserWidget owner, UWidget styleRoot)
        {
            try
            {
                UCanvasPanel pageRoot = b1.UGSE_UMGFuncLib.GetRootWidget(owner) as UCanvasPanel;
                if (pageRoot == null)
                {
                    Log.Warn("[BuildManager] preset panel: page root is not UCanvasPanel");
                    return;
                }

                _owner = owner;
                _styleRoot = styleRoot;
                _nav.Clear();
                _focus = -1;
                _slotCount = LoadSlotCount();
                LoadSlotNames();
                _selected = 0;

                _overlay = UObject.NewObject<UCanvasPanel>(owner);
                UCanvasPanelSlot overlaySlot = pageRoot.AddChildToCanvas(_overlay);
                overlaySlot.SetPosition(new FVector2D(0f, 0f));
                overlaySlot.SetSize(new FVector2D(PanelWidth, PanelHeight));
                overlaySlot.SetZOrder(500);

                UBorder background = UObject.NewObject<UBorder>(_overlay);
                background.SetBrushColor(new FLinearColor(0.015f, 0.012f, 0.01f, 0.94f));
                UCanvasPanelSlot backgroundSlot = _overlay.AddChildToCanvas(background);
                backgroundSlot.SetPosition(new FVector2D(0f, 0f));
                backgroundSlot.SetSize(new FVector2D(PanelWidth, PanelHeight));
                backgroundSlot.SetZOrder(0);

                // 内容层整体下移，背景仍铺满整屏。
                _content = UObject.NewObject<UCanvasPanel>(_overlay);
                UCanvasPanelSlot contentSlot = _overlay.AddChildToCanvas(_content);
                contentSlot.SetPosition(new FVector2D(0f, ContentShiftY));
                contentSlot.SetSize(new FVector2D(PanelWidth, PanelHeight - ContentShiftY));
                contentSlot.SetZOrder(1);

                // 导航焦点框（手柄/点击落点）：较暗的细边。
                for (int i = 0; i < _focusBars.Length; i++)
                {
                    _focusBars[i] = UObject.NewObject<UBorder>(_content);
                    _focusBars[i].SetBrushColor(NavColor);
                    _focusBarSlots[i] = _content.AddChildToCanvas(_focusBars[i]);
                    _focusBarSlots[i].SetPosition(new FVector2D(0f, 0f));
                    _focusBarSlots[i].SetSize(new FVector2D(1f, 1f));
                    _focusBarSlots[i].SetZOrder(30);
                    _focusBars[i].SetVisibility(ESlateVisibility.Collapsed);
                }

                // 已锁定（选中）方案的亮金框，只有点击/手柄 A 才会移动。
                for (int i = 0; i < _lockBars.Length; i++)
                {
                    _lockBars[i] = UObject.NewObject<UBorder>(_content);
                    _lockBars[i].SetBrushColor(FocusColor);
                    _lockBarSlots[i] = _content.AddChildToCanvas(_lockBars[i]);
                    _lockBarSlots[i].SetPosition(new FVector2D(0f, 0f));
                    _lockBarSlots[i].SetSize(new FVector2D(1f, 1f));
                    _lockBarSlots[i].SetZOrder(32);
                    _lockBars[i].SetVisibility(ESlateVisibility.Collapsed);
                }

                _titleText = AddCenteredText(L.T("panel.title"), LeftX, 140f, LeftWidth, 80f);

                // 操作区：功能键本身无底，焦点/悬停时白底黑字（土地庙风格）。
                _actionStyles[0] = AddStyledButton(_content, L.T("action.clear"), 210f, 250f, 170f, 80f,
                    new FOnButtonPressedEvent.Signature(ClearSelectedStatic), false, ActionText, 0);
                _actionStyles[1] = AddStyledButton(_content, L.T("action.delete"), 390f, 250f, 170f, 80f,
                    new FOnButtonPressedEvent.Signature(DeleteSelectedStatic), false, ActionText, 1);
                _actionStyles[2] = AddStyledButton(_content, L.T("action.rename"), 570f, 250f, 170f, 80f,
                    new FOnButtonPressedEvent.Signature(RenameSelectedStatic), false, ActionText, 2);
                _actionStyles[3] = AddStyledButton(_content, L.T("action.save"), LeftX, 340f, 270f, 80f,
                    new FOnButtonPressedEvent.Signature(SaveSelectedStatic), false, ActionText, 3);
                _actionStyles[4] = AddStyledButton(_content, L.T("action.apply"), LeftX + 290f, 340f, 270f, 80f,
                    new FOnButtonPressedEvent.Signature(ApplySelectedStatic), false, ActionText, 4);
                for (int i = 0; i < _actionStyles.Length; i++)
                {
                    if (_actionStyles[i] == null) continue;
                    _actionButtons[i] = _actionStyles[i].Button;
                    _actionLabels[i] = _actionStyles[i].Label;
                    _actionLabelColors[i] = _actionStyles[i].NormalColor;
                }

                // 左列方案行（预建 14 行，按数量显示）：默认黑底，选中/悬停/焦点白底。
                for (int n = 1; n <= MaxSlots; n++)
                {
                    int slot = n;
                    StyledButton rowStyle = AddStyledButton(_content, L.F("slot.name", n),
                        LeftX, RowTop + (n - 1) * RowPitch, LeftWidth, RowHeight,
                        GetSlotDelegate(n), true, RowText, -1);
                    rowStyle.Button.OnHovered.Bind(GetHoverDelegate(n));
                    _rows[n] = new SlotRow { Slot = n, Style = rowStyle };
                }

                _addStyle = AddStyledButton(_content, "＋", LeftX, RowTop + _slotCount * RowPitch,
                    LeftWidth, RowHeight, new FOnButtonPressedEvent.Signature(AddSlotStatic),
                    true, RowText, -1);
                _addButton = _addStyle.Button;

                // 返回放在列表下方；再下面两行是随输入设备切换的快捷键提示。
                // 位置随槽位数量下移（ApplyLayout 每次刷新）。
                _returnStyle = AddStyledButton(_content, L.T("action.back"), LeftX, ReturnY(), LeftWidth, 80f,
                    new FOnButtonPressedEvent.Signature(ClosePanelStatic), false, ActionText, 5);
                _hintRename = AddText("", LeftX, ReturnY() + 106f, LeftWidth, 38f, ETextJustify.Center, 10);
                _hintBack = AddText("", LeftX, ReturnY() + 164f, LeftWidth, 38f, ETextJustify.Center, 10);
                ApplyHintStyle(_hintRename);
                ApplyHintStyle(_hintBack);
                RefreshHints();

                // 右列：标题、时间、分区和图标卡片。
                // ① 武器+防具 ② 珍玩 ③ 法宝/精魄/法术（6 格）④ 葫芦/酒/泡酒物（6 格）。
                _headerName = AddText("", RightX, 200f, 1500f, 80f, ETextJustify.Left, 10);
                _headerTime = AddText("", RightX, 290f, 1500f, 50f, ETextJustify.Left, 10);
                _sectionEquip = AddText(L.T("section.equip"), 950f, 375f, 1200f, 50f, ETextJustify.Left, 10);
                _sectionCurio = AddText(L.T("section.curio"), 950f, 745f, 1200f, 50f, ETextJustify.Left, 10);
                _sectionFabao = AddText(L.T("section.fabao"), 950f, 1115f, 1600f, 50f, ETextJustify.Left, 10);
                _sectionHulu = AddText(L.T("section.hulu"), 950f, 1485f, 1600f, 50f, ETextJustify.Left, 10);

                // 右上角设置入口：只放语言，后续设置项往里加。
                _settingsButtonStyle = AddStyledButton(_content, L.T("settings.title"), 3460f, 108f, 280f, 84f,
                    new FOnButtonPressedEvent.Signature(OpenSettingsStatic), false, ActionText, -1);

                for (int i = 0; i < _equipCards.Length; i++)
                    _equipCards[i] = CreateCard(950f + i * CardStepX, 430f);
                for (int i = 0; i < _curioCards.Length; i++)
                    _curioCards[i] = CreateCard(950f + i * CardStepX, 800f);
                _fabaoCards[0] = CreateCard(950f, 1170f);
                _fabaoCards[1] = CreateCard(950f + CardStepX, 1170f);
                for (int i = 0; i < _spellCards.Length; i++)
                    _spellCards[i] = CreateCard(950f + (2 + i) * CardStepX, 1170f);
                _gourdCard = CreateCard(950f, 1540f);
                _wineCard = CreateCard(950f + CardStepX, 1540f);
                for (int i = 0; i < _partnerCards.Length; i++)
                    _partnerCards[i] = CreateCard(950f + (2 + i) * CardStepX, 1540f);

                ItemDetailPopup.Build(_content, _styleRoot);

                _emptyText = AddText("", RightX, 780f, RightWidth, 160f, ETextJustify.Center, 10);
                _emptyText.SetAutoWrapText(true);
                _emptyText.SetVisibility(ESlateVisibility.Collapsed);

                _statusText = AddText("", RightX, 1900f, RightWidth, 60f, ETextJustify.Left, 10);
                _statusText.SetAutoWrapText(true);

                BuildRenameDialog();
                BuildSettingsDialog();

                // 语言表在设置里切换后，所有静态文案立刻重刷。
                L.Changed -= OnLanguageChanged;
                L.Changed += OnLanguageChanged;
                UpdateActionStates();

                _overlay.SetVisibility(ESlateVisibility.Collapsed);
                ApplyLayout();
                BuildNav();
                RefreshAll();
                RefreshSelectionState();
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] preset panel build failed: " + ex);
                _overlay = null;
            }
        }

        private static Card CreateCard(float x, float y)
        {
            var card = new Card();
            card.X = x;
            card.Y = y;
            card.IconAreaX = x + IconAreaX;
            card.IconAreaY = y + IconAreaY;
            card.IconAreaW = IconAreaW;
            card.IconAreaH = IconAreaH;

            card.Frame = UObject.NewObject<UBorder>(_content);
            card.Frame.SetBrushColor(CardDark);
            card.FrameSlot = _content.AddChildToCanvas(card.Frame);
            card.FrameSlot.SetPosition(new FVector2D(x, y));
            card.FrameSlot.SetSize(new FVector2D(CardW, CardH));
            card.FrameSlot.SetZOrder(5);

            // 金色描边：放在不透明底框下方 3 单位，底框盖住中心后只露出外圈。
            // HitTestInvisible 保证不抢 Frame 的悬停判定。
            card.Border = UObject.NewObject<UBorder>(_content);
            card.Border.SetBrushColor(FocusColor);
            card.BorderSlot = _content.AddChildToCanvas(card.Border);
            card.BorderSlot.SetPosition(new FVector2D(x - 3f, y - 3f));
            card.BorderSlot.SetSize(new FVector2D(CardW + 6f, CardH + 6f));
            card.BorderSlot.SetZOrder(4);
            card.Border.SetVisibility(ESlateVisibility.Collapsed);

            card.Icon = UObject.NewObject<UImage>(_content);
            card.IconSlot = _content.AddChildToCanvas(card.Icon);
            card.IconSlot.SetPosition(new FVector2D(card.IconAreaX, card.IconAreaY));
            card.IconSlot.SetSize(new FVector2D(card.IconAreaW, card.IconAreaH));
            card.IconSlot.SetZOrder(6);

            card.Quality = UObject.NewObject<UBorder>(_content);
            card.Quality.SetBrushColor(Quality3);
            card.QualitySlot = _content.AddChildToCanvas(card.Quality);
            card.QualitySlot.SetPosition(new FVector2D(x + QualityBarX, y + QualityBarY));
            card.QualitySlot.SetSize(new FVector2D(QualityBarW, QualityBarH));
            card.QualitySlot.SetZOrder(7);

            card.Label = UObject.NewObject<UTextBlock>(_content);
            UIEquipPageHook.TryCopyTextStyle(_styleRoot, card.Label);
            card.Label.SetJustification(ETextJustify.Center);
            card.Label.SetAutoWrapText(true);
            card.Label.SetColorAndOpacity(RenameText);
            card.LabelSlot = _content.AddChildToCanvas(card.Label);
            card.LabelSlot.SetPosition(new FVector2D(x + 10f, y + 210f));
            card.LabelSlot.SetSize(new FVector2D(CardW - 20f, 80f));
            card.LabelSlot.SetZOrder(7);

            SetCardVisible(card, false);
            return card;
        }

        private static void SetCardVisible(Card card, bool visible)
        {
            if (card == null) return;
            ESlateVisibility v = visible ? ESlateVisibility.Visible : ESlateVisibility.Collapsed;
            card.Frame.SetVisibility(v);
            card.Icon.SetVisibility(v);
            card.Quality.SetVisibility(v);
            card.Label.SetVisibility(v);
            // 描边由 SetCardFocus 单独控制，刷新内容时先收起来，避免残留旧焦点。
            if (card.Border != null) card.Border.SetVisibility(ESlateVisibility.Collapsed);
        }

        private static void HideCard(Card card)
        {
            if (card == null) return;
            card.ItemId = 0;
            card.Kind = null;
            SetCardVisible(card, false);
        }

        private static void SetCard(Card card, int itemId, string name, string kind)
        {
            if (card == null) return;
            if (itemId <= 0 || string.IsNullOrEmpty(name))
            {
                HideCard(card);
                return;
            }

            card.ItemId = itemId;
            card.Kind = kind;
            SetCardVisible(card, true);
            card.Label.SetText(FText.FromString(name));
            SetCardQuality(card, itemId);

            string path = null;
            try
            {
                path = B1UI.GSUI.GSUIResPathUtil.GetItemIconTPath(itemId);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] icon path failed id=" + itemId + ": " + ex.Message);
            }

            if (string.IsNullOrEmpty(path) || path == card.LastPath) return;

            card.LastPath = path;
            card.NeedsFit = true;
            // 新贴图加载前先回到预留区域，避免沿用上一张图的缩放尺寸。
            card.IconSlot.SetPosition(new FVector2D(card.IconAreaX, card.IconAreaY));
            card.IconSlot.SetSize(new FVector2D(card.IconAreaW, card.IconAreaH));

            try
            {
                b1.UIAssetLoadHelper.AsyncLoadTexForImage(_owner, card.Icon, path, true, true);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] icon load failed id=" + itemId + ": " + ex.Message);
            }
        }

        /// <summary>按 ItemDesc.f6 给卡片下方的稀有度条上色；查不到稀有度时隐藏色条。</summary>
        private static void SetCardQuality(Card card, int itemId)
        {
            int quality = GameData.ItemQuality(itemId);
            FLinearColor color = QualityColor(quality);
            if (color.A <= 0f)
            {
                card.Quality.SetVisibility(ESlateVisibility.Collapsed);
                return;
            }

            card.Quality.SetBrushColor(color);
            card.Quality.SetVisibility(ESlateVisibility.Visible);
        }

        private static FLinearColor QualityColor(int quality)
        {
            switch (quality)
            {
                case 1: return Quality1;
                case 2: return Quality2;
                case 3: return Quality3;
                case 4: return Quality4;
                case 5: return Quality5;
                case 6: return Quality6;
                default: return new FLinearColor(0f, 0f, 0f, 0f);
            }
        }

        /// <summary>
        /// 贴图加载是异步的：加载完成后读取贴图原始尺寸，按 contain 方式缩放并居中，
        /// 既保持宽高比又尽量铺满格子预留的图标区域。
        /// </summary>
        public static void ProcessIconFit()
        {
            if (!_visible || _content == null) return;

            for (int i = 0; i < _equipCards.Length; i++) FitCard(_equipCards[i]);
            for (int i = 0; i < _fabaoCards.Length; i++) FitCard(_fabaoCards[i]);
            for (int i = 0; i < _curioCards.Length; i++) FitCard(_curioCards[i]);
            FitCard(_gourdCard);
            FitCard(_wineCard);
            for (int i = 0; i < _partnerCards.Length; i++) FitCard(_partnerCards[i]);
            for (int i = 0; i < _spellCards.Length; i++) FitCard(_spellCards[i]);
        }

        private static void FitCard(Card card)
        {
            if (card == null || !card.NeedsFit) return;

            try
            {
                if (card.Icon.GetVisibility() != ESlateVisibility.Visible)
                {
                    card.NeedsFit = false;
                    return;
                }

                FVector2D texture = card.Icon.Brush.ImageSize;
                if (texture.X < 2f || texture.Y < 2f) return; // 还没加载完

                float scale = Math.Min(card.IconAreaW / texture.X, card.IconAreaH / texture.Y);
                float width = texture.X * scale;
                float height = texture.Y * scale;

                card.IconSlot.SetPosition(new FVector2D(
                    card.IconAreaX + (card.IconAreaW - width) * 0.5f,
                    card.IconAreaY + (card.IconAreaH - height) * 0.5f));
                card.IconSlot.SetSize(new FVector2D(width, height));

                FSlateBrush brush = card.Icon.Brush;
                brush.ImageSize = new FVector2D(width, height);
                card.Icon.Brush = brush;

                card.NeedsFit = false;
                Log.Debug("[BuildManager] icon fit " + texture.X + "x" + texture.Y +
                          " -> " + width.ToString("0") + "x" + height.ToString("0"));
            }
            catch (Exception ex)
            {
                card.NeedsFit = false;
                Log.Warn("[BuildManager] icon fit failed: " + ex.Message);
            }
        }

        private static void RefreshAll()
        {
            if (_overlay == null) return;
            ApplyLayout();
            RefreshRows();
            RefreshDetails();
        }

        /// <summary>“返回”按钮的 Y：紧跟在方案列表（含“+”行）下方，随槽位数量移动。</summary>
        private static float ReturnY()
        {
            // 返回按钮与右列最下方一排物品格子的垂直中心对齐；槽位很多时再往下让位。
            int rows = _slotCount + (_slotCount < MaxSlots ? 1 : 0);
            return Math.Max(1650f, RowTop + rows * RowPitch + 24f);
        }

        private static void MoveTo(UWidget widget, float x, float y)
        {
            if (widget == null) return;
            UCanvasPanelSlot slot = widget.Slot as UCanvasPanelSlot;
            if (slot != null) slot.SetPosition(new FVector2D(x, y));
        }

        private static void ApplyLayout()
        {
            if (_overlay == null) return;

            for (int n = 1; n <= MaxSlots; n++)
            {
                SlotRow row = _rows[n];
                if (row == null) continue;
                bool visible = n <= _slotCount;
                // 隐藏整个容器：视觉层和透明按钮都在容器里，只隐藏按钮会留下黑底和文字。
                row.Style.Root.SetVisibility(visible ? ESlateVisibility.Visible : ESlateVisibility.Collapsed);
                UCanvasPanelSlot slot = row.Style.Slot as UCanvasPanelSlot;
                if (slot != null)
                {
                    slot.SetPosition(new FVector2D(LeftX, RowTop + (n - 1) * RowPitch));
                }
            }

            if (_addButton != null)
            {
                bool showAdd = _slotCount < MaxSlots;
                _addStyle.Root.SetVisibility(showAdd ? ESlateVisibility.Visible : ESlateVisibility.Collapsed);
                UCanvasPanelSlot slot = _addStyle.Slot as UCanvasPanelSlot;
                if (slot != null)
                {
                    slot.SetPosition(new FVector2D(LeftX, RowTop + _slotCount * RowPitch));
                }
            }

            float returnY = ReturnY();
            if (_returnStyle != null)
            {
                _returnStyle.Root.SetVisibility(ESlateVisibility.Visible);
                MoveTo(_returnStyle.Root, LeftX, returnY);
            }
            MoveTo(_hintRename, LeftX, returnY + 106f);
            MoveTo(_hintBack, LeftX, returnY + 164f);
        }

        private static void RefreshRows()
        {
            for (int n = 1; n <= MaxSlots; n++)
            {
                SlotRow row = _rows[n];
                if (row == null) continue;
                row.Style.Label.SetText(FText.FromString(DisplayName(n)));
            }
            RefreshButtonStyles();
        }

        private static void RefreshDetails()
        {
            if (_overlay == null) return;

            // 详情浮框跟着右列内容走：重新渲染时先收起，下次悬停会按新方案重建套装件数。
            ItemDetailPopup.Hide();
            _detailEquipIds = null;

            // 右列显示“预览槽位”（鼠标悬停/手柄焦点）优先，没有预览时回到锁定的槽位。
            int slot = _previewSlot > 0 && _previewSlot <= _slotCount ? _previewSlot : _selected;
            if (slot <= 0)
            {
                _headerName.SetText(FText.FromString(""));
                _headerTime.SetText(FText.FromString(""));
                _emptyText.SetText(FText.FromString(
                    L.T("empty.select")));
                _emptyText.SetVisibility(ESlateVisibility.Visible);
                SetSectionsVisible(false);
                HideAllCards();
                return;
            }

            string presetName = DisplayName(slot);
            _headerName.SetText(FText.FromString(presetName));

            PresetData data = PresetStore.Exists("preset" + slot)
                ? PresetStore.Load("preset" + slot)
                : null;

            if (data == null)
            {
                _headerTime.SetText(FText.FromString(L.T("empty.slot")));
                _emptyText.SetText(FText.FromString(
                    L.F("empty.slot_hint", presetName)));
                _emptyText.SetVisibility(ESlateVisibility.Visible);
                SetSectionsVisible(false);
                HideAllCards();
                return;
            }

            _emptyText.SetVisibility(ESlateVisibility.Collapsed);
            SetSectionsVisible(true);
            _headerTime.SetText(FText.FromString(string.IsNullOrEmpty(data.SavedAt) ? L.T("empty.saved") : data.SavedAt));

            _detailEquipIds = new List<int>();
            foreach (PresetEquip presetEquip in data.Equips)
            {
                if (presetEquip != null && presetEquip.EquipId > 0) _detailEquipIds.Add(presetEquip.EquipId);
            }

            var equipById = new Dictionary<int, int>();
            foreach (PresetEquip equip in data.Equips)
            {
                if (equip != null && equip.EquipId > 0) equipById[equip.Slot] = equip.EquipId;
            }

            // 第一行：武器 + 防具（不含法宝和葫芦）。
            int[] equipOrder = { 5, 0, 1, 2, 3 };
            for (int i = 0; i < _equipCards.Length; i++)
            {
                int id;
                if (equipById.TryGetValue(equipOrder[i], out id))
                {
                    SetCard(_equipCards[i], id, EquipDisplay(id), L.T("card.cat.equip"));
                }
                else
                {
                    HideCard(_equipCards[i]);
                }
            }

            // 第二行：珍玩。
            for (int i = 0; i < _curioCards.Length; i++) HideCard(_curioCards[i]);
            foreach (PresetAccessory accessory in data.Accessories)
            {
                if (accessory == null || accessory.Pos < 0 || accessory.Pos >= _curioCards.Length) continue;
                SetCard(_curioCards[accessory.Pos], accessory.EquipId, EquipDisplay(accessory.EquipId), L.T("card.cat.curio"));
            }

            // 第三行：法宝 / 精魄 / 四个法术（共 6 格）。
            int fabaoId;
            if (equipById.TryGetValue(6, out fabaoId))
                SetCard(_fabaoCards[0], fabaoId, EquipDisplay(fabaoId), L.T("card.cat.fabao"));
            else
                HideCard(_fabaoCards[0]);

            if (data.SoulSkill != null && data.SoulSkill.SoulSkillId > 0)
                SetCard(_fabaoCards[1], data.SoulSkill.SoulSkillId, SoulDisplay(data.SoulSkill.SoulSkillId), L.T("card.cat.soul"));
            else
                HideCard(_fabaoCards[1]);

            for (int i = 0; i < _spellCards.Length; i++) HideCard(_spellCards[i]);
            int spellCard = 0;
            foreach (PresetSpell spell in data.Spells)
            {
                if (spell == null || spell.SpellId <= 0) continue;
                if (spellCard >= _spellCards.Length) break;
                SetCard(_spellCards[spellCard], spell.SpellId, SpellDisplay(spell.SpellId), L.T("card.cat.spell"));
                spellCard++;
            }

            // 第四行：葫芦 / 酒 / 泡酒物。
            int gourdId;
            if (equipById.TryGetValue(4, out gourdId))
                SetCard(_gourdCard, gourdId, EquipDisplay(gourdId), L.T("card.cat.gourd"));
            else
                HideCard(_gourdCard);

            if (data.Wine != null && data.Wine.WineId > 0)
                SetCard(_wineCard, data.Wine.WineId, EquipDisplay(data.Wine.WineId), L.T("card.cat.wine"));
            else
                HideCard(_wineCard);

            for (int i = 0; i < _partnerCards.Length; i++) HideCard(_partnerCards[i]);
            if (data.Wine != null && data.Wine.Partners != null)
            {
                var partners = new List<PresetWinePartner>(data.Wine.Partners);
                partners.Sort((a, b) => a.Pos.CompareTo(b.Pos));
                for (int i = 0; i < partners.Count && i < _partnerCards.Length; i++)
                {
                    int itemId = partners[i].ItemId;
                    SetCard(_partnerCards[i], itemId, EquipDisplay(itemId), L.T("card.cat.partner"));
                }
            }
        }

        private static void HideAllCards()
        {
            for (int i = 0; i < _equipCards.Length; i++) HideCard(_equipCards[i]);
            for (int i = 0; i < _fabaoCards.Length; i++) HideCard(_fabaoCards[i]);
            for (int i = 0; i < _curioCards.Length; i++) HideCard(_curioCards[i]);
            HideCard(_gourdCard);
            HideCard(_wineCard);
            for (int i = 0; i < _partnerCards.Length; i++) HideCard(_partnerCards[i]);
            for (int i = 0; i < _spellCards.Length; i++) HideCard(_spellCards[i]);
        }

        private static void SetSectionsVisible(bool visible)
        {
            ESlateVisibility v = visible ? ESlateVisibility.Visible : ESlateVisibility.Collapsed;
            if (_sectionEquip != null) _sectionEquip.SetVisibility(v);
            if (_sectionFabao != null) _sectionFabao.SetVisibility(v);
            if (_sectionCurio != null) _sectionCurio.SetVisibility(v);
            if (_sectionHulu != null) _sectionHulu.SetVisibility(v);
        }

        private static string EquipDisplay(int id)
        {
            string name = GameData.LocalizedName(id);
            if (string.IsNullOrEmpty(name)) name = GameData.RawEquipName(id);
            return string.IsNullOrEmpty(name) ? L.T("unknown") : name;
        }

        private static string SoulDisplay(int id)
        {
            string name = GameData.LocalizedName(id);
            if (string.IsNullOrEmpty(name)) name = GameData.RawSoulName(id);
            return string.IsNullOrEmpty(name) ? L.T("unknown.soul") : name;
        }

        private static string SpellDisplay(int id)
        {
            string name = GameData.LocalizedName(id);
            if (string.IsNullOrEmpty(name)) name = GameData.RawSpellName(id);
            return string.IsNullOrEmpty(name) ? L.T("unknown.spell") : name;
        }

        /// <summary>点击/手柄 A：选中方案；再次点击已选中的方案则取消选中。</summary>
        private static void SelectSlot(int slot, bool toggle = true)
        {
            if (slot < 1 || slot > _slotCount) return;
            if (toggle && _selected == slot)
            {
                DeselectSlot();
                return;
            }

            _selected = slot;
            _previewSlot = 0;
            _hoverSlot = 0;
            RefreshRows();
            RefreshDetails();
            FocusSlot(slot);
            RefreshSelectionState();
        }

        /// <summary>取消选中：回到“未选择方案”状态，动作按钮文字变灰黑。</summary>
        private static void DeselectSlot()
        {
            _selected = 0;
            _previewSlot = 0;
            _hoverSlot = 0;
            RefreshRows();
            RefreshDetails();
            RefreshSelectionState();
        }

        /// <summary>未选中方案时隐藏锁定框、置灰动作按钮。</summary>
        private static void RefreshSelectionState()
        {
            // 不再使用几何边框：选中/焦点由白色底块表示。
            HideLockBars();
            UpdateActionStates();
        }

        private static void UpdateActionStates()
        {
            bool enabled = _selected > 0;
            for (int i = 0; i < _actionButtons.Length; i++)
            {
                UButton button = _actionButtons[i];
                if (button == null) continue;
                try
                {
                    // 未选中时只把文字改成灰黑；点击仍由 RequireSelection 兜底。
                    button.SetIsEnabled(enabled);
                    if (_actionStyles[i] != null)
                    {
                        _actionStyles[i].NormalColor = enabled ? ActionText : DisabledText;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] action state failed: " + ex.Message);
                }
            }
            RefreshButtonStyles();
        }

        private static FSlateColor MakeSlateColor(float r, float g, float b, float a)
        {
            var color = new FSlateColor();
            color.SpecifiedColor = new FLinearColor(r, g, b, a);
            return color;
        }

        private static void PlaceLockBars(int slot)
        {
            if (slot < 1 || slot > _slotCount) return;
            float y = RowTop + (slot - 1) * RowPitch;
            const float t = 5f;
            SetLockBar(0, LeftX - t, y - t, LeftWidth + t * 2f, t);
            SetLockBar(1, LeftX - t, y + RowHeight, LeftWidth + t * 2f, t);
            SetLockBar(2, LeftX - t, y, t, RowHeight);
            SetLockBar(3, LeftX + LeftWidth, y, t, RowHeight);
        }

        private static void SetLockBar(int index, float x, float y, float width, float height)
        {
            if (_lockBarSlots[index] == null) return;
            _lockBarSlots[index].SetPosition(new FVector2D(x, y));
            _lockBarSlots[index].SetSize(new FVector2D(width, height));
            _lockBars[index].SetVisibility(ESlateVisibility.HitTestInvisible);
        }

        private static void HideLockBars()
        {
            for (int i = 0; i < _lockBars.Length; i++)
            {
                if (_lockBars[i] != null) _lockBars[i].SetVisibility(ESlateVisibility.Collapsed);
            }
        }

        /// <summary>鼠标悬停只预览右列，不改变黄框选中的槽位。</summary>
        private static void PreviewSlot(int slot)
        {
            if (!_visible || slot < 1 || slot > _slotCount) return;
            if (_previewSlot == slot) return;
            _previewSlot = slot;
            _hoverSlot = slot;
            RefreshRows();
            RefreshDetails();
        }

        /// <summary>鼠标离开方案行后，右列回到黄框选中的槽位。</summary>
        private static void ClearPreview()
        {
            _hoverSlot = 0;
            if (_previewSlot == 0) return;
            _previewSlot = 0;
            RefreshRows();
            RefreshDetails();
        }

        private static void FocusSlot(int slot)
        {
            for (int i = 0; i < _nav.Count; i++)
            {
                if (_nav[i].Slot != slot) continue;
                SetFocus(i);
                return;
            }
        }

        private static void SaveSelectedStatic() { SaveSelected(); }
        private static void ApplySelectedStatic() { ApplySelected(); }
        private static void ClearSelectedStatic() { ClearSelected(); }
        private static void DeleteSelectedStatic() { DeleteSelected(); }
        private static void AddSlotStatic() { AddSlot(); }
        private static void RenameSelectedStatic() { RenameSelected(); }
        private static void ConfirmRenameStatic() { ConfirmRename(); }
        private static void CancelRenameStatic() { CancelRename(); }

        private static bool RequireSelection()
        {
            if (_selected > 0) return true;
            Status(L.T("status.select_first"));
            return false;
        }

        private static void SaveSelected()
        {
            if (!RequireSelection()) return;
            Log.Info("[BuildManager] action: save slot=" + _selected);
            if (Sequencer.IsRunning)
            {
                Status(L.T("status.swapping"));
                return;
            }

            ClearPreview();
            PresetService.SavePreset(_selected.ToString(), DisplayName(_selected));
            RefreshAll();
            Status(L.F("status.saved", DisplayName(_selected)));
        }

        private static void ApplySelected()
        {
            if (!RequireSelection()) return;
            Log.Info("[BuildManager] action: apply slot=" + _selected);
            ClearPreview();
            PresetService.ApplyPreset(_selected.ToString());
            Status(L.F("status.applied", DisplayName(_selected)));
        }

        private static void ClearSelected()
        {
            if (!RequireSelection()) return;
            Log.Info("[BuildManager] action: clear slot=" + _selected);
            ClearPreview();
            if (!PresetStore.Exists("preset" + _selected))
            {
                Status(L.F("status.already_empty", DisplayName(_selected)));
                return;
            }

            if (PresetStore.Delete("preset" + _selected))
            {
                RefreshAll();
                Status(L.F("status.cleared", DisplayName(_selected)));
            }
        }

        private static void AddSlot()
        {
            Log.Info("[BuildManager] action: add slot (count=" + _slotCount + ")");
            if (_slotCount >= MaxSlots)
            {
                Status(L.F("status.max_slots", MaxSlots));
                return;
            }

            _slotCount++;
            _selected = _slotCount;
            SaveSlotMeta();
            ApplyLayout();
            BuildNav();
            RefreshAll();
            SelectSlot(_selected, false);
            Status(L.F("status.added", DisplayName(_selected)));
        }

        private static void DeleteSelected()
        {
            if (!RequireSelection()) return;
            Log.Info("[BuildManager] action: delete slot=" + _selected);
            if (_slotCount <= 1)
            {
                ClearSelected();
                return;
            }

            if (Sequencer.IsRunning)
            {
                Status(L.T("status.swapping_delete"));
                return;
            }

            ClearPreview();
            PresetStore.Delete("preset" + _selected);

            // 后面的槽位整体前移，保持配装1..N 连续；自定义名称一起前移。
            for (int i = _selected; i < _slotCount; i++)
            {
                MovePresetFile(i + 1, i);
                string moved;
                if (_slotNames.TryGetValue(i + 1, out moved)) _slotNames[i] = moved;
                else _slotNames.Remove(i);
            }
            _slotNames.Remove(_slotCount);

            _slotCount--;
            _selected = Math.Min(_selected, _slotCount);
            SaveSlotMeta();
            ApplyLayout();
            BuildNav();
            RefreshAll();
            SelectSlot(_selected, false);
            Status(L.F("status.deleted", _slotCount));
        }

        private static void MovePresetFile(int from, int to)
        {
            string src = PresetStore.PathFor("preset" + from);
            string dst = PresetStore.PathFor("preset" + to);
            try
            {
                if (File.Exists(src))
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                }
                else if (File.Exists(dst))
                {
                    File.Delete(dst);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] move preset " + from + "->" + to + " failed: " + ex);
            }
        }

        private static void Status(string message)
        {
            if (_statusText != null) _statusText.SetText(FText.FromString(message));
            Log.Info("[BuildManager] " + message);
        }

        // ---------------- 槽位数量持久化 ----------------

        private static string SlotsPath
        {
            get { return Path.Combine(ModPaths.PresetsDir, "slots.json"); }
        }

        private static int LoadSlotCount()
        {
            try
            {
                if (File.Exists(SlotsPath))
                {
                    Dictionary<string, object> root = Json.AsObject(
                        Json.Parse(File.ReadAllText(SlotsPath, Encoding.UTF8)));
                    int count = Json.ToInt(Json.Get(root, "Count"));
                    if (count > 0) return Math.Max(1, Math.Min(MaxSlots, count));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] slots.json load failed: " + ex.Message);
            }

            int highest = 0;
            for (int i = 1; i <= MaxSlots; i++)
            {
                if (PresetStore.Exists("preset" + i)) highest = i;
            }
            return Math.Max(DefaultSlots, highest);
        }

        private static void SaveSlotMeta()
        {
            try
            {
                string dir = ModPaths.PresetsDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder(256);
                sb.Append("{\"Count\":").Append(_slotCount);
                sb.Append(",\"Names\":{");
                bool first = true;
                for (int i = 1; i <= _slotCount; i++)
                {
                    string name;
                    if (!_slotNames.TryGetValue(i, out name) || string.IsNullOrEmpty(name)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(i).Append("\":").Append(Json.Escape(name));
                }
                sb.Append("}}");

                File.WriteAllText(SlotsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] slots.json save failed: " + ex.Message);
            }
        }

        private static void LoadSlotNames()
        {
            _slotNames.Clear();
            try
            {
                if (!File.Exists(SlotsPath)) return;
                Dictionary<string, object> root = Json.AsObject(
                    Json.Parse(File.ReadAllText(SlotsPath, Encoding.UTF8)));
                Dictionary<string, object> names = Json.AsObject(Json.Get(root, "Names"));
                if (names == null) return;

                foreach (KeyValuePair<string, object> pair in names)
                {
                    int slot;
                    if (!int.TryParse(pair.Key, out slot) || slot < 1 || slot > MaxSlots) continue;
                    string value = Json.ToStringValue(pair.Value);
                    if (!string.IsNullOrEmpty(value)) _slotNames[slot] = value;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] slot names load failed: " + ex.Message);
            }
        }

        private static string DisplayName(int slot)
        {
            string name;
            if (_slotNames.TryGetValue(slot, out name) && !string.IsNullOrEmpty(name)) return name;
            return L.F("slot.name", slot);
        }

        // ---------------- 重命名 ----------------

        private static void BuildRenameDialog()
        {
            _renameRoot = UObject.NewObject<UCanvasPanel>(_content);
            UCanvasPanelSlot rootSlot = _content.AddChildToCanvas(_renameRoot);
            rootSlot.SetPosition(new FVector2D(1100f, 720f));
            rootSlot.SetSize(new FVector2D(1640f, 560f));
            rootSlot.SetZOrder(80);

            UBorder bg = UObject.NewObject<UBorder>(_renameRoot);
            bg.SetBrushColor(new FLinearColor(0f, 0f, 0f, 1f));
            UCanvasPanelSlot bgSlot = _renameRoot.AddChildToCanvas(bg);
            bgSlot.SetPosition(new FVector2D(0f, 0f));
            bgSlot.SetSize(new FVector2D(1640f, 560f));
            bgSlot.SetZOrder(0);

            _renameTitle = AddTextOn(_renameRoot, L.T("rename.title"), 60f, 30f, 1520f, 80f, ETextJustify.Left, 2);

            _renameBox = UObject.NewObject<UEditableTextBox>(_renameRoot);
            _renameBox.SetHintText(FText.FromString(L.T("rename.hint")));
            _renameBox.SelectAllTextWhenFocused = true;
            try
            {
                // 复制游戏字体的 FSlateFontInfo，保证中文能显示；文字改黑色（输入框是浅底）。
                FEditableTextBoxStyle style = _renameBox.WidgetStyle;
                if (_statusText != null) style.Font = _statusText.Font;
                FSlateColor black = MakeSlateColor(0.05f, 0.045f, 0.04f, 1f);
                style.ForegroundColor = black;
                style.FocusedForegroundColor = black;
                style.ReadOnlyForegroundColor = black;
                _renameBox.WidgetStyle = style;
                _renameBox.SetForegroundColor(new FLinearColor(0.05f, 0.045f, 0.04f, 1f));
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] rename box font failed: " + ex.Message);
            }

            UCanvasPanelSlot boxSlot = _renameRoot.AddChildToCanvas(_renameBox);
            boxSlot.SetPosition(new FVector2D(60f, 140f));
            boxSlot.SetSize(new FVector2D(1520f, 110f));
            boxSlot.SetZOrder(3);

            // 和土地庙选择按钮同款：默认白字无底，鼠标悬停/手柄聚焦时白色高亮块 + 黑字。
            _renameConfirmStyle = AddStyledButton(_renameRoot, L.T("common.ok"), 500f, 300f, 280f, 90f,
                new FOnButtonPressedEvent.Signature(ConfirmRenameStatic), false, RenameText, -1);
            _renameCancelStyle = AddStyledButton(_renameRoot, L.T("common.cancel"), 860f, 300f, 280f, 90f,
                new FOnButtonPressedEvent.Signature(CancelRenameStatic), false, RenameText, -1);

            _renameRoot.SetVisibility(ESlateVisibility.Collapsed);
        }

        // ---------------- 设置（语言） ----------------

        private static void BuildSettingsDialog()
        {
            _settingsRoot = UObject.NewObject<UCanvasPanel>(_content);
            UCanvasPanelSlot rootSlot = _content.AddChildToCanvas(_settingsRoot);
            rootSlot.SetPosition(new FVector2D(1220f, 500f));
            rootSlot.SetSize(new FVector2D(1400f, 1200f));
            rootSlot.SetZOrder(90);

            UBorder bg = UObject.NewObject<UBorder>(_settingsRoot);
            bg.SetBrushColor(new FLinearColor(0f, 0f, 0f, 1f));
            UCanvasPanelSlot bgSlot = _settingsRoot.AddChildToCanvas(bg);
            bgSlot.SetPosition(new FVector2D(0f, 0f));
            bgSlot.SetSize(new FVector2D(1400f, 1200f));
            bgSlot.SetZOrder(0);

            _settingsTitleText = AddTextOn(_settingsRoot, L.T("settings.title"),
                60f, 40f, 1280f, 80f, ETextJustify.Left, 2);
            _settingsLangText = AddTextOn(_settingsRoot, L.T("settings.language"),
                60f, 150f, 640f, 56f, ETextJustify.Left, 2);
            _settingsCurrentText = AddTextOn(_settingsRoot, "",
                60f, 208f, 1280f, 48f, ETextJustify.Left, 2);
            ApplyHintStyle(_settingsCurrentText);

            for (int i = 0; i < _settingsCodes.Length; i++)
            {
                string code = _settingsCodes[i];
                int index = i;
                StyledButton row = AddStyledButton(_settingsRoot, "", 60f, 292f + i * 100f, 1280f, 84f,
                    new FOnButtonPressedEvent.Signature(delegate { SelectLanguage(code); }),
                    true, RowText, -1);
                row.Button.OnHovered.Bind(new FOnButtonHoverEvent.Signature(
                    delegate { _settingsHover = index + 1; RefreshSettingsHighlight(); }));
                _settingsRows[i] = row;
            }

            _settingsNoteText = AddTextOn(_settingsRoot, L.T("settings.note"),
                60f, 890f, 1280f, 80f, ETextJustify.Left, 2);
            _settingsNoteText.SetAutoWrapText(true);
            ApplyHintStyle(_settingsNoteText);

            // 语言相关的状态提示（自动生成快照时显示进度）；位置在提示文字下方。
            _settingsStatusText = AddTextOn(_settingsRoot, "",
                60f, 975f, 1280f, 60f, ETextJustify.Left, 2);
            _settingsStatusText.SetAutoWrapText(true);
            ApplyHintStyle(_settingsStatusText);
            _settingsStatusText.SetColorAndOpacity(MakeSlateColor(0.86f, 0.72f, 0.40f, 1f));

            _settingsCloseStyle = AddStyledButton(_settingsRoot, L.T("settings.close"),
                1040f, 1050f, 300f, 84f,
                new FOnButtonPressedEvent.Signature(CloseSettingsStatic), false, ActionText, -1);

            RefreshSettingsText();
            _settingsRoot.SetVisibility(ESlateVisibility.Collapsed);
        }

        private static void RefreshSettingsText()
        {
            if (_settingsRoot == null) return;

            if (_settingsTitleText != null) _settingsTitleText.SetText(FText.FromString(L.T("settings.title")));
            if (_settingsLangText != null) _settingsLangText.SetText(FText.FromString(L.T("settings.language")));
            if (_settingsCurrentText != null)
                _settingsCurrentText.SetText(FText.FromString(
                    L.F("settings.lang_current", L.DisplayName(L.CurrentCode))));
            if (_settingsNoteText != null) _settingsNoteText.SetText(FText.FromString(L.T("settings.note")));
            if (_settingsCloseStyle != null && _settingsCloseStyle.Label != null)
                _settingsCloseStyle.Label.SetText(FText.FromString(L.T("settings.close")));

            for (int i = 0; i < _settingsRows.Length; i++)
            {
                StyledButton row = _settingsRows[i];
                if (row == null || row.Label == null) continue;
                string code = _settingsCodes[i];
                string name = i == 0 ? L.T("settings.lang_auto") : L.DisplayName(code);
                bool active = string.Equals(L.OverrideCode, code, StringComparison.OrdinalIgnoreCase);
                row.Label.SetText(FText.FromString((active ? "◆ " : "◇ ") + name));
            }
            RefreshSettingsHighlight();
        }

        /// <summary>设置面板的导航项数：语言行 + 关闭。</summary>
        private static int SettingsNavCount { get { return _settingsCodes.Length + 1; } }

        private static void RefreshSettingsHighlight()
        {
            for (int i = 0; i < _settingsRows.Length; i++)
            {
                bool on = _settingsHover > 0 ? _settingsHover == i + 1 : _settingsFocus == i;
                SetStyleHighlight(_settingsRows[i], on);
            }

            bool closeHover = _settingsCloseStyle != null && _settingsCloseStyle.Button != null &&
                              _settingsCloseStyle.Button.IsHovered();
            SetStyleHighlight(_settingsCloseStyle, closeHover || _settingsFocus == _settingsCodes.Length);
        }

        /// <summary>手柄 A：语言行=选中；关闭=关面板。</summary>
        private static void ActivateSettingsFocus()
        {
            if (_settingsFocus >= _settingsCodes.Length)
            {
                CloseSettings();
                return;
            }
            if (_settingsFocus >= 0)
                SelectLanguage(_settingsCodes[_settingsFocus]);
        }

        private static int IndexOfCode(string code)
        {
            for (int i = 0; i < _settingsCodes.Length; i++)
            {
                if (string.Equals(_settingsCodes[i], code, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private static void OpenSettings()
        {
            if (_settingsRoot == null) return;
            _settingsOpen = true;
            Log.Info("[BuildManager] action: open settings (override=" + L.OverrideCode +
                     " current=" + L.CurrentCode + " game=" + L.GameCode + ")");
            _settingsFocus = IndexOfCode(L.OverrideCode);
            _settingsHover = 0;
            _lastSettingsNavDir = -1;
            _lastSettingsNavTick = 0;
            ItemDetailPopup.Hide();
            RefreshSettingsText();
            _settingsRoot.SetVisibility(ESlateVisibility.Visible);
        }

        private static void OpenSettingsStatic() { OpenSettings(); }

        private static void CloseSettings()
        {
            if (!_settingsOpen) return;
            Log.Info("[BuildManager] action: close settings");
            _settingsOpen = false;
            _settingsHover = 0;
            _lastSettingsNavDir = -1;
            _lastSettingsNavTick = 0;
            if (_settingsRoot != null) _settingsRoot.SetVisibility(ESlateVisibility.Collapsed);
            UIEquipPageHook.FocusEntry();
        }

        private static void CloseSettingsStatic() { CloseSettings(); }

        // ---------------- 自动生成分语言快照 ----------------
        //
        // 游戏只按“当前游戏语言”提供文本，所以当 MOD 语言 != 游戏语言时，
        // 玩家选中目标语言后：临时把游戏 culture 切到目标语言 → 导出快照 → 切回原语言。
        // 快照写到 data/locale/<code>.json，之后切换语言直接读文件，不再反复切换。

        private static bool _snapshotRunning;
        private static string _snapshotCode;
        private static string _snapshotRestoreCulture;
        private static int _snapshotStage;
        private static int _snapshotStageTick;

        private static readonly List<string> _snapshotQueue = new List<string>();
        private static bool _batchTried;
        private static int _batchIndex;
        private static int _batchTotal;

        private static void StartSnapshot(string code)
        {
            StartSnapshot(code, false);
        }

        private static void StartSnapshot(string code, bool batch)
        {
            if (string.IsNullOrEmpty(code)) return;
            if (LocalizedData.HasDataFor(code)) return;

            if (_snapshotRunning) CancelSnapshot(true);

            _snapshotCode = code;
            _snapshotRestoreCulture = CultureSwitch.RawCurrent();
            _snapshotStage = 1;
            _snapshotStageTick = Environment.TickCount + 300; // 给本地化刷新留出时间
            _snapshotRunning = true;

            SetSettingsStatus(batch
                ? L.F("settings.snapshot_batch", L.DisplayName(code), _batchIndex, _batchTotal)
                : L.F("settings.snapshot_working", L.DisplayName(code)));

            Log.Info("[BuildManager] snapshot start: " + code + " (game " + L.GameCode +
                     ", raw culture " + CultureSwitch.RawCurrent() + ", batch=" + batch + ")");

            CultureSwitch.Set(code);
        }

        /// <summary>
        /// 开发模式：打开面板时自动补齐所有支持的语种快照（每台机器只跑一次）。
        /// 生成出来的 data/locale/*.json 可以直接打包进发布包，终端用户无需再生成。
        /// </summary>
        private static void MaybeGenerateAllSnapshots()
        {
            if (_batchTried || !ModPaths.IsDev) return;
            _batchTried = true;

            var missing = new List<string>();
            for (int i = 0; i < L.Supported.Length; i++)
            {
                string code = L.Supported[i];
                // 游戏自身语言也要生成：发布后可能出现“日语游戏 + 英语 MOD”，
                // 没有 en 快照就只能退回当前运行语言的实时文本。
                if (LocalizedData.HasDataFor(code)) continue;
                missing.Add(code);
            }

            if (missing.Count == 0) return;

            Log.Info("[BuildManager] dev batch snapshot: " + string.Join(",", missing.ToArray()));

            _snapshotQueue.Clear();
            _snapshotQueue.AddRange(missing);
            _batchTotal = missing.Count;
            _batchIndex = 0;

            StartNextSnapshot();
        }

        private static void StartNextSnapshot()
        {
            if (_snapshotQueue.Count == 0) return;

            string code = _snapshotQueue[0];
            _snapshotQueue.RemoveAt(0);
            _batchIndex++;

            StartSnapshot(code, true);
        }

        private static void CancelSnapshot(bool restore)
        {
            _snapshotRunning = false;
            _snapshotStage = 0;
            _snapshotCode = null;

            if (restore && !string.IsNullOrEmpty(_snapshotRestoreCulture))
            {
                CultureSwitch.Set(_snapshotRestoreCulture);
                Log.Info("[BuildManager] snapshot cancelled; culture restored to " + _snapshotRestoreCulture);
            }
            _snapshotRestoreCulture = null;
        }

        /// <summary>每帧推进：切换后等一小会儿，再导出并恢复原语言。</summary>
        private static void UpdateSnapshotJob()
        {
            if (!_snapshotRunning) return;
            if (Environment.TickCount < _snapshotStageTick) return;
            if (_snapshotStage != 1) return;

            int items;
            int equips;
            int suits;
            Log.Info("[BuildManager] snapshot dump begin: culture now=" + CultureSwitch.RawCurrent() +
                     " target=" + _snapshotCode);
            bool ok = LocalizedData.TryDump(_snapshotCode, out items, out equips, out suits);

            string restore = _snapshotRestoreCulture;
            string code = _snapshotCode;

            _snapshotRunning = false;
            _snapshotStage = 0;
            _snapshotCode = null;
            _snapshotRestoreCulture = null;

            if (!string.IsNullOrEmpty(restore)) CultureSwitch.Set(restore);

            LocalizedData.Invalidate();
            try { ApplyLanguage(); } catch { }

            if (ok)
            {
                SetSettingsStatus(L.F("settings.snapshot_done", L.DisplayName(L.CurrentCode)));
                Log.Info("[BuildManager] snapshot ok: " + code + " items=" + items +
                         " equips=" + equips + " suits=" + suits);
            }
            else
            {
                SetSettingsStatus(L.T("settings.snapshot_fail"));
                Log.Warn("[BuildManager] snapshot failed: " + code);
            }

            // 批量生成：继续下一个语种。
            if (_snapshotQueue.Count > 0) StartNextSnapshot();
        }

        private static void SetSettingsStatus(string text)
        {
            if (_settingsStatusText == null) return;
            _settingsStatusText.SetText(FText.FromString(text ?? string.Empty));
        }

        private static void SelectLanguage(string code)
        {
            Log.Info("[BuildManager] action: select language=" + code +
                     " (game=" + L.GameCode + ", override was " + L.OverrideCode + ")");
            _settingsFocus = IndexOfCode(code);
            L.SetOverride(code);
            RefreshSettingsText();

            if (code == L.Auto)
            {
                if (_snapshotRunning) CancelSnapshot(true);
                SetSettingsStatus(string.Empty);
                return;
            }

            // 选中非游戏语言时，自动准备该语言的物品快照（已有则直接读）。
            StartSnapshot(code);
        }

        /// <summary>设置面板内方向导航：上下切换语言行。</summary>
        public static void NavigateSettingsFromNative(int direction, bool repeat)
        {
            if (!_settingsOpen) return;

            int now = Environment.TickCount;
            int gap = repeat ? 150 : 45;
            if (direction == _lastSettingsNavDir && now - _lastSettingsNavTick < gap) return;
            _lastSettingsNavDir = direction;
            _lastSettingsNavTick = now;

            NotifyGamepadInput();

            int count = SettingsNavCount;
            if (direction == 0) _settingsFocus = (_settingsFocus - 1 + count) % count;
            else if (direction == 1) _settingsFocus = (_settingsFocus + 1) % count;
            _settingsHover = 0;
            RefreshSettingsHighlight();
        }

        /// <summary>语言切换后刷新所有静态文案（动态内容交给 RefreshAll）。</summary>
        private static void ApplyLanguage()
        {
            if (_overlay == null) return;

            // 语言变了，分语言物品数据也要重新读。
            LocalizedData.Invalidate();

            if (_titleText != null) _titleText.SetText(FText.FromString(L.T("panel.title")));

            for (int i = 0; i < _actionKeys.Length; i++)
            {
                if (_actionLabels[i] == null) continue;
                _actionLabels[i].SetText(FText.FromString(L.T(_actionKeys[i])));
            }

            if (_returnStyle != null && _returnStyle.Label != null)
                _returnStyle.Label.SetText(FText.FromString(L.T("action.back")));

            if (_settingsButtonStyle != null && _settingsButtonStyle.Label != null)
                _settingsButtonStyle.Label.SetText(FText.FromString(L.T("settings.title")));

            if (_sectionEquip != null) _sectionEquip.SetText(FText.FromString(L.T("section.equip")));
            if (_sectionCurio != null) _sectionCurio.SetText(FText.FromString(L.T("section.curio")));
            if (_sectionFabao != null) _sectionFabao.SetText(FText.FromString(L.T("section.fabao")));
            if (_sectionHulu != null) _sectionHulu.SetText(FText.FromString(L.T("section.hulu")));

            for (int n = 1; n <= MaxSlots; n++)
            {
                SlotRow row = _rows[n];
                if (row == null || row.Style == null || row.Style.Label == null) continue;
                row.Style.Label.SetText(FText.FromString(DisplayName(n)));
            }

            if (_renameBox != null) _renameBox.SetHintText(FText.FromString(L.T("rename.hint")));
            if (_renameConfirmStyle != null && _renameConfirmStyle.Label != null)
                _renameConfirmStyle.Label.SetText(FText.FromString(L.T("common.ok")));
            if (_renameCancelStyle != null && _renameCancelStyle.Label != null)
                _renameCancelStyle.Label.SetText(FText.FromString(L.T("common.cancel")));
            if (_renaming && _renameTitle != null)
                _renameTitle.SetText(FText.FromString(L.F("rename.title_slot", DisplayName(_renameSlot))));

            if (_statusText != null) _statusText.SetText(FText.FromString(""));

            RefreshHints();
            RefreshAll();
            RefreshSettingsText();
            try { UIEquipPageHook.RefreshEntryText(); } catch { }
        }

        private static void OnLanguageChanged()
        {
            try { ApplyLanguage(); }
            catch (Exception ex) { Log.Warn("[BuildManager] apply language failed: " + ex); }
        }

        private static void RenameSelected()
        {
            if (!RequireSelection()) return;
            Log.Info("[BuildManager] action: rename slot=" + _selected);
            BeginRename(_selected);
        }

        private static void BeginRename(int slot)
        {
            if (slot < 1 || slot > _slotCount || _renameRoot == null) return;

            _selected = slot;
            _previewSlot = 0;
            _hoverSlot = 0;
            ItemDetailPopup.Hide();
            RefreshRows();
            RefreshDetails();
            RefreshSelectionState();

            _renameSlot = slot;
            _renaming = true;
            _renameFocus = 0;
            _renameHover = 0;
            _lastRenameNavDir = -1;
            _lastRenameNavTick = 0;
            _renameTitle.SetText(FText.FromString(L.F("rename.title_slot", DisplayName(slot))));
            _renameBox.SetText(FText.FromString(DisplayName(slot)));
            try { _renameBox.SetIsEnabled(true); } catch { }
            _renameRoot.SetVisibility(ESlateVisibility.Visible);
            RefreshRenameFocus();
            try { _renameBox.SetKeyboardFocus(); } catch { }
            bool focusOk = false;
            try { focusOk = _renameBox.HasKeyboardFocus() || _renameBox.HasFocusedDescendants(); } catch { }
            Log.Info("[BuildManager] rename begin slot=" + slot + " focus=" + focusOk);
        }

        private static void ConfirmRename()
        {
            if (!_renaming) return;

            string text = null;
            try { text = _renameBox.GetText().ToString(); } catch { }
            text = (text ?? string.Empty).Trim();

            int slot = _renameSlot;
            _renaming = false;
            _renameFocus = 0;
            _renameHover = 0;
            _lastRenameNavDir = -1;
            _lastRenameNavTick = 0;
            if (_renameRoot != null) _renameRoot.SetVisibility(ESlateVisibility.Collapsed);
            UIEquipPageHook.FocusEntry();

            if (string.IsNullOrEmpty(text) || text == L.F("slot.name", slot)) _slotNames.Remove(slot);
            else _slotNames[slot] = text;

            SaveSlotMeta();
            RefreshRows();
            RefreshDetails();
            Status(L.F("rename.done", DisplayName(slot)));
            Log.Info("[BuildManager] rename slot=" + slot + " -> " + DisplayName(slot));
        }

        private static void CancelRename()
        {
            if (!_renaming) return;
            _renaming = false;
            _renameFocus = 0;
            _renameHover = 0;
            _lastRenameNavDir = -1;
            _lastRenameNavTick = 0;
            if (_renameRoot != null) _renameRoot.SetVisibility(ESlateVisibility.Collapsed);
            UIEquipPageHook.FocusEntry();
            Log.Info("[BuildManager] rename cancelled");
        }

        public static bool IsRenaming { get { return _renaming; } }

        // ---------------- 导航 ----------------

        private static void BuildNav()
        {
            _nav.Clear();
            RegisterNav("clear", 210f, 250f, 170f, 80f, ClearSelected, 0, 0);
            RegisterNav("delete", 390f, 250f, 170f, 80f, DeleteSelected, 0, 1);
            RegisterNav("rename", 570f, 250f, 170f, 80f, RenameSelected, 0, 2);
            RegisterNav("save", LeftX, 340f, 270f, 80f, SaveSelected, 0, 3);
            RegisterNav("apply", LeftX + 290f, 340f, 270f, 80f, ApplySelected, 0, 4);

            for (int n = 1; n <= _slotCount; n++)
            {
                int slot = n;
                RegisterNav("slot" + n, LeftX, RowTop + (n - 1) * RowPitch, LeftWidth, RowHeight,
                    () => SelectSlot(slot), n);
            }

            if (_slotCount < MaxSlots)
            {
                RegisterNav("add", LeftX, RowTop + _slotCount * RowPitch, LeftWidth, RowHeight,
                    AddSlot, 0, 6);
            }

            RegisterNav("return", LeftX, ReturnY(), LeftWidth, 80f, Back, 0, 5);

            // 右上角设置按钮也参与手柄导航（从槽位行/每行最右格向右到达）。
            RegisterNav("settings", 3460f, 108f, 280f, 84f, OpenSettings, 0, -1);

            // 右列物品格子也参与手柄导航（停在格子上时显示详情浮框）。
            for (int i = 0; i < _equipCards.Length; i++) RegisterCardNav(_equipCards[i]);
            for (int i = 0; i < _curioCards.Length; i++) RegisterCardNav(_curioCards[i]);
            for (int i = 0; i < _fabaoCards.Length; i++) RegisterCardNav(_fabaoCards[i]);
            for (int i = 0; i < _spellCards.Length; i++) RegisterCardNav(_spellCards[i]);
            RegisterCardNav(_gourdCard);
            RegisterCardNav(_wineCard);
            for (int i = 0; i < _partnerCards.Length; i++) RegisterCardNav(_partnerCards[i]);

            _focus = -1;
        }

        private static void RegisterCardNav(Card card)
        {
            if (card == null) return;
            _nav.Add(new NavItem
            {
                Name = "card",
                X = card.X,
                Y = card.Y,
                W = CardW,
                H = CardH,
                Action = null,
                Slot = 0,
                Card = card
            });
        }

        private static void RegisterNav(string name, float x, float y, float width, float height,
                                        Action action, int slot, int actionIndex = -1)
        {
            _nav.Add(new NavItem
            {
                Name = name,
                X = x,
                Y = y,
                W = width,
                H = height,
                Action = action,
                Slot = slot,
                ActionIndex = actionIndex
            });
        }

        /// <summary>
        /// 导航分区：左侧方案/按钮区与右侧物品格区。上下方向不跨区；
        /// 从左区按右可进入物品格，从物品格按左可回到左区。
        /// </summary>
        private static bool AllowsNavCandidate(NavItem current, NavItem candidate, int direction)
        {
            bool currentIsCard = current.Card != null;
            bool candidateIsCard = candidate.Card != null;

            if (direction == 0 || direction == 1) return currentIsCard == candidateIsCard;
            if (currentIsCard) return direction == 3 ? candidateIsCard : true;
            return direction == 3 ? true : !candidateIsCard;
        }

        private static bool IsNavVisible(NavItem item)
        {
            if (item.Card != null)
            {
                return item.Card.ItemId > 0
                    && item.Card.Frame.GetVisibility() == ESlateVisibility.Visible;
            }
            return item.Slot <= 0 || item.Slot <= _slotCount;
        }

        private static void SetFocus(int index)
        {
            if (index < 0 || index >= _nav.Count) return;
            NavItem item = _nav[index];
            if (!IsNavVisible(item)) return;

            _focus = index;
            RefreshButtonStyles();
        }

        private static void SetBar(int index, float x, float y, float width, float height)
        {
            _focusBarSlots[index].SetPosition(new FVector2D(x, y));
            _focusBarSlots[index].SetSize(new FVector2D(width, height));
            _focusBars[index].SetVisibility(ESlateVisibility.HitTestInvisible);
        }

        /// <summary>
        /// 游戏输入管线送来的方向：repeat=true 时按 120ms 节流（摇杆按住会持续触发），
        /// 不同方向立即响应，避免按住左摇杆时一次跳好几格。
        /// </summary>
        public static void NavigateFromNative(int direction, bool repeat)
        {
            if (!_visible || _renaming) return;

            int now = Environment.TickCount;
            int minGap = repeat ? 120 : 40;
            if (direction == _lastNavDir && now - _lastNavTick < minGap) return;

            _lastNavDir = direction;
            _lastNavTick = now;
            Navigate(direction);
        }

        /// <summary>左摇杆/十字键：0=上 1=下 2=左 3=右。运行在游戏线程。</summary>
        public static void Navigate(int direction)
        {
            if (!_visible || _renaming) return;
            if (_focus < 0)
            {
                // 从最近一次锁定/预览的位置开始，没有就从头。
                FocusSlot(_selected > 0 ? _selected : (_previewSlot > 0 ? _previewSlot : 1));
            }
            if (_focus < 0) return;

            NavItem current = _nav[_focus];
            double cx = current.X + current.W * 0.5;
            double cy = current.Y + current.H * 0.5;

            // 设置按钮：下和左都回到左侧
            //   未选中方案 -> 配装列表第一个
            //   已选中方案 -> 第一行物品最右格
            if (string.Equals(current.Name, "settings", StringComparison.Ordinal))
            {
                if (direction == 1 || direction == 2) MoveLeftFromSettings();
                return;
            }

            if (direction == 2 || direction == 3)
            {
                bool currentIsCard = current.Card != null;

                // 槽位行向右：
                //   未选中方案 -> 设置按钮
                //   已选中方案 -> 进入右侧第一行第一个物品格
                if (direction == 3 && !currentIsCard && current.Slot > 0)
                {
                    if (_selected <= 0)
                    {
                        FocusSettingsButton();
                        return;
                    }

                    int firstCard = FindFirstCardNav();
                    if (firstCard >= 0)
                    {
                        SelectFocus(firstCard, direction);
                        return;
                    }
                }

                // 物品格处于本行最右时，再向右进入设置按钮。
                if (direction == 3 && currentIsCard && IsRightmostCardInRow(current))
                {
                    FocusSettingsButton();
                    return;
                }

                // 规则 2：右侧每行最左边的格子再往左，回到当前锁定的方案行，
                // 而不是跳到「返回」按钮或最后一个方案。
                if (direction == 2 && currentIsCard)
                {
                    int sameRow = FindCardLeftInRow(cx, cy);
                    if (sameRow >= 0)
                    {
                        SelectFocus(sameRow, direction);
                        return;
                    }

                    int slot = _selected > 0 ? _selected : _previewSlot;
                    int slotNav = FindSlotNav(slot);
                    if (slotNav >= 0)
                    {
                        SelectFocus(slotNav, direction);
                        return;
                    }
                }

                // 其余情况：同一行内左右移动；跨区且无对齐行时按纵向最近兜底。
                int best = -1;
                double bestDistance = double.MaxValue;
                int fallback = -1;
                double fallbackRowDelta = double.MaxValue;
                double fallbackDistance = double.MaxValue;

                for (int i = 0; i < _nav.Count; i++)
                {
                    if (i == _focus) continue;
                    NavItem item = _nav[i];
                    if (!IsNavVisible(item) || !AllowsNavCandidate(current, item, direction)) continue;

                    double ix = item.X + item.W * 0.5;
                    double iy = item.Y + item.H * 0.5;
                    if (direction == 2 ? ix >= cx - 1.0 : ix <= cx + 1.0) continue;

                    double rowDelta = Math.Abs(iy - cy);
                    double distance = Math.Abs(ix - cx);

                    if (rowDelta <= 80.0)
                    {
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = i;
                        }
                        continue;
                    }

                    bool candidateIsCard = item.Card != null;
                    if (candidateIsCard == currentIsCard) continue;

                    if (rowDelta < fallbackRowDelta - 0.5
                        || (Math.Abs(rowDelta - fallbackRowDelta) <= 0.5 && distance < fallbackDistance))
                    {
                        fallbackRowDelta = rowDelta;
                        fallbackDistance = distance;
                        fallback = i;
                    }
                }

                if (best < 0) best = fallback;
                if (best >= 0) SelectFocus(best, direction);
                return;
            }

            double rowY = double.MaxValue;
            double nearestRowDelta = double.MaxValue;
            for (int i = 0; i < _nav.Count; i++)
            {
                if (i == _focus) continue;
                NavItem item = _nav[i];
                if (!IsNavVisible(item) || !AllowsNavCandidate(current, item, direction)) continue;

                double iy = item.Y + item.H * 0.5;
                double delta = direction == 0 ? cy - iy : iy - cy;
                if (delta <= 1.0) continue;

                if (delta < nearestRowDelta)
                {
                    nearestRowDelta = delta;
                    rowY = iy;
                }
            }

            if (nearestRowDelta == double.MaxValue) return;

            int bestRow = -1;
            double bestRowDistance = double.MaxValue;
            for (int i = 0; i < _nav.Count; i++)
            {
                if (i == _focus) continue;
                NavItem item = _nav[i];
                if (!IsNavVisible(item) || !AllowsNavCandidate(current, item, direction)) continue;

                double ix = item.X + item.W * 0.5;
                double iy = item.Y + item.H * 0.5;
                if (Math.Abs(iy - rowY) > 40.0) continue;

                double distance = Math.Abs(ix - cx);
                if (distance < bestRowDistance)
                {
                    bestRowDistance = distance;
                    bestRow = i;
                }
            }

            if (bestRow >= 0) SelectFocus(bestRow, direction);
        }

        /// <summary>右侧最左上角的第一个可见格子（先按行 Y，再按列 X）。</summary>
        private static int FindFirstCardNav()
        {
            int best = -1;
            double bestY = double.MaxValue;
            double bestX = double.MaxValue;
            for (int i = 0; i < _nav.Count; i++)
            {
                NavItem item = _nav[i];
                if (item.Card == null || !IsNavVisible(item)) continue;
                if (item.Y < bestY - 0.5
                    || (Math.Abs(item.Y - bestY) <= 0.5 && item.X < bestX))
                {
                    bestY = item.Y;
                    bestX = item.X;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>同一行内位于当前格子左侧的最近格子；已在最左列时返回 -1。</summary>
        private static int FindCardLeftInRow(double cx, double cy)
        {
            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < _nav.Count; i++)
            {
                NavItem item = _nav[i];
                if (item.Card == null || !IsNavVisible(item)) continue;

                double iy = item.Y + item.H * 0.5;
                double ix = item.X + item.W * 0.5;
                if (Math.Abs(iy - cy) > 80.0) continue;
                if (ix >= cx - 1.0) continue;

                double distance = cx - ix;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static int FindNavByName(string name)
        {
            for (int i = 0; i < _nav.Count; i++)
            {
                if (string.Equals(_nav[i].Name, name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static void FocusSettingsButton()
        {
            int index = FindNavByName("settings");
            if (index >= 0) SelectFocus(index, 3);
        }

        /// <summary>从设置按钮向左：未选方案回第一行槽位；已选方案回第一行最右物品格。</summary>
        private static void MoveLeftFromSettings()
        {
            if (_selected <= 0)
            {
                int firstSlot = FindSlotNav(1);
                if (firstSlot >= 0) SelectFocus(firstSlot, 2);
                return;
            }

            int card = FindRightmostCardOfTopRow();
            if (card >= 0)
            {
                SelectFocus(card, 2);
                return;
            }

            int slotNav = FindSlotNav(_selected);
            if (slotNav >= 0) SelectFocus(slotNav, 2);
        }

        /// <summary>当前格子是否是其所在行（同一 Y 带、可见）最右侧的那一个。</summary>
        private static bool IsRightmostCardInRow(NavItem current)
        {
            double cy = current.Y + current.H * 0.5;
            double maxX = current.X;
            for (int i = 0; i < _nav.Count; i++)
            {
                NavItem item = _nav[i];
                if (item == null || item.Card == null || !IsNavVisible(item)) continue;
                double iy = item.Y + item.H * 0.5;
                if (Math.Abs(iy - cy) > 80.0) continue;
                if (item.X > maxX + 0.5) maxX = item.X;
            }
            return current.X >= maxX - 0.5;
        }

        /// <summary>第一行（Y 最小）里最右侧的可见物品格。</summary>
        private static int FindRightmostCardOfTopRow()
        {
            double minY = double.MaxValue;
            for (int i = 0; i < _nav.Count; i++)
            {
                NavItem item = _nav[i];
                if (item == null || item.Card == null || !IsNavVisible(item)) continue;
                if (item.Y < minY) minY = item.Y;
            }
            if (minY == double.MaxValue) return -1;

            int best = -1;
            float bestX = float.MinValue;
            for (int i = 0; i < _nav.Count; i++)
            {
                NavItem item = _nav[i];
                if (item == null || item.Card == null || !IsNavVisible(item)) continue;
                if (Math.Abs(item.Y - minY) > 1.0) continue;
                if (item.X > bestX)
                {
                    bestX = item.X;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>指定方案行的导航索引（用于从右侧格子回到当前方案）。</summary>
        private static int FindSlotNav(int slot)
        {            if (slot <= 0) return -1;
            string name = "slot" + slot;
            for (int i = 0; i < _nav.Count; i++)
            {
                NavItem item = _nav[i];
                if (item.Card != null || item.Slot != slot) continue;
                if (item.Name != name) continue;
                if (!IsNavVisible(item)) continue;
                return i;
            }
            return -1;
        }

        private static void SelectFocus(int index, int direction)
        {
            SetFocus(index);
            NavItem item = _nav[index];
            // 方向键只移动焦点/预览，不改变锁定目标；按 A 才锁定方案。
            if (item.Card != null)
            {
                ClearPreview();
                ShowCardDetail(item.Card);
            }
            else if (item.Slot > 0)
            {
                ItemDetailPopup.Hide();
                PreviewSlot(item.Slot);
            }
            else
            {
                ItemDetailPopup.Hide();
                ClearPreview();
            }
            Log.Debug("[BuildManager] gamepad nav dir=" + (direction + 1) + " -> " + item.Name);
        }

        /// <summary>显示格子的详情浮框；套装件数按右列当前展示的方案计算。</summary>
        private static void ShowCardDetail(Card card)
        {
            if (card == null || card.ItemId <= 0)
            {
                ItemDetailPopup.Hide();
                return;
            }

            ItemDetailPopup.Show(card.ItemId, card.Kind, _detailEquipIds,
                card.X, card.Y, CardW, CardH);
        }

        /// <summary>A 键确认：方案行=锁定该方案；动作按钮=对锁定方案执行。</summary>
        public static void Confirm()
        {
            if (!_visible) return;

            // 同一次按键可能同时触发 ButtonPress 和 Confirm 两个 UI 动作；
            // 250ms 内只处理第一次，避免应用/保存被执行两遍。
            int now = Environment.TickCount;
            if (now - _lastConfirmTick < 250) return;
            _lastConfirmTick = now;

            if (_settingsOpen)
            {
                ActivateSettingsFocus();
                return;
            }

            if (_renaming)
            {
                if (_renameFocus == 2) CancelRename();
                else ConfirmRename();
                return;
            }

            if (_focus < 0)
            {
                FocusSlot(_selected > 0 ? _selected : (_previewSlot > 0 ? _previewSlot : 1));
            }
            if (_focus < 0) return;

            NavItem item = _nav[_focus];
            Log.Info("[BuildManager] gamepad confirm: " + item.Name);

            if (item.Card != null)
            {
                ShowCardDetail(item.Card);
                return;
            }

            if (item.Slot > 0)
            {
                SelectSlot(item.Slot);
                return;
            }

            // 清空/删除/重命名/保存/应用需要先选中方案；返回与新增不需要。
            if (item.ActionIndex >= 0 && item.ActionIndex <= 4 && !RequireSelection()) return;
            if (item.Action != null) item.Action();
        }

        /// <summary>B 键返回（XInput）：重命名中先取消，否则关闭面板。</summary>
        public static void BackKey()
        {
            if (!_visible) return;
            Log.Info("[BuildManager] input: back key (selected=" + _selected +
                     " settings=" + _settingsOpen + " renaming=" + _renaming + ")");
            if (_settingsOpen)
            {
                CloseSettings();
                return;
            }
            if (_renaming)
            {
                CancelRename();
                return;
            }
            // 已选中方案时，返回键先退回“未选中”状态，光标留在刚才那一行，不关面板。
            if (_selected > 0)
            {
                int slot = _selected;
                DeselectSlot();
                ItemDetailPopup.Hide();
                FocusSlot(slot);
                return;
            }
            _backGuardUntil = Environment.TickCount + 400;
            Back();
        }

        /// <summary>游戏自己的 Back 事件要退出装备页时调用：只关配装面板，并吞掉本次 Back。</summary>
        public static void CloseFromGameBack()
        {
            if (!_visible) return;
            if (_settingsOpen)
            {
                CloseSettings();
                return;
            }
            if (_renaming)
            {
                CancelRename();
                return;
            }
            // 与手柄 B 一致：已选中方案时先退回未选中状态，光标留在原方案行。
            if (_selected > 0)
            {
                int slot = _selected;
                DeselectSlot();
                ItemDetailPopup.Hide();
                FocusSlot(slot);
                return;
            }
            _backGuardUntil = Environment.TickCount + 300;
            Back();
        }

        /// <summary>手柄 Y：重命名当前锁定的方案。</summary>
        public static void RenameFromGamepad()
        {
            if (!_visible || _renaming) return;
            if (RequireSelection()) BeginRename(_selected);
        }

        /// <summary>
        /// 重命名对话框打开期间，原生 BUI_Button 的延迟聚焦会把手柄模式下的
        /// Slate 键盘焦点抢回去，导致输入框里的字既没选中、也打不进字。
        /// 每帧检查一次，发现焦点丢了就还给输入框；SelectAllTextWhenFocused
        /// 会在重新获得焦点时自动全选，和鼠标打开时的行为一致。
        /// </summary>
        public static void EnsureRenameFocus()
        {
            // 焦点在确定/取消时不要抢回输入框，否则输入框会继续吃掉左右方向键。
            if (!_renaming || _renameBox == null || _renameFocus != 0) return;

            bool focused = false;
            try
            {
                // Slate 的实际焦点可能落在 EditableTextBox 内部的子控件上，
                // 这时 HasKeyboardFocus 为 false 但打字仍然有效；两个都检查，
                // 避免每帧重复 SetKeyboardFocus 把已经输入的内容重置。
                focused = _renameBox.HasKeyboardFocus() || _renameBox.HasFocusedDescendants();
            }
            catch { }

            if (focused) return;

            try
            {
                _renameBox.SetKeyboardFocus();
                int now = Environment.TickCount;
                if (now - _renameFocusLogTick > 1000)
                {
                    _renameFocusLogTick = now;
                    Log.Info("[BuildManager] rename box refocused");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] rename box refocus failed: " + ex.Message);
            }
        }

        public static bool BackGuardActive
        {
            get { return Environment.TickCount < _backGuardUntil; }
        }

        /// <summary>
        /// 重命名窗口内的方向导航：
        /// 输入框 ↓=确定；确定 →=取消、↑=输入框；取消 ←=确定、↑=输入框；
        /// 其余方向不变化（输入框 ↑/←/→、确定 ←/↓、取消 →/↓）。
        /// </summary>
        public static void NavigateRenameFromNative(int direction, bool repeat)
        {
            if (_settingsOpen)
            {
                NavigateSettingsFromNative(direction, repeat);
                return;
            }
            if (!_renaming) return;
            int now = Environment.TickCount;
            int gap = repeat ? 150 : 45;
            if (direction == _lastRenameNavDir && now - _lastRenameNavTick < gap) return;
            _lastRenameNavDir = direction;
            _lastRenameNavTick = now;
            Log.Info("[BuildManager] rename nav dir=" + direction + " focus=" + _renameFocus);
            NotifyGamepadInput();
            NavigateRename(direction);
        }

        private static void NavigateRename(int direction)
        {
            int next = _renameFocus;
            if (_renameFocus == 0)
            {
                if (direction == 1) next = 1; // 输入框 ↓ -> 确定
            }
            else if (_renameFocus == 1)
            {
                if (direction == 3) next = 2;      // 确定 → 取消
                else if (direction == 0) next = 0; // 确定 ↑ 输入框
            }
            else if (_renameFocus == 2)
            {
                if (direction == 2) next = 1;      // 取消 ← 确定
                else if (direction == 0) next = 0; // 取消 ↑ 输入框
            }

            if (next != _renameFocus) SetRenameFocus(next);
        }

        private static void SetRenameFocus(int focus)
        {
            if (!_renaming) return;
            if (focus < 0) focus = 0;
            if (focus > 2) focus = 2;
            if (_renameFocus == focus) return;
            _renameFocus = focus;

            if (focus == 0)
            {
                // 回到输入框：恢复可编辑并交还键盘焦点。
                try { _renameBox.SetIsEnabled(true); } catch { }
                try { _renameBox.SetKeyboardFocus(); } catch { }
            }
            else
            {
                // 切到确定/取消：让输入框让出 Slate 焦点并临时禁用，否则左右键会被
                // EditableTextBox 吃掉（用于移动光标），无法到达页面/按钮导航层。
                try { _renameBox.SetIsEnabled(false); } catch { }
                UIEquipPageHook.FocusEntryNow();
            }

            RefreshRenameFocus();
            Log.Info("[BuildManager] rename focus=" + focus);
        }

        public static bool RenameFocusIsInput
        {
            get { return _renameFocus == 0; }
        }

        private static void RefreshRenameFocus()
        {
            int active = _renameHover > 0 ? _renameHover : _renameFocus;
            SetStyleHighlight(_renameConfirmStyle, active == 1);
            SetStyleHighlight(_renameCancelStyle, active == 2);
        }

        private static void Back()
        {
            if (!_visible) return;
            if (_settingsOpen)
            {
                CloseSettings();
                return;
            }
            Log.Info("[BuildManager] preset panel back/close");
            ClosePanel();
        }

        /// <summary>幂等关闭：Back 可能被游戏和 XInput 各触发一次，不能第二次又“切回打开”。</summary>
        private static void ClosePanel()
        {
            if (!_visible) return;
            _visible = false;
            _lastNavDir = -1;
            _lastNavTick = 0;
            _renaming = false;
            _renameFocus = 0;
            _renameHover = 0;
            _lastRenameNavDir = -1;
            _lastRenameNavTick = 0;
            _settingsOpen = false;
            _settingsHover = 0;
            _lastSettingsNavDir = -1;
            _lastSettingsNavTick = 0;
            if (_settingsRoot != null)
            {
                try { _settingsRoot.SetVisibility(ESlateVisibility.Collapsed); }
                catch { }
            }
            // 面板刚关闭时同一次 B/Esc 可能还在路上，短暂吞掉，避免把装备页也关掉。
            _backGuardUntil = Environment.TickCount + 400;
            _lastHoverSlot = 0;
            _hoverSlot = 0;
            _previewSlot = 0;
            _hoverCard = null;
            ItemDetailPopup.Hide();
            ClearPending();
            GamepadNavigator.OnPanelClosed();
            if (_renameRoot != null)
            {
                try { _renameRoot.SetVisibility(ESlateVisibility.Collapsed); }
                catch { }
            }
            if (_overlay != null)
            {
                try { _overlay.SetVisibility(ESlateVisibility.Collapsed); }
                catch { }
            }
        }

        private static void ClosePanelStatic()
        {
            Log.Info("[BuildManager] preset panel close pressed");
            Back();
        }

        /// <summary>鼠标悬停预览：只切换右列内容，不改变锁定的槽位。</summary>
        private static int _inactivePageTicks;

        public static void ProcessMouseHover()
        {
            // 快照任务与面板是否可见无关，先推进它。
            UpdateSnapshotJob();

            if (!_visible || _overlay == null)
            {
                _lastHoverSlot = 0;
                _hoverAction = -1;
                _inactivePageTicks = 0;
                ItemDetailPopup.Hide();
                return;
            }

            // 玩家用肩键切走（背包/设置等）时，装备页不再前台：立即收起配装面板，
            // 否则我们的输入补丁会继续吃掉手柄事件，导致新页面没有反应。
            if (!UIEquipPageHook.IsEquipPageActive())
            {
                _inactivePageTicks++;
                if (_inactivePageTicks >= 2)
                {
                    _inactivePageTicks = 0;
                    Log.Info("[BuildManager] equip page left; auto-closing preset panel");
                    ClosePanel();
                    return;
                }
            }
            else
            {
                _inactivePageTicks = 0;
            }

            UpdateInputMode();

            if (_settingsOpen)
            {
                int settingsHover = 0;
                for (int i = 0; i < _settingsRows.Length; i++)
                {
                    if (_settingsRows[i] != null && _settingsRows[i].Button != null &&
                        _settingsRows[i].Button.IsHovered())
                        settingsHover = i + 1;
                }
                _settingsHover = settingsHover;
                RefreshSettingsHighlight();
                ItemDetailPopup.Hide();
                return;
            }

            if (_renaming)
            {
                _lastHoverSlot = 0;
                ItemDetailPopup.Hide();

                int renameHover = 0;
                try
                {
                    if (_renameConfirmStyle != null && _renameConfirmStyle.Button.IsHovered()) renameHover = 1;
                    else if (_renameCancelStyle != null && _renameCancelStyle.Button.IsHovered()) renameHover = 2;
                }
                catch { }

                if (renameHover != _renameHover)
                {
                    _renameHover = renameHover;
                    RefreshRenameFocus();
                }
                return;
            }

            int hover = 0;
            for (int n = 1; n <= _slotCount; n++)
            {
                SlotRow row = _rows[n];
                if (row == null || row.Style == null) continue;
                if (!row.Style.Button.IsHovered()) continue;
                hover = n;
                break;
            }

            if (hover != _lastHoverSlot)
            {
                _lastHoverSlot = hover;

                // 鼠标移出方案行（例如移向“保存当前/应用”）时，右列回到选中槽位，
                // 避免“看到的是悬停方案、操作的却是另一个槽位”。
                if (hover > 0) PreviewSlot(hover);
                else ClearPreview();
            }

            int hoverAction = -1;
            for (int i = 0; i < _actionStyles.Length; i++)
            {
                if (_actionStyles[i] != null && _actionStyles[i].Button.IsHovered())
                {
                    hoverAction = i;
                    break;
                }
            }
            if (_returnStyle != null && _returnStyle.Button.IsHovered()) hoverAction = 5;
            if (hoverAction != _hoverAction)
            {
                _hoverAction = hoverAction;
                RefreshButtonStyles();
            }

            // 鼠标停在物品格子上时显示详情浮框，并让该格子进入金色高亮（名字 + 描边）。
            Card hoveredCard = FindHoveredCard();
            if (!object.ReferenceEquals(hoveredCard, _hoverCard))
            {
                _hoverCard = hoveredCard;
                RefreshButtonStyles();
            }
            if (hoveredCard != null) ShowCardDetail(hoveredCard);
            else if (_focus < 0 || _focus >= _nav.Count || _nav[_focus].Card == null) ItemDetailPopup.Hide();
        }

        private static Card FindHoveredCard()
        {
            for (int i = 0; i < _equipCards.Length; i++)
                if (IsCardHovered(_equipCards[i])) return _equipCards[i];
            for (int i = 0; i < _curioCards.Length; i++)
                if (IsCardHovered(_curioCards[i])) return _curioCards[i];
            for (int i = 0; i < _fabaoCards.Length; i++)
                if (IsCardHovered(_fabaoCards[i])) return _fabaoCards[i];
            for (int i = 0; i < _spellCards.Length; i++)
                if (IsCardHovered(_spellCards[i])) return _spellCards[i];
            if (IsCardHovered(_gourdCard)) return _gourdCard;
            if (IsCardHovered(_wineCard)) return _wineCard;
            for (int i = 0; i < _partnerCards.Length; i++)
                if (IsCardHovered(_partnerCards[i])) return _partnerCards[i];
            return null;
        }

        private static bool IsCardHovered(Card card)
        {
            if (card == null || card.ItemId <= 0) return false;
            try
            {
                if (card.Frame.GetVisibility() != ESlateVisibility.Visible) return false;
                return card.Frame.IsHovered() || card.Icon.IsHovered();
            }
            catch
            {
                return false;
            }
        }

        public static void EnqueueGamepad(Action action)
        {
            if (action == null) return;
            lock (_pendingLock) _pending.Enqueue(action);
        }

        public static void ClearPending()
        {
            lock (_pendingLock) _pending.Clear();
        }

        /// <summary>每帧由输入管理器 tick 调用，保证 UMG 操作都在游戏线程执行。</summary>
        public static void ProcessPending()
        {
            if (_renaming) EnsureRenameFocus();

            // 面板关闭时也要能处理“用手柄 A 打开面板”的请求（此时 _overlay 可能还没建）。
            for (int guard = 0; guard < 8; guard++)
            {
                Action action;
                lock (_pendingLock)
                {
                    if (_pending.Count == 0) return;
                    action = _pending.Dequeue();
                }

                try { action(); }
                catch (Exception ex) { Log.Error("[BuildManager] pending gamepad action failed: " + ex); }
            }
        }

        // ---------------- 静态回调（UMG 动态绑定只稳定支持静态方法） ----------------

        private static void OnRowClicked(int slot)
        {
            int now = Environment.TickCount;
            if (_lastClickSlot == slot && now - _lastClickTick <= 400)
            {
                _lastClickSlot = 0;
                _lastClickTick = 0;
                BeginRename(slot);
                return;
            }

            _lastClickSlot = slot;
            _lastClickTick = now;
            SelectSlot(slot);
        }

        private static void Slot1() { OnRowClicked(1); }
        private static void Slot2() { OnRowClicked(2); }
        private static void Slot3() { OnRowClicked(3); }
        private static void Slot4() { OnRowClicked(4); }
        private static void Slot5() { OnRowClicked(5); }
        private static void Slot6() { OnRowClicked(6); }
        private static void Slot7() { OnRowClicked(7); }
        private static void Slot8() { OnRowClicked(8); }
        private static void Slot9() { OnRowClicked(9); }
        private static void Slot10() { OnRowClicked(10); }
        private static void Slot11() { OnRowClicked(11); }
        private static void Slot12() { OnRowClicked(12); }
        private static void Slot13() { OnRowClicked(13); }
        private static void Slot14() { OnRowClicked(14); }

        private static FOnButtonPressedEvent.Signature GetSlotDelegate(int slot)
        {
            switch (slot)
            {
                case 1: return new FOnButtonPressedEvent.Signature(Slot1);
                case 2: return new FOnButtonPressedEvent.Signature(Slot2);
                case 3: return new FOnButtonPressedEvent.Signature(Slot3);
                case 4: return new FOnButtonPressedEvent.Signature(Slot4);
                case 5: return new FOnButtonPressedEvent.Signature(Slot5);
                case 6: return new FOnButtonPressedEvent.Signature(Slot6);
                case 7: return new FOnButtonPressedEvent.Signature(Slot7);
                case 8: return new FOnButtonPressedEvent.Signature(Slot8);
                case 9: return new FOnButtonPressedEvent.Signature(Slot9);
                case 10: return new FOnButtonPressedEvent.Signature(Slot10);
                case 11: return new FOnButtonPressedEvent.Signature(Slot11);
                case 12: return new FOnButtonPressedEvent.Signature(Slot12);
                case 13: return new FOnButtonPressedEvent.Signature(Slot13);
                case 14: return new FOnButtonPressedEvent.Signature(Slot14);
                default: return null;
            }
        }

        private static void Hover1() { PreviewSlot(1); }
        private static void Hover2() { PreviewSlot(2); }
        private static void Hover3() { PreviewSlot(3); }
        private static void Hover4() { PreviewSlot(4); }
        private static void Hover5() { PreviewSlot(5); }
        private static void Hover6() { PreviewSlot(6); }
        private static void Hover7() { PreviewSlot(7); }
        private static void Hover8() { PreviewSlot(8); }
        private static void Hover9() { PreviewSlot(9); }
        private static void Hover10() { PreviewSlot(10); }
        private static void Hover11() { PreviewSlot(11); }
        private static void Hover12() { PreviewSlot(12); }
        private static void Hover13() { PreviewSlot(13); }
        private static void Hover14() { PreviewSlot(14); }

        private static FOnButtonHoverEvent.Signature GetHoverDelegate(int slot)
        {
            switch (slot)
            {
                case 1: return new FOnButtonHoverEvent.Signature(Hover1);
                case 2: return new FOnButtonHoverEvent.Signature(Hover2);
                case 3: return new FOnButtonHoverEvent.Signature(Hover3);
                case 4: return new FOnButtonHoverEvent.Signature(Hover4);
                case 5: return new FOnButtonHoverEvent.Signature(Hover5);
                case 6: return new FOnButtonHoverEvent.Signature(Hover6);
                case 7: return new FOnButtonHoverEvent.Signature(Hover7);
                case 8: return new FOnButtonHoverEvent.Signature(Hover8);
                case 9: return new FOnButtonHoverEvent.Signature(Hover9);
                case 10: return new FOnButtonHoverEvent.Signature(Hover10);
                case 11: return new FOnButtonHoverEvent.Signature(Hover11);
                case 12: return new FOnButtonHoverEvent.Signature(Hover12);
                case 13: return new FOnButtonHoverEvent.Signature(Hover13);
                case 14: return new FOnButtonHoverEvent.Signature(Hover14);
                default: return null;
            }
        }

        // ---------------- UMG 基础控件 ----------------

        private static UTextBlock AddText(string text, float x, float y, float width, float height,
                                          ETextJustify justify, int zOrder)
        {
            return AddTextOn(_content, text, x, y, width, height, justify, zOrder);
        }

        /// <summary>在给定矩形内同时水平、垂直居中的文字（用透明 UBorder 保证垂直居中）。</summary>
        private static UTextBlock AddCenteredText(string text, float x, float y, float width, float height)
        {
            UBorder box = UObject.NewObject<UBorder>(_content);
            box.SetBrushColor(new FLinearColor(0f, 0f, 0f, 0f));
            box.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
            box.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);

            UTextBlock label = UObject.NewObject<UTextBlock>(box);
            UIEquipPageHook.TryCopyTextStyle(_styleRoot, label);
            label.SetText(FText.FromString(text));
            label.SetJustification(ETextJustify.Center);
            label.SetAutoWrapText(false);

            // 原生软阴影是 (3,8) 偏正下；改成右下并收紧，同时把字下移一点，
            // 让“不含阴影的字形”在框内垂直居中。
            try
            {
                FSlateFontInfo font = label.Font;
                FFontOutlineSettings outline = font.OutlineSettings;
                FFontGSSoftShadowCfg shadow = outline.GSSoftShadowCfg;
                shadow.GSSoftShadowOffset = new FIntPoint(6, 4);
                shadow.GSSoftShadowSize = 3;
                outline.GSSoftShadowCfg = shadow;
                font.OutlineSettings = outline;
                label.Font = font;

                var transform = new FWidgetTransform();
                transform.Translation = new FVector2D(0f, 6f);
                transform.Scale = new FVector2D(1f, 1f);
                label.RenderTransform = transform;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] title style failed: " + ex.Message);
            }

            box.SetContent(label);

            UCanvasPanelSlot slot = _content.AddChildToCanvas(box);
            slot.SetPosition(new FVector2D(x, y));
            slot.SetSize(new FVector2D(width, height));
            slot.SetZOrder(10);
            return label;
        }

        private static UTextBlock AddTextOn(UCanvasPanel canvas, string text, float x, float y,
                                            float width, float height, ETextJustify justify, int zOrder)
        {
            UTextBlock label = UObject.NewObject<UTextBlock>(canvas);
            UIEquipPageHook.TryCopyTextStyle(_styleRoot, label);
            label.SetText(FText.FromString(text));
            label.SetJustification(justify);

            UCanvasPanelSlot slot = canvas.AddChildToCanvas(label);
            slot.SetPosition(new FVector2D(x, y));
            slot.SetSize(new FVector2D(width, height));
            slot.SetZOrder(zOrder);
            return label;
        }

        // ---------------- 土地庙风格按钮 ----------------

        // 土地庙选项获得焦点时的原生毛笔笔刷（不规则毛边），比装备格的方框更有质感。
        private static readonly string SelectBrushPath =
            "Texture2D'/Game/00MainHZ/UIDev/Comm/Interact/IMG_interactfocus_shrine.IMG_interactfocus_shrine'";

        private static StyledButton AddStyledButton(UCanvasPanel canvas, string text,
            float x, float y, float width, float height,
            FOnButtonPressedEvent.Signature onPressed, bool blackBack,
            FSlateColor normalColor, int actionIndex)
        {
            var style = new StyledButton();
            style.NormalColor = normalColor;

            // 视觉层放在容器 canvas 里，容器本身不参与命中测试；透明 UButton 作为子控件盖在最上面。
            // 不能把 UCanvasPanel 直接塞进 UButton 内容槽：本作 UMG 里那样内容不会渲染。
            UCanvasPanel root = UObject.NewObject<UCanvasPanel>(canvas);
            root.SetVisibility(ESlateVisibility.SelfHitTestInvisible);

            style.Back = UObject.NewObject<UBorder>(root);
            style.Back.SetBrushColor(blackBack
                ? new FLinearColor(0.018f, 0.016f, 0.014f, 1f)
                : new FLinearColor(0f, 0f, 0f, 0f));
            UCanvasPanelSlot backSlot = root.AddChildToCanvas(style.Back);
            backSlot.SetPosition(new FVector2D(0f, 0f));
            backSlot.SetSize(new FVector2D(width, height));
            backSlot.SetZOrder(0);

            // 纯白高亮底色，保证黑字可读。Ink 毛笔层只作为叠加装饰，不能反过来决定它是否显示。
            style.Glow = UObject.NewObject<UBorder>(root);
            style.Glow.SetBrushColor(new FLinearColor(0.93f, 0.91f, 0.86f, 0.95f));
            UCanvasPanelSlot glowSlot = root.AddChildToCanvas(style.Glow);
            glowSlot.SetPosition(new FVector2D(0f, 0f));
            glowSlot.SetSize(new FVector2D(width, height));
            glowSlot.SetZOrder(1);
            style.Glow.SetVisibility(ESlateVisibility.Collapsed);

            style.Ink = UObject.NewObject<UImage>(root);
            UCanvasPanelSlot inkSlot = root.AddChildToCanvas(style.Ink);
            inkSlot.SetPosition(new FVector2D(0f, 0f));
            inkSlot.SetSize(new FVector2D(width, height));
            inkSlot.SetZOrder(2);
            style.Ink.SetVisibility(ESlateVisibility.Collapsed);
            // 方案行很宽，用 9 宫格拉伸，避免毛边和笔触被横向压扁。
            ApplyInkBrush(style);
            try
            {
                b1.UIAssetLoadHelper.AsyncLoadTexForImage(_owner, style.Ink, SelectBrushPath, true, true);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] select brush load failed: " + ex.Message);
            }

            style.Label = UObject.NewObject<UTextBlock>(root);
            if (_styleRoot != null) UIEquipPageHook.TryCopyTextStyle(_styleRoot, style.Label);
            style.Label.SetText(FText.FromString(text));
            style.Label.SetJustification(ETextJustify.Center);
            style.Label.SetAutoWrapText(false);
            style.Label.SetColorAndOpacity(normalColor);
            // 阴影只向下偏移，字形仍由下面的 UBorder 居中，所以黑字/白字不会因此偏高。
            UIEquipPageHook.ApplyDownwardTextShadow(style.Label);

            // 用透明 UBorder 包住文字，水平/垂直都居中，保证白字和黑字都落在框的正中。
            UBorder labelBox = UObject.NewObject<UBorder>(root);
            labelBox.SetBrushColor(new FLinearColor(0f, 0f, 0f, 0f));
            labelBox.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
            labelBox.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            labelBox.SetContent(style.Label);
            UCanvasPanelSlot labelSlot = root.AddChildToCanvas(labelBox);
            labelSlot.SetPosition(new FVector2D(0f, 0f));
            labelSlot.SetSize(new FVector2D(width, height));
            labelSlot.SetZOrder(3);

            UButton button = UObject.NewObject<UButton>(root);
            button.BackgroundColor = new FLinearColor(0f, 0f, 0f, 0f);
            // 透明命中层：只负责点击/悬停，自带底色和内容都隐藏。
            button.ColorAndOpacity = new FLinearColor(1f, 1f, 1f, 0f);
            // 不能参与游戏焦点：否则手柄 A 会直接激活当前获得焦点的按钮（表现为连按应用）。
            button.IsFocusable = false;
            if (onPressed != null) button.OnPressed.Bind(onPressed);
            UCanvasPanelSlot buttonSlot = root.AddChildToCanvas(button);
            buttonSlot.SetPosition(new FVector2D(0f, 0f));
            buttonSlot.SetSize(new FVector2D(width, height));
            buttonSlot.SetZOrder(10);

            style.Button = button;
            style.Root = root;
            style.W = width;
            style.H = height;
            style.Slot = canvas.AddChildToCanvas(root);
            style.Slot.SetPosition(new FVector2D(x, y));
            style.Slot.SetSize(new FVector2D(width, height));
            style.Slot.SetZOrder(20);

            AddGoldFrame(style, root, width, height);
            return style;
        }

        /// <summary>焦点金框：四条细边贴在按钮外围，比整块金色底更接近原生描边感。</summary>
        private static void AddGoldFrame(StyledButton style, UCanvasPanel root, float width, float height)
        {
            // 4px 实线，画在按钮外沿（不压住白底高亮和文字）；z=4 高于白底（z1）和文字（z3）。
            const float thickness = 4f;
            FLinearColor frameColor = new FLinearColor(0.86f, 0.62f, 0.16f, 1f);
            float[][] rects =
            {
                new[] { -thickness, -thickness, width + thickness * 2f, thickness },
                new[] { -thickness, height, width + thickness * 2f, thickness },
                new[] { -thickness, 0f, thickness, height },
                new[] { width, 0f, thickness, height },
            };

            style.GoldFrame = new UBorder[rects.Length];
            for (int i = 0; i < rects.Length; i++)
            {
                UBorder strip = UObject.NewObject<UBorder>(root);
                strip.SetBrushColor(frameColor);
                UCanvasPanelSlot slot = root.AddChildToCanvas(strip);
                slot.SetPosition(new FVector2D(rects[i][0], rects[i][1]));
                slot.SetSize(new FVector2D(rects[i][2], rects[i][3]));
                slot.SetZOrder(4);
                strip.SetVisibility(ESlateVisibility.Collapsed);
                style.GoldFrame[i] = strip;
            }
        }

        private static void ApplyInkBrush(StyledButton style)
        {
            try
            {
                FSlateBrush brush = style.Ink.Brush;
                brush.DrawAs = ESlateBrushDrawType.Box;
                FMargin margin = brush.Margin;
                margin.Left = 0.18f;
                margin.Top = 0.18f;
                margin.Right = 0.18f;
                margin.Bottom = 0.18f;
                brush.Margin = margin;
                style.Ink.Brush = brush;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] ink brush setup failed: " + ex.Message);
            }
        }

        private static bool _brushSizeLogged;

        private static void LogBrushSize(StyledButton style)
        {
            if (_brushSizeLogged) return;
            try
            {
                UnrealEngine.Engine.UTexture2D tex = style.Ink.Brush.ResourceObject as UnrealEngine.Engine.UTexture2D;
                if (tex == null) return;
                _brushSizeLogged = true;
                Log.Info("[BuildManager] shrine brush size=" + tex.Blueprint_GetSizeX() + "x" + tex.Blueprint_GetSizeY());
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] shrine brush size log failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 选中态底纹：与入口按钮同一套蓝白渐变，纯 UMG 条带实现（原生 UGSFocusWidget 是代码绘制，
        /// 拿不到贴图/画刷）。首次高亮时惰性创建，之后复用。
        /// </summary>
        private static void EnsureGradient(StyledButton style)
        {
            if (style == null || style.Gradient != null || style.Root == null) return;
            try
            {
                style.Gradient = UObject.NewObject<UCanvasPanel>(style.Root);
                UCanvasPanelSlot gs = style.Root.AddChildToCanvas(style.Gradient);
                gs.SetPosition(new FVector2D(0f, 0f));
                gs.SetSize(new FVector2D(style.W, style.H));
                gs.SetZOrder(1);

                const int Steps = 10;
                float stepH = style.H / Steps;
                var top = new FLinearColor(0.55f, 0.62f, 0.78f, 1f);
                var bottom = new FLinearColor(0.95f, 0.94f, 0.91f, 1f);

                style.GradientStrips = new UBorder[Steps];
                style.GradientColors = new FLinearColor[Steps];
                for (int i = 0; i < Steps; i++)
                {
                    float t = Steps <= 1 ? 0f : (float)i / (Steps - 1);
                    var color = new FLinearColor(
                        top.R + (bottom.R - top.R) * t,
                        top.G + (bottom.G - top.G) * t,
                        top.B + (bottom.B - top.B) * t,
                        1f);
                    UBorder strip = UObject.NewObject<UBorder>(style.Gradient);
                    strip.SetBrushColor(color);
                    UCanvasPanelSlot ss = style.Gradient.AddChildToCanvas(strip);
                    ss.SetPosition(new FVector2D(0f, i * stepH));
                    ss.SetSize(new FVector2D(style.W, stepH + 0.75f));
                    ss.SetZOrder(0);
                    style.GradientStrips[i] = strip;
                    style.GradientColors[i] = color;
                }
                style.Gradient.SetVisibility(ESlateVisibility.Collapsed);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] highlight gradient failed: " + ex.Message);
                style.Gradient = null;
                style.GradientStrips = null;
            }
        }

        private static void SetStyleHighlight(StyledButton style, bool on, float strength = 0.95f)
        {
            if (style == null) return;

            if (on) EnsureGradient(style);

            if (style.Gradient != null)
            {
                style.Gradient.SetVisibility(on
                    ? ESlateVisibility.SelfHitTestInvisible
                    : ESlateVisibility.Collapsed);
                if (on && style.GradientStrips != null)
                {
                    for (int i = 0; i < style.GradientStrips.Length; i++)
                    {
                        FLinearColor c = style.GradientColors[i];
                        style.GradientStrips[i].SetBrushColor(new FLinearColor(c.R, c.G, c.B, strength));
                    }
                }
            }
            else if (style.Glow != null)
            {
                // 渐变创建失败时的兜底：纯白底保证黑字可读。
                style.Glow.SetBrushColor(new FLinearColor(0.93f, 0.91f, 0.86f, strength));
                style.Glow.SetVisibility(on
                    ? ESlateVisibility.SelfHitTestInvisible
                    : ESlateVisibility.Collapsed);
            }

            // 毛笔贴图是墨色，暂不使用。
            if (style.Ink != null) style.Ink.SetVisibility(ESlateVisibility.Collapsed);

            if (style.Label != null)
                style.Label.SetColorAndOpacity(on ? HighlightText : style.NormalColor);

            // 所有按钮/行/格子获得焦点时统一加金色边框。
            if (style.GoldFrame != null)
            {
                for (int i = 0; i < style.GoldFrame.Length; i++)
                {
                    if (style.GoldFrame[i] == null) continue;
                    style.GoldFrame[i].SetVisibility(on
                        ? ESlateVisibility.HitTestInvisible
                        : ESlateVisibility.Collapsed);
                }
            }
        }

        private static void RefreshButtonStyles()
        {
            int focusSlot = -1;
            int focusAction = -1;
            string focusName = null;
            if (_focus >= 0 && _focus < _nav.Count)
            {
                NavItem item = _nav[_focus];
                if (item.Card == null)
                {
                    focusSlot = item.Slot;
                    focusAction = item.ActionIndex;
                    focusName = item.Name;
                }
            }

            bool actionsEnabled = _selected > 0;
            for (int n = 1; n <= MaxSlots; n++)
            {
                SlotRow row = _rows[n];
                if (row == null) continue;
                bool selected = n == _selected;
                bool on = selected || n == _hoverSlot || n == focusSlot;
                // 选中=实心白底；只是光标经过/手柄聚焦=50% 透明度的白底，和选中区分开。
                SetStyleHighlight(row.Style, on, selected ? 0.95f : 0.5f);
            }

            for (int i = 0; i < _actionStyles.Length; i++)
            {
                bool on = actionsEnabled && (i == _hoverAction || i == focusAction);
                SetStyleHighlight(_actionStyles[i], on);
            }
            SetStyleHighlight(_returnStyle, focusAction == 5 || _hoverAction == 5);
            SetStyleHighlight(_addStyle, focusName == "add");

            bool settingsHovered = _settingsButtonStyle != null && _settingsButtonStyle.Button != null &&
                                   _settingsButtonStyle.Button.IsHovered();
            SetStyleHighlight(_settingsButtonStyle,
                !_settingsOpen && (focusName == "settings" || settingsHovered));

            HighlightCardFocus();
        }

        private static void HighlightCardFocus()
        {
            Card navCard = (_focus >= 0 && _focus < _nav.Count) ? _nav[_focus].Card : null;
            // 鼠标悬停的格子优先显示金色（与手柄焦点一样：名字变金 + 金色描边）。
            Card focused = _hoverCard != null ? _hoverCard : navCard;
            for (int i = 0; i < _equipCards.Length; i++) SetCardFocus(_equipCards[i], focused);
            for (int i = 0; i < _curioCards.Length; i++) SetCardFocus(_curioCards[i], focused);
            for (int i = 0; i < _fabaoCards.Length; i++) SetCardFocus(_fabaoCards[i], focused);
            for (int i = 0; i < _spellCards.Length; i++) SetCardFocus(_spellCards[i], focused);
            SetCardFocus(_gourdCard, focused);
            SetCardFocus(_wineCard, focused);
            for (int i = 0; i < _partnerCards.Length; i++) SetCardFocus(_partnerCards[i], focused);
        }

        private static void SetCardFocus(Card card, Card focused)
        {
            if (card == null || card.Frame == null) return;
            card.Frame.SetBrushColor(CardDark);
            bool on = object.ReferenceEquals(card, focused);
            if (card.Label != null)
                card.Label.SetColorAndOpacity(on ? CardFocusText : RenameText);
            if (card.Border != null)
                card.Border.SetVisibility(on ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed);
        }

        // ---------------- 输入设备提示 ----------------

        [StructLayout(LayoutKind.Sequential)]
        private struct MousePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out MousePoint point);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        /// <summary>XInput 轮询线程检测到手柄活动时调用。</summary>
        public static void NotifyGamepadInput()
        {
            _padModeRequest = 1;
        }

        private static void UpdateInputMode()
        {
            if (System.Threading.Interlocked.Exchange(ref _padModeRequest, 0) == 1) SetInputMode(1);

            MousePoint point;
            if (GetCursorPos(out point))
            {
                if (_lastMouseX != int.MinValue
                    && (Math.Abs(point.X - _lastMouseX) > 2 || Math.Abs(point.Y - _lastMouseY) > 2))
                {
                    SetInputMode(0);
                }
                _lastMouseX = point.X;
                _lastMouseY = point.Y;
            }

            bool down = (GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (down && !_rightButtonDown)
            {
                if (_renaming) CancelRename();
                else Back();
            }
            _rightButtonDown = down;
        }

        private static void SetInputMode(int mode)
        {
            if (_inputMode == mode) return;
            _inputMode = mode;
            RefreshHints();
        }

        private static void RefreshHints()
        {
            bool pad = _inputMode == 1;
            if (_hintRename != null)
                _hintRename.SetText(FText.FromString(pad ? L.T("hint.rename_pad") : L.T("hint.rename_mouse")));
            if (_hintBack != null)
                _hintBack.SetText(FText.FromString(pad ? L.T("hint.back_pad") : L.T("hint.back_mouse")));
        }

        private static bool _hintFontResolved;
        private static FSlateFontInfo _hintFont;

        /// <summary>
        /// 底部提示和可点击按钮用不同字体：游戏主字体是 B1Font_Main，
        /// 这里找加载过的 DevOnly 微软雅黑（MSYH_Font）给提示行，字号再小一号、颜色压暗。
        /// </summary>
        private static void ApplyHintStyle(UTextBlock label)
        {
            if (label == null) return;
            try
            {
                label.Font = HintFont();
                label.SetColorAndOpacity(MakeSlateColor(0.55f, 0.53f, 0.49f, 1f));
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] hint font failed: " + ex.Message);
            }
        }

        private static FSlateFontInfo HintFont()
        {
            if (!_hintFontResolved)
            {
                _hintFontResolved = true;
                try
                {
                    foreach (UTextBlock tb in UObject.GetObjects<UTextBlock>())
                    {
                        if (tb == null) continue;
                        FSlateFontInfo candidate = tb.Font;
                        if (candidate.FontObject == null) continue;
                        string path = UObject.GetPathNameSafe(candidate.FontObject);
                        if (string.IsNullOrEmpty(path) ||
                            path.IndexOf("MSYH_Font", StringComparison.Ordinal) < 0) continue;
                        candidate.Size = 26;
                        _hintFont = candidate;
                        Log.Info("[BuildManager] hint font: " + path);
                        break;
                    }
                }
                catch { }
            }

            if (_hintFont.FontObject == null && _hintRename != null)
            {
                _hintFont = _hintRename.Font;
            }
            FSlateFontInfo font = _hintFont;
            font.Size = 26;
            return font;
        }

        private static UButton AddButton(string text, float x, float y, float width, float height,
                                         FOnButtonPressedEvent.Signature onPressed,
                                         out UTextBlock label, out UBorder border)
        {
            return AddButtonOn(_content, text, x, y, width, height, onPressed, out label, out border);
        }

        private static UButton AddButtonOn(UCanvasPanel canvas, string text, float x, float y,
                                           float width, float height,
                                           FOnButtonPressedEvent.Signature onPressed,
                                           out UTextBlock label, out UBorder border)
        {
            UButton button = UObject.NewObject<UButton>(canvas);
            border = UObject.NewObject<UBorder>(button);
            border.SetBrushColor(Dark);
            border.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
            border.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);

            label = UObject.NewObject<UTextBlock>(border);
            UIEquipPageHook.TryCopyTextStyle(_styleRoot, label);
            label.SetText(FText.FromString(text));
            label.SetJustification(ETextJustify.Center);

            border.SetContent(label);
            button.SetContent(border);
            button.BackgroundColor = new FLinearColor(0f, 0f, 0f, 0f);
            // 面板内的按钮不参与游戏焦点，手柄确认统一由 UIActionTrackerPatch 处理。
            button.IsFocusable = false;

            if (onPressed != null) button.OnPressed.Bind(onPressed);

            UCanvasPanelSlot slot = canvas.AddChildToCanvas(button);
            slot.SetPosition(new FVector2D(x, y));
            slot.SetSize(new FVector2D(width, height));
            slot.SetZOrder(20);
            return button;
        }
    }
}
