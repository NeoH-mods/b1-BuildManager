using System;
using System.Collections.Generic;
using System.Text;
using b1.Localization;
using B1UI.GSUI;
using CSharpModBase;
using ResB1;
using UnrealEngine.SlateCore;
using UnrealEngine.UMG;
using UnrealEngine.Runtime;
using UnrealEngine.Slate;

namespace BuildManager
{
    /// <summary>
    /// 右侧物品格子的详情浮框：内容取自游戏静态表与 RoleAttrHelper（同原生详情框一致），
    /// 排版按原生详情页复现：品质小字 + 大标题 + 细分区线 + 属性行 + 分区标题/正文。
    /// </summary>
    internal static class ItemDetailPopup
    {
        private const float Width = 900f;
        private const float MinHeight = 520f;
        private const float MaxHeight = 1900f;
        private const float Gap = 24f;
        private const float Margin = 40f;
        private const float TopMargin = 140f;
        private const float ContentHeight = 2160f - 80f;
        private const float HeaderHeight = 172f;

        // 原生详情字号（1920 画布下约 20–34px；本面板 3840 设计尺寸，取 2 倍）。
        private const int SizeQuality = 40;
        private const int SizeTitle = 64;
        private const int SizeAttr = 42;
        private const int SizeSection = 46;
        private const int SizeBody = 42;

        private static readonly FLinearColor Gold = new FLinearColor(0.72f, 0.55f, 0.28f, 1f);
        private static readonly FLinearColor PanelDark = new FLinearColor(0.028f, 0.025f, 0.022f, 0.985f);
        private static readonly FLinearColor TitleColor = new FLinearColor(0.92f, 0.90f, 0.85f, 1f);
        private static readonly FLinearColor SubColor = new FLinearColor(0.62f, 0.58f, 0.5f, 1f);
        private static readonly FLinearColor BodyColor = new FLinearColor(0.82f, 0.79f, 0.72f, 1f);
        private static readonly FLinearColor AttrLabelColor = new FLinearColor(0.64f, 0.62f, 0.57f, 1f);
        private static readonly FLinearColor SectionColor = new FLinearColor(0.82f, 0.68f, 0.40f, 1f);
        private static readonly FLinearColor DimColor = new FLinearColor(0.58f, 0.55f, 0.49f, 1f);

        private static UBorder _frame;
        private static UCanvasPanelSlot _frameSlot;
        private static UBorder _background;
        private static UCanvasPanelSlot _backgroundSlot;
        private static UBorder _header;
        private static UCanvasPanelSlot _headerSlot;
        private static UCanvasPanel _root;
        private static UCanvasPanelSlot _rootSlot;
        private static UVerticalBox _stack;
        private static UCanvasPanelSlot _stackSlot;
        private static float _height = 1080f;
        private static UTextBlock _title;
        private static UTextBlock _qualityLabel;
        private static FSlateFontInfo _baseFont;

        private static bool _visible;
        private static int _itemId;
        private static float _cardX, _cardY, _cardW, _cardH;

        public static bool IsVisible { get { return _visible; } }

        public static void Destroy()
        {
            _frame = null;
            _frameSlot = null;
            _background = null;
            _backgroundSlot = null;
            _header = null;
            _headerSlot = null;
            _root = null;
            _rootSlot = null;
            _stack = null;
            _stackSlot = null;
            _title = null;
            _qualityLabel = null;
            _visible = false;
            _itemId = 0;
        }

