using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CSharpModBase;

namespace BuildManager
{
    /// <summary>某个物品在当前语言下的名称与描述。</summary>
    internal sealed class LocItemText
    {
        public string Name;
        public string Desc;
        public string Brief;
        public string Effect;
        public string Hud;
    }

    /// <summary>
    /// 分语言物品文本表。
    ///
    /// 背景：游戏配置里存的是本地化 key，解析只能用“当前游戏语言”，没有带 culture 的查询接口。
    /// 所以当 MOD 语言和游戏语言不同时（例如游戏英文、MOD 韩语），物品名会跟着游戏走。
    /// 解决办法：用当前游戏语言导出一次 data/locale/&lt;code&gt;.json，MOD 切到该语言时优先读这份快照。
    /// </summary>
    internal static class LocalizedData
    {
        private static readonly Dictionary<int, LocItemText> Items = new Dictionary<int, LocItemText>();
        private static readonly Dictionary<int, string> EquipNames = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> EquipEffects = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> SuitNames = new Dictionary<int, string>();
        private static readonly Dictionary<int, Dictionary<int, string>> SuitEffects =
            new Dictionary<int, Dictionary<int, string>>();
        private static readonly Dictionary<int, string> AttrNames = new Dictionary<int, string>();
        private static string _loadedCode;
        private static readonly HashSet<string> BrokenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string Dir
        {
            get { return Path.Combine(Path.Combine(ModPaths.ModDir, "data"), "locale"); }
        }

        public static string PathFor(string code)
        {
            return Path.Combine(Dir, code + ".json");
        }

        public static bool HasDataFor(string code)
        {
            try { return File.Exists(PathFor(code)) && !BrokenCodes.Contains(code); }
            catch { return false; }
        }

        public static void Invalidate()
        {
            _loadedCode = null;
        }