        public static void Build(UCanvasPanel content, UWidget styleRoot)
        {
            if (content == null) return;

            _frame = UObject.NewObject<UBorder>(content);
            _frame.SetBrushColor(Gold);
            _frameSlot = content.AddChildToCanvas(_frame);
            _frameSlot.SetPosition(new FVector2D(0f, 0f));
            _frameSlot.SetSize(new FVector2D(Width + 6f, _height + 6f));
            _frameSlot.SetZOrder(70);

            _background = UObject.NewObject<UBorder>(content);
            _background.SetBrushColor(PanelDark);
            _backgroundSlot = content.AddChildToCanvas(_background);
            _backgroundSlot.SetPosition(new FVector2D(3f, 3f));
            _backgroundSlot.SetSize(new FVector2D(Width, _height));
            _backgroundSlot.SetZOrder(71);

            // 顶部稀有度色带（半透明），对应原生详情框的彩色标题背景。
            _header = UObject.NewObject<UBorder>(content);
            _header.SetBrushColor(new FLinearColor(0.2f, 0.1f, 0.05f, 0.35f));
            _headerSlot = content.AddChildToCanvas(_header);
            _headerSlot.SetPosition(new FVector2D(3f, 3f));
            _headerSlot.SetSize(new FVector2D(Width, HeaderHeight));
            _headerSlot.SetZOrder(71);

            // 文本放在独立画布里，定位时整体移动，内部坐标保持不变。
            _root = UObject.NewObject<UCanvasPanel>(content);
            _rootSlot = content.AddChildToCanvas(_root);
            _rootSlot.SetPosition(new FVector2D(0f, 0f));
            _rootSlot.SetSize(new FVector2D(Width, _height));
            _rootSlot.SetZOrder(72);
            // 内容超出时裁在浮框内，避免正文画到框外。
            _root.SetClipping(EWidgetClipping.ClipToBounds);

            _qualityLabel = UObject.NewObject<UTextBlock>(_root);
            if (styleRoot != null) UIEquipPageHook.TryCopyTextStyle(styleRoot, _qualityLabel);
            _baseFont = _qualityLabel.Font;
            _qualityLabel.SetJustification(ETextJustify.Left);
            _qualityLabel.SetAutoWrapText(false);
            UCanvasPanelSlot qualitySlot = _root.AddChildToCanvas(_qualityLabel);
            qualitySlot.SetPosition(new FVector2D(36f, 24f));
            qualitySlot.SetSize(new FVector2D(Width - 72f, 48f));
            qualitySlot.SetZOrder(0);

            _title = UObject.NewObject<UTextBlock>(_root);
            _title.Font = BoldFont(SizeTitle);
            _title.SetJustification(ETextJustify.Left);
            _title.SetAutoWrapText(false);
            UCanvasPanelSlot titleSlot = _root.AddChildToCanvas(_title);
            titleSlot.SetPosition(new FVector2D(36f, 68f));
            titleSlot.SetSize(new FVector2D(Width - 72f, 84f));
            titleSlot.SetZOrder(0);

            _stack = UObject.NewObject<UVerticalBox>(_root);
            _stackSlot = _root.AddChildToCanvas(_stack);
            _stackSlot.SetPosition(new FVector2D(36f, HeaderHeight + 16f));
            _stackSlot.SetSize(new FVector2D(Width - 72f, _height - HeaderHeight - 32f));
            _stackSlot.SetZOrder(0);

            Hide();
        }

        /// <summary>显示某个格子的详情；kind 用于副标题（装备/珍玩/精魄/法术/葫芦/酒/泡酒物）。</summary>
        public static void Show(int itemId, string kind, List<int> presetEquipIds,
                                float cardX, float cardY, float cardW, float cardH)
        {
            if (itemId <= 0) { Hide(); return; }

            try
            {
                bool rebuild = !_visible || _itemId != itemId;
                _itemId = itemId;
                _cardX = cardX;
                _cardY = cardY;
                _cardW = cardW;
                _cardH = cardH;

                if (rebuild) RebuildContent(itemId, kind, presetEquipIds);

                Reposition();
                ShowWidgets();
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] detail popup failed id=" + itemId + ": " + ex.Message);
                Hide();
            }
        }

        /// <summary>格子位置没变、只是重新排版（例如面板重建后跟随同一格子）。</summary>
        public static void ShowCard(int itemId, string kind, List<int> presetEquipIds,
                                    float cardX, float cardY, float cardW, float cardH, bool forceReload)
        {
            if (forceReload) { Hide(); }
            Show(itemId, kind, presetEquipIds, cardX, cardY, cardW, cardH);
        }

        public static void Hide()
        {
            _visible = false;
            _itemId = 0;
            SetAllVisibility(ESlateVisibility.Collapsed);
        }

        private static void ShowWidgets()
        {
            _visible = true;
            SetAllVisibility(ESlateVisibility.SelfHitTestInvisible);
        }

        private static void SetAllVisibility(ESlateVisibility visibility)
        {
            if (_frame != null) _frame.SetVisibility(visibility);
            if (_background != null) _background.SetVisibility(visibility);
            if (_header != null) _header.SetVisibility(visibility);
            if (_root != null) _root.SetVisibility(visibility);
        }

        // ---------------- 内容排版 ----------------

        private static void RebuildContent(int itemId, string kind, List<int> presetEquipIds)
        {
            DetailContent content = ItemDetailText.Build(itemId, presetEquipIds);

            string name = GameData.LocalizedName(itemId);
            if (string.IsNullOrEmpty(name)) name = GameData.ItemName(itemId);
            if (string.IsNullOrEmpty(name)) name = L.T("popup.unknown_item");

            int quality = GameData.ItemQuality(itemId);
            FLinearColor qualityColor = QualityColor(quality);

            _qualityLabel.SetText(FText.FromString(content.QualityLabel));
            _qualityLabel.Font = FontOf(SizeQuality);
            _qualityLabel.SetColorAndOpacity(ToSlate(quality >= 1 && quality <= 6
                ? qualityColor : SubColor));

            _title.SetText(FText.FromString(name));
            _title.Font = BoldFont(SizeTitle);
            _title.SetColorAndOpacity(ToSlate(TitleColor));

            _header.SetBrushColor(new FLinearColor(
                qualityColor.R * 0.45f, qualityColor.G * 0.45f, qualityColor.B * 0.45f, 0.30f));

            _stack.ClearChildren();

            // 属性区：每行“名称 + 数值”，和原生详情页一致。
            if (content.Attrs.Count > 0)
            {
                AddDivider(qualityColor);
                foreach (KeyValuePair<string, string> attr in content.Attrs) AddAttrRow(attr.Key, attr.Value);
            }

            // 分区：效果 / 套装 / 背景故事，区域之间用同稀有度的细线隔开。
            foreach (DetailSection section in content.Sections)
            {
                if (string.IsNullOrEmpty(section.Text) && string.IsNullOrEmpty(section.Title)) continue;

                AddSpacer(6f);
                AddDivider(qualityColor);
                if (!string.IsNullOrEmpty(section.Title))
                {
                    AddText(section.Title, SizeSection, SectionColor, true, 14f);
                }
                AddText(section.Text, SizeBody, section.Dim ? DimColor : BodyColor, false, 6f);
            }

            if (content.Attrs.Count == 0 && content.Sections.Count == 0)
            {
                AddText(L.T("popup.no_more"), SizeBody, BodyColor, false, 6f);
            }

            // 按内容估算高度：信息多就高、信息少就矮，一个框刚好装下所有文字。
            _height = EstimateHeight(content);
            if (_stackSlot != null)
                _stackSlot.SetSize(new FVector2D(Width - 72f, _height - HeaderHeight - 32f));
            ApplyBoxSize();
        }