        private static void EnsureLoaded()
        {
            string code = L.CurrentCode;
            if (string.Equals(_loadedCode, code, StringComparison.OrdinalIgnoreCase)) return;

            _loadedCode = code;
            Items.Clear();
            EquipNames.Clear();
            EquipEffects.Clear();
            SuitNames.Clear();
            SuitEffects.Clear();
            AttrNames.Clear();

            try
            {
                string path = PathFor(code);
                if (!File.Exists(path)) return;

                Dictionary<string, object> root = Json.AsObject(
                    Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (root == null) return;

                Dictionary<string, object> items = Json.AsObject(Json.Get(root, "items"));
                if (items != null)
                {
                    foreach (KeyValuePair<string, object> pair in items)
                    {
                        int id;
                        if (!int.TryParse(pair.Key, out id)) continue;

                        Dictionary<string, object> rec = Json.AsObject(pair.Value);
                        if (rec == null) continue;

                        Items[id] = new LocItemText
                        {
                            Name = Clean(Json.ToStringValue(Json.Get(rec, "n"))),
                            Desc = Clean(Json.ToStringValue(Json.Get(rec, "d"))),
                            Brief = Clean(Json.ToStringValue(Json.Get(rec, "b"))),
                            Effect = Clean(Json.ToStringValue(Json.Get(rec, "e"))),
                            Hud = Clean(Json.ToStringValue(Json.Get(rec, "h"))),
                        };
                    }
                }

                Dictionary<string, object> equips = Json.AsObject(Json.Get(root, "equips"));
                if (equips != null)
                {
                    foreach (KeyValuePair<string, object> pair in equips)
                    {
                        int id;
                        if (!int.TryParse(pair.Key, out id)) continue;
                        string name = Json.ToStringValue(pair.Value);
                        if (!string.IsNullOrEmpty(name)) EquipNames[id] = Clean(name);
                    }
                }

                Dictionary<string, object> equipEffects = Json.AsObject(Json.Get(root, "equipEffects"));
                if (equipEffects != null)
                {
                    foreach (KeyValuePair<string, object> pair in equipEffects)
                    {
                        int id;
                        if (!int.TryParse(pair.Key, out id)) continue;
                        string text = Json.ToStringValue(pair.Value);
                        if (!string.IsNullOrEmpty(text)) EquipEffects[id] = Clean(text);
                    }
                }

                Dictionary<string, object> suits = Json.AsObject(Json.Get(root, "suits"));
                if (suits != null)
                {
                    foreach (KeyValuePair<string, object> pair in suits)
                    {
                        int id;
                        if (!int.TryParse(pair.Key, out id)) continue;
                        string name = Json.ToStringValue(pair.Value);
                        if (!string.IsNullOrEmpty(name)) SuitNames[id] = Clean(name);
                    }
                }

                Dictionary<string, object> suitEffects = Json.AsObject(Json.Get(root, "suitEffects"));
                if (suitEffects != null)
                {
                    foreach (KeyValuePair<string, object> pair in suitEffects)
                    {
                        int suitId;
                        if (!int.TryParse(pair.Key, out suitId)) continue;

                        Dictionary<string, object> byCount = Json.AsObject(pair.Value);
                        if (byCount == null) continue;

                        var map = new Dictionary<int, string>();
                        foreach (KeyValuePair<string, object> inner in byCount)
                        {
                            int count;
                            if (!int.TryParse(inner.Key, out count)) continue;
                            string text = Json.ToStringValue(inner.Value);
                            if (!string.IsNullOrEmpty(text)) map[count] = Clean(text);
                        }
                        if (map.Count > 0) SuitEffects[suitId] = map;
                    }
                }

                Dictionary<string, object> attrs = Json.AsObject(Json.Get(root, "attrs"));                if (attrs != null)
                {
                    foreach (KeyValuePair<string, object> pair in attrs)
                    {
                        int id;
                        if (!int.TryParse(pair.Key, out id)) continue;
                        string name = Json.ToStringValue(pair.Value);
                        if (!string.IsNullOrEmpty(name)) AttrNames[id] = Clean(name);
                    }
                }

                BrokenCodes.Remove(code);
                Log.Info("[BuildManager] locale data loaded: " + code +
                         " items=" + Items.Count + " equips=" + EquipNames.Count +
                         " effects=" + EquipEffects.Count + " suits=" + SuitNames.Count +
                         " attrs=" + AttrNames.Count);
            }
            catch (Exception ex)
            {
                BrokenCodes.Add(code);
                Log.Warn("[BuildManager] locale data load failed (" + code + "): " + ex.Message);
            }
        }

        public static LocItemText Item(int id)
        {
            EnsureLoaded();
            LocItemText value;
            return Items.TryGetValue(id, out value) ? value : null;
        }

        public static string ItemName(int id)
        {
            LocItemText item = Item(id);
            return item == null ? null : item.Name;
        }

        public static string EquipName(int id)
        {
            EnsureLoaded();
            string value;
            return EquipNames.TryGetValue(id, out value) ? value : null;
        }

        /// <summary>装备的“独门妙用”文本（EquipDesc.EquipEffectDesc）。</summary>
        public static string EquipEffect(int id)
        {
            EnsureLoaded();
            string value;
            return EquipEffects.TryGetValue(id, out value) ? value : null;
        }

        public static string SuitName(int id)
        {
            EnsureLoaded();
            string value;
            return SuitNames.TryGetValue(id, out value) ? value : null;
        }

        /// <summary>套装分档效果文本（按套装件数）。</summary>
        public static string SuitEffect(int suitId, int triggerNum)
        {
            EnsureLoaded();
            Dictionary<int, string> map;
            if (!SuitEffects.TryGetValue(suitId, out map)) return null;
            string value;
            return map.TryGetValue(triggerNum, out value) ? value : null;
        }

        /// <summary>属性名（法力上限/防御等），按属性 id 取。</summary>
        public static string Attr(int type)
        {
            EnsureLoaded();
            string value;
            return AttrNames.TryGetValue(type, out value) ? value : null;
        }

        // ---------------- 导出 ----------------

        /// <summary>把当前游戏语言下的物品文本导出到 data/locale/&lt;code&gt;.json。</summary>
        public static bool TryDump(string code, out int items, out int equips, out int suits)
        {
            items = 0;
            equips = 0;
            suits = 0;

            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

                List<string> supported = CultureSwitch.Supported();
                Log.Info("[BuildManager] dump begin code=" + code +
                         " culture=" + CultureSwitch.RawCurrent() +
                         " supported=" + (supported == null ? "?" : string.Join(",", supported.ToArray())));

                var sb = new StringBuilder(1024 * 1024);
                sb.Append("{\n  \"language\": ").Append(Json.Escape(code)).Append(",\n");

                sb.Append("  \"items\": {");
                var sample = new StringBuilder();
                try
                {
                    ResB1.TBItemDesc table = b1.GameDBRuntime.GetTBItemDesc();
                    if (table != null && table.List != null)
                    {
                        foreach (ResB1.ItemDesc desc in table.List)
                        {
                            if (desc == null || desc.Id <= 0) continue;

                            // 配置里是本地化 key，必须先按当前 culture 解析成文本再写快照。
                            string name = GameData.LocalizeKey(desc.Name);
                            if (items < 3 && !string.IsNullOrEmpty(name)) sample.Append(name).Append(" | ");

                            if (items > 0) sb.Append(',');
                            sb.Append("\n    \"").Append(desc.Id).Append("\": {");
                            bool wrote = false;
                            AppendField(sb, ref wrote, "n", name);
                            AppendField(sb, ref wrote, "d", GameData.LocalizeKey(desc.Desc));
                            AppendField(sb, ref wrote, "b", GameData.LocalizeKey(desc.BriefDesc));
                            AppendField(sb, ref wrote, "e", GameData.LocalizeKey(desc.EffectDesc));
                            AppendField(sb, ref wrote, "h", GameData.LocalizeKey(desc.HudEffectDesc));
                            sb.Append('}');
                            items++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] dump items failed: " + ex.Message);
                }
                sb.Append("\n  },\n");

                Log.Info("[BuildManager] dump sample(" + code + "): " + sample.ToString().Trim());

                sb.Append("  \"equips\": {");
                var equipEffects = new StringBuilder();
                int effectCount = 0;
                try
                {
                    ResB1.TBEquipDesc table = b1.GameDBRuntime.GetTBEquipDesc();
                    if (table != null && table.List != null)
                    {
                        foreach (ResB1.EquipDesc desc in table.List)
                        {
                            if (desc == null || desc.Id <= 0) continue;

                            string name = GameData.LocalizeKey(desc.EquipName);
                            if (!string.IsNullOrEmpty(name))
                            {
                                if (equips > 0) sb.Append(',');
                                sb.Append("\n    \"").Append(desc.Id).Append("\": ").Append(Json.Escape(name));
                                equips++;
                            }

                            string effect = GameData.LocalizeKey(desc.EquipEffectDesc);
                            if (!string.IsNullOrEmpty(effect))
                            {
                                if (effectCount > 0) equipEffects.Append(',');
                                equipEffects.Append("\n    \"").Append(desc.Id).Append("\": ").Append(Json.Escape(effect));
                                effectCount++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] dump equips failed: " + ex.Message);
                }
                sb.Append("\n  },\n");

                sb.Append("  \"equipEffects\": {").Append(equipEffects).Append("\n  },\n");

                sb.Append("  \"suits\": {");
                var suitEffectsSb = new StringBuilder();
                int suitEffectCount = 0;
                try
                {
                    Dictionary<int, BtlB1.FUStSuitDesc> table = b1.GameDBRuntime.GetTBFUStSuitDesc();
                    if (table != null)
                    {
                        foreach (KeyValuePair<int, BtlB1.FUStSuitDesc> pair in table)
                        {
                            if (pair.Value == null) continue;

                            string name = GameData.LocalizeKey(pair.Value.SuitName);
                            if (!string.IsNullOrEmpty(name))
                            {
                                if (suits > 0) sb.Append(',');
                                sb.Append("\n    \"").Append(pair.Key).Append("\": ").Append(Json.Escape(name));
                                suits++;
                            }

                            // 套装分档效果（2件/4件/6件…）
                            if (pair.Value.SuitInfo == null || pair.Value.SuitInfo.Count == 0) continue;

                            var inner = new StringBuilder();
                            int innerCount = 0;
                            foreach (BtlB1.FUStSuitInfo info in pair.Value.SuitInfo)
                            {
                                if (info == null) continue;
                                string desc = GameData.LocalizeKey(info.SuitEffectDesc);
                                if (string.IsNullOrEmpty(desc)) continue;
                                if (innerCount > 0) inner.Append(',');
                                inner.Append("\n      \"").Append(info.TriggerNum).Append("\": ")
                                     .Append(Json.Escape(desc));
                                innerCount++;
                            }
                            if (innerCount == 0) continue;

                            if (suitEffectCount > 0) suitEffectsSb.Append(',');
                            suitEffectsSb.Append("\n    \"").Append(pair.Key).Append("\": {")
                                          .Append(inner).Append("\n    }");
                            suitEffectCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] dump suits failed: " + ex.Message);
                }
                sb.Append("\n  },\n");

                sb.Append("  \"suitEffects\": {").Append(suitEffectsSb).Append("\n  },\n");

                // 属性名（法力上限/防御等）来自游戏 UI 词表，同样只能按当前游戏语言取，
                // 所以一并写进快照，MOD 切到该语言时就能显示对应语言。
                sb.Append("  \"attrs\": {");
                int attrCount = 0;
                try
                {
                    var seen = new HashSet<int>();
                    foreach (object value in Enum.GetValues(typeof(BtlShare.EBGUAttrFloat)))
                    {
                        int id = Convert.ToInt32(value);
                        if (!seen.Add(id)) continue;

                        string name = null;
                        try
                        {
                            B1UI.GSUI.EUIWordID word = (B1UI.GSUI.EUIWordID)
                                ((int)B1UI.GSUI.EUIWordID.ATTR_NAME_START + id);
                            name = B1UI.GSUI.GSB1UIUtil.GetUIWordDescFText(word).ToString();
                        }
                        catch { }

                        if (string.IsNullOrEmpty(name)) continue;
                        if (attrCount > 0) sb.Append(',');
                        sb.Append("\n    \"").Append(id).Append("\": ").Append(Json.Escape(name));
                        attrCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] dump attrs failed: " + ex.Message);
                }
                sb.Append("\n  }\n}\n");

                File.WriteAllText(PathFor(code), sb.ToString(), new UTF8Encoding(false));
                Log.Info("[BuildManager] locale data dumped: " + code +
                         " items=" + items + " equips=" + equips +
                         " effects=" + effectCount + " suits=" + suits +
                         " suitEffects=" + suitEffectCount + " attrs=" + attrCount);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] locale data dump failed: " + ex);
                return false;
            }
        }

        private static void AppendField(StringBuilder sb, ref bool wrote, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (wrote) sb.Append(',');
            sb.Append('"').Append(key).Append("\": ").Append(Json.Escape(value));
            wrote = true;
        }

        /// <summary>去掉游戏富文本标记（&lt;{0}_KW&gt;、&lt;/&gt;、ruby 注音等）并修剪空白。</summary>
        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.IndexOf('<') < 0) return value.Trim();

            var sb = new StringBuilder(value.Length);
            bool inTag = false;
            foreach (char ch in value)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }
    }
}