        /// <summary>按文本行数估算浮框高度（Slate 行高约 1.35 倍字号）。</summary>
        private static float EstimateHeight(DetailContent content)
        {
            float h = HeaderHeight + 16f;   // 标题区 + 内容起点
            h += DividerBlock();            // 属性区顶部细线

            foreach (KeyValuePair<string, string> attr in content.Attrs)
                h += LineHeight(SizeAttr) + 8f;

            foreach (DetailSection section in content.Sections)
            {
                if (string.IsNullOrEmpty(section.Text) && string.IsNullOrEmpty(section.Title)) continue;
                h += 6f + DividerBlock();
                if (!string.IsNullOrEmpty(section.Title)) h += LineHeight(SizeSection) + 14f;
                h += TextHeight(section.Text, SizeBody) + 6f;
            }

            if (content.Attrs.Count == 0 && content.Sections.Count == 0)
                h += LineHeight(SizeBody);

            // 中文换行比按字数估算更容易多折一行，固定预留一行余量，避免最后一行被裁。
            h += LineHeight(SizeBody);
            h += 40f; // 底部留白
            if (h < MinHeight) h = MinHeight;
            if (h > MaxHeight) h = MaxHeight;
            return h;
        }

        private static float DividerBlock()
        {
            return 2f + 6f + 4f;
        }

        private static float LineHeight(int size)
        {
            // 中文字体的实际行距比字号大不少（约 1.5 倍），估高时留足余量避免裁字。
            return size * 1.6f;
        }

        private static float TextHeight(string text, int size)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            float unitsPerLine = Math.Max(1f, (Width - 72f) / (size * 1.22f));
            int lines = 0;
            foreach (string raw in text.Split('\n'))
            {
                float units = 0f;
                foreach (char ch in raw)
                {
                    if (ch >= 0x2E80) units += 1f;
                    else if (ch == ' ') units += 0.35f;
                    else if (ch < 0x80) units += 0.55f;
                    else units += 1f;
                }
                lines += Math.Max(1, (int)Math.Ceiling(units / unitsPerLine));
            }
            return lines * LineHeight(size);
        }

        /// <summary>把动态高度同步到外框、背景和内容画布。</summary>
        private static void ApplyBoxSize()
        {
            if (_frameSlot != null) _frameSlot.SetSize(new FVector2D(Width + 6f, _height + 6f));
            if (_backgroundSlot != null) _backgroundSlot.SetSize(new FVector2D(Width, _height));
            if (_rootSlot != null) _rootSlot.SetSize(new FVector2D(Width, _height));
        }

        private static void AddDivider(FLinearColor color)
        {
            if (_stack == null) return;

            UBorder line = UObject.NewObject<UBorder>(_stack);
            line.SetBrushColor(new FLinearColor(color.R, color.G, color.B, 0.85f));
            USizeBox box = UObject.NewObject<USizeBox>(_stack);
            box.SetHeightOverride(2f);
            box.SetContent(line);
            UVerticalBoxSlot slot = _stack.AddChildToVerticalBox(box);
            slot.SetPadding(MarginOf(0f, 6f, 0f, 4f));
        }

        private static void AddSpacer(float height)
        {
            if (_stack == null) return;
            USpacer spacer = UObject.NewObject<USpacer>(_stack);
            spacer.Size = new FVector2D(0f, height);
            _stack.AddChildToVerticalBox(spacer);
        }

        private static void AddText(string text, int size, FLinearColor color, bool bold, float topPadding)
        {
            if (_stack == null || string.IsNullOrEmpty(text)) return;

            UTextBlock label = UObject.NewObject<UTextBlock>(_stack);
            label.Font = bold ? BoldFont(size) : FontOf(size);
            label.SetText(FText.FromString(text));
            label.SetColorAndOpacity(ToSlate(color));
            label.SetJustification(ETextJustify.Left);
            label.SetAutoWrapText(true);
            UVerticalBoxSlot slot = _stack.AddChildToVerticalBox(label);
            slot.SetPadding(MarginOf(0f, topPadding, 0f, 0f));
        }

        private static void AddAttrRow(string name, string value)
        {
            if (_stack == null) return;

            UHorizontalBox row = UObject.NewObject<UHorizontalBox>(_stack);
            UVerticalBoxSlot rowSlot = _stack.AddChildToVerticalBox(row);
            rowSlot.SetPadding(MarginOf(0f, 8f, 0f, 0f));

            UTextBlock label = UObject.NewObject<UTextBlock>(row);
            label.Font = FontOf(SizeAttr);
            label.SetText(FText.FromString(name));
            label.SetColorAndOpacity(ToSlate(AttrLabelColor));
            label.SetJustification(ETextJustify.Left);
            label.SetAutoWrapText(false);
            row.AddChildToHorizontalBox(label);

            USpacer spacer = UObject.NewObject<USpacer>(row);
            UHorizontalBoxSlot spacerSlot = row.AddChildToHorizontalBox(spacer);
            var fill = new FSlateChildSize();
            fill.SizeRule = ESlateSizeRule.Fill;
            fill.Value = 1f;
            spacerSlot.SetSize(fill);

            UTextBlock amount = UObject.NewObject<UTextBlock>(row);
            amount.Font = FontOf(SizeAttr);
            amount.SetText(FText.FromString(value));
            amount.SetColorAndOpacity(ToSlate(BodyColor));
            amount.SetJustification(ETextJustify.Right);
            amount.SetAutoWrapText(false);
            row.AddChildToHorizontalBox(amount);
        }

        private static FMargin MarginOf(float left, float top, float right, float bottom)
        {
            var margin = new FMargin();
            margin.Left = left;
            margin.Top = top;
            margin.Right = right;
            margin.Bottom = bottom;
            return margin;
        }

        private static FSlateFontInfo FontOf(int size)
        {
            FSlateFontInfo font = _baseFont;
            font.Size = size;
            return font;
        }

        /// <summary>标题/分区标题用同字体加描边来接近原生的粗体效果。</summary>
        private static FSlateFontInfo BoldFont(int size)
        {
            FSlateFontInfo font = _baseFont;
            font.Size = size;
            try
            {
                FFontOutlineSettings outline = font.OutlineSettings;
                outline.OutlineSize = 2;
                outline.OutlineColor = new FLinearColor(0f, 0f, 0f, 0.55f);
                font.OutlineSettings = outline;
            }
            catch { }
            return font;
        }

        private static void Reposition()
        {
            if (_frameSlot == null) return;

            float rightX = _cardX + _cardW + Gap;
            float x;
            if (rightX + Width <= 3840f - Margin) x = rightX;
            else x = _cardX - Gap - Width;
            if (x < Margin) x = Margin;
            if (x + Width > 3840f - Margin) x = 3840f - Margin - Width;

            float centerY = _cardY + _cardH * 0.5f;
            float y = centerY - _height * 0.5f;

            // 物品栏过高：浮框上边贴到物品栏上边；物品栏过低：浮框下边贴到物品栏下边。
            if (y > _cardY) y = _cardY;
            if (y + _height < _cardY + _cardH) y = _cardY + _cardH - _height;

            // 再限制在页面内容区内，避免超出屏幕/压住顶部页签。
            if (y < TopMargin) y = TopMargin;
            float maxY = ContentHeight - Margin - _height;
            if (y > maxY) y = maxY;

            _frameSlot.SetPosition(new FVector2D(x - 3f, y - 3f));
            _frameSlot.SetSize(new FVector2D(Width + 6f, _height + 6f));
            _backgroundSlot.SetPosition(new FVector2D(x, y));
            _backgroundSlot.SetSize(new FVector2D(Width, _height));
            _headerSlot.SetPosition(new FVector2D(x, y));
            _headerSlot.SetSize(new FVector2D(Width, HeaderHeight));
            _rootSlot.SetPosition(new FVector2D(x, y));
            _rootSlot.SetSize(new FVector2D(Width, _height));
        }

        private static FSlateColor ToSlate(FLinearColor color)
        {
            var slate = new FSlateColor();
            slate.SpecifiedColor = color;
            return slate;
        }

        private static FLinearColor QualityColor(int quality)
        {
            switch (quality)
            {
                case 1: return new FLinearColor(0.60f, 0.59f, 0.55f, 1f);
                case 2: return new FLinearColor(0.13f, 0.34f, 0.14f, 1f);
                case 3: return new FLinearColor(0.058f, 0.105f, 0.138f, 1f);
                case 4: return new FLinearColor(0.141f, 0.084f, 0.352f, 1f);
                case 5: return new FLinearColor(0.68f, 0.44f, 0.165f, 1f);
                case 6: return new FLinearColor(0.93f, 0.225f, 0.115f, 1f);
                default: return Gold;
            }
        }
    }

    internal sealed class DetailSection
    {
        public string Title;
        public string Text;
        public bool Dim;
    }

    internal sealed class DetailContent
    {
        public string QualityLabel = L.T("popup.item");
        public readonly List<KeyValuePair<string, string>> Attrs =
            new List<KeyValuePair<string, string>>();
        public readonly List<DetailSection> Sections = new List<DetailSection>();
    }

    /// <summary>详情文本：数值属性、特殊效果、套装效果与当前件数、背景故事。</summary>
    internal static class ItemDetailText
    {
        public static DetailContent Build(int itemId, List<int> presetEquipIds)
        {
            var content = new DetailContent();

            ResB1.ItemDesc item = null;
            ResB1.EquipDesc equip = null;
            ResB1.SoulSkillDesc soul = null;
            try { item = b1.GameDBRuntime.GetItemDesc(itemId); } catch { }
            try { equip = b1.GameDBRuntime.GetEquipDesc(itemId); } catch { }
            try { soul = b1.GameDBRuntime.GetSoulSkillDesc(itemId); } catch { }

            int quality = item != null ? (int)item.Quality : GameData.ItemQuality(itemId);
            content.QualityLabel = QualityLabel(quality);

            // 数值属性：装备/精魄的 AttrEffectId -> 同原生的属性行。
            try
            {
                int attrEffectId = 0;
                if (equip != null) attrEffectId = equip.AttrEffectId;
                if (attrEffectId <= 0 && soul != null) attrEffectId = soul.AttrEffectId;
                if (attrEffectId > 0)
                {
                    CommB1.EffectAttrList list = b1.RoleAttrHelper.CalcEquipAttrByAttrId(attrEffectId);
                    if (list != null && list.Attrs != null)
                    {
                        foreach (CommB1.EffectAttrFloat attr in list.Attrs)
                        {
                            content.Attrs.Add(new KeyValuePair<string, string>(
                                AttrName(attr.Type), AttrValue(attr)));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] detail attr failed id=" + itemId + ": " + ex.Message);
            }

            // 特殊效果 / 物品说明。优先用 data/*.json 里已本地化的文本，运行时配置里是 key。
            try
            {
                // 分语言快照（data/locale/<MOD语言>.json）优先，其次运行时 key（跟随游戏语言），
                // 最后才是 data/*.json 的中文导出快照。
                LocItemText loc = LocalizedData.Item(itemId);

                string equipEffect = PickText(LocalizedData.EquipEffect(itemId),
                    equip != null ? Clean(equip.EquipEffectDesc) : null,
                    Clean(GameData.EquipEffectDesc(itemId)));

                ItemTexts texts = GameData.ItemText(itemId);
                string effect = PickText(loc != null ? loc.Effect : null,
                    Clean(item != null ? item.EffectDesc : null),
                    texts != null ? Clean(texts.Effect) : null);
                string hud = PickText(loc != null ? loc.Hud : null,
                    Clean(item != null ? item.HudEffectDesc : null),
                    texts != null ? Clean(texts.HudEffect) : null);

                if (!string.IsNullOrEmpty(equipEffect))
                {
                    AddSection(content, L.T("popup.unique"), equipEffect, false);
                }
                else if (!string.IsNullOrEmpty(effect))
                {
                    // f15 已是详情用的完整效果，f24 是 HUD 用的精简版，内容高度重复；
                    // 原生详情页只显示一份，这里只在 f15 缺失时才退回 f24。
                    AddSection(content, L.T("popup.effect"), effect, false);
                }
                else if (!string.IsNullOrEmpty(hud))
                {
                    AddSection(content, L.T("popup.effect"), hud, false);
                }

                // 背景故事/引文放到最后，使用更暗的颜色，和原生详情页底部的题记一致。
                var flavor = new StringBuilder();
                string desc = PickText(loc != null ? loc.Desc : null,
                    Clean(item != null ? item.Desc : null),
                    texts != null ? Clean(texts.Desc) : null);
                string brief = PickText(loc != null ? loc.Brief : null,
                    Clean(item != null ? item.BriefDesc : null),
                    texts != null ? Clean(texts.Brief) : null);
                if (!string.IsNullOrEmpty(desc)) flavor.Append(desc);
                if (!string.IsNullOrEmpty(brief))
                {
                    if (flavor.Length > 0) flavor.Append("\n");
                    flavor.Append("「").Append(brief).Append("」");
                }
                if (flavor.Length > 0)
                {
                    content.Sections.Add(new DetailSection { Text = flavor.ToString(), Dim = true });
                }
            }
            catch { }

            // 套装：当前方案里同一套装的件数 + 每档效果。
            try
            {
                if (equip != null && equip.SuitId != 0)
                {
                    BtlB1.FUStSuitDesc suit = b1.BGW_GameDB.GetSuitDesc(equip.SuitId);
                    if (suit != null && suit.SuitInfo != null && suit.SuitInfo.Count > 0)
                    {
                        int owned = 0;
                        if (presetEquipIds != null && presetEquipIds.Count > 0)
                        {
                            var counts = b1.RoleAttrHelper.CalcEquipSuitList(new List<int>(presetEquipIds));
                            counts.TryGetValue(equip.SuitId, out owned);
                        }

                        int max = 0;
                        foreach (BtlB1.FUStSuitInfo info in suit.SuitInfo)
                        {
                            if (info.TriggerNum > max) max = info.TriggerNum;
                        }

                        var sb = new StringBuilder();
                        foreach (BtlB1.FUStSuitInfo info in suit.SuitInfo)
                        {
                            bool active = owned >= info.TriggerNum;
                            string effectText = LocalizedData.SuitEffect(equip.SuitId, info.TriggerNum);
                            if (string.IsNullOrEmpty(effectText)) effectText = Clean(info.SuitEffectDesc);
                            if (sb.Length > 0) sb.Append("\n");
                            sb.Append(active ? "◆ " : "◇ ").Append(L.F("popup.pieces", info.TriggerNum))
                              .Append(effectText);
                        }

                        string suitName = LocalizedData.SuitName(equip.SuitId);
                        if (string.IsNullOrEmpty(suitName)) suitName = Clean(suit.SuitName);
                        if (string.IsNullOrEmpty(suitName)) suitName = L.T("popup.unknown_suit");
                        var section = new DetailSection
                        {
                            Title = L.F("popup.suit_title", suitName, owned, max),
                            Text = sb.ToString()
                        };
                        // 套装放在效果之后、题记之前。
                        int insert = content.Sections.Count;
                        for (int i = 0; i < content.Sections.Count; i++)
                        {
                            if (content.Sections[i].Dim) { insert = i; break; }
                        }
                        content.Sections.Insert(insert, section);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] detail suit failed id=" + itemId + ": " + ex.Message);
            }

            return content;
        }

        private static void AddSection(DetailContent content, string title, string text, bool dim)
        {
            if (string.IsNullOrEmpty(text)) return;
            content.Sections.Add(new DetailSection { Title = title, Text = text, Dim = dim });
        }

        /// <summary>
        /// 三种文本来源择优：分语言快照 &gt; 运行时文本 &gt; data/*.json 快照。
        /// MOD 与游戏语言不同且 MOD 是中文时，中文 JSON 快照优先于游戏文本。
        /// </summary>
        private static string PickText(string snapshot, string live, string legacy)
        {
            if (!string.IsNullOrEmpty(snapshot)) return snapshot;

            if (GameData.ModLanguageUsesLegacyChinese)
            {
                if (!string.IsNullOrEmpty(legacy)) return legacy;
                if (!string.IsNullOrEmpty(live)) return live;
            }
            else
            {
                if (!string.IsNullOrEmpty(live)) return live;
                if (!string.IsNullOrEmpty(legacy)) return legacy;
            }
            return null;
        }

        /// <summary>先按本地化 key 解析文本，再去掉富文本标记（&lt;{0}_KW&gt;、&lt;/&gt; 等）。</summary>
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // JSON 导出的文本已经是中文，只有运行时配置里的 "ItemDesc.xxx.xxx" 这类 key 才需要解析。
            if (!HasCjk(text))
            {
                try { text = text.ToFText(false).ToString(); }
                catch { }
            }

            var sb = new StringBuilder(text.Length);
            bool inTag = false;
            foreach (char ch in text)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (inTag) continue;
                if (ch == '\r') continue;
                sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        private static bool HasCjk(string text)
        {
            foreach (char ch in text)
            {
                if (ch >= 0x2E80) return true;
            }
            return false;
        }

        private static string QualityLabel(int quality)
        {
            // MOD 语言 != 游戏语言时，游戏词表只会返回游戏语言，直接用 MOD 自己的译名。
            if (!GameData.ModLanguageDiffersFromGame)
            {
                try
                {
                    B1UI.GSUI.EUIWordID word = (B1UI.GSUI.EUIWordID)
                        ((int)B1UI.GSUI.EUIWordID.QUALITY_NAME_START + quality);
                    string text = B1UI.GSUI.GSB1UIUtil.GetUIWordDescFText(word).ToString();
                    if (!string.IsNullOrEmpty(text)) return text;
                }
                catch { }
            }

            if (quality >= 1 && quality <= 6) return L.T("quality." + quality);
            return L.T("quality.0");
        }

        private static string AttrName(BtlShare.EBGUAttrFloat type)
        {
            // 分语言快照里带属性名（导出时按该语言写入），优先用它。
            string snapshot = LocalizedData.Attr((int)type);
            if (!string.IsNullOrEmpty(snapshot)) return snapshot;

            try
            {
                B1UI.GSUI.EUIWordID word = (B1UI.GSUI.EUIWordID)
                    ((int)B1UI.GSUI.EUIWordID.ATTR_NAME_START + (int)type);
                string text = B1UI.GSUI.GSB1UIUtil.GetUIWordDescFText(word).ToString();
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch { }
            return L.T("popup.attrs") + (int)type;
        }

        private static string AttrValue(CommB1.EffectAttrFloat attr)
        {
            if (ShowPercentage(attr.Type))
            {
                float value = (float)((int)((float)attr.Value * 0.1f)) * 0.1f;
                return value + "%";
            }
            return attr.Value.ToString();
        }

        /// <summary>与 VIEquipAttr.ShowPercentage 相同的百分比判定（IL 里是无符号区间比较）。</summary>
        private static bool ShowPercentage(BtlShare.EBGUAttrFloat type)
        {
            int t = (int)type;
            if (t <= 112)
            {
                if ((uint)(t - 53) <= 1) return true;
                if ((uint)(t - 111) <= 1) return true;
                return false;
            }
            if ((uint)(t - 117) <= 3) return true;
            if ((uint)(t - 161) <= 1) return true;
            if ((uint)(t - 167) > 3) return false;
            return true;
        }
    }
}
