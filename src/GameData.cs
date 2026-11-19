using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using b1.Localization;
using CSharpModBase;

namespace BuildManager
{
    internal sealed class EquipMeta
    {
        public int Id;
        public int Slot;
        public string Name;
        public int Series;
        public string EffectDesc;

        public string Display
        {
            get
            {
                return string.IsNullOrEmpty(Name) ? Id.ToString() : Name + "(" + Id + ")";
            }
        }
    }

    /// <summary>ItemDesc 里的物品文本（JSON 导出已是本地化后的中文）。</summary>
    internal sealed class ItemTexts
    {
        public string Brief;
        public string Desc;
        public string Effect;
        public string HudEffect;
    }

    /// <summary>
    /// 静态表（EquipDesc / SpellDesc / SoulSkillDesc）。开发期读 Lua 版 data 目录。
    /// 用自带的 Json 解析器，不依赖游戏 Mono 的 DataContractJsonSerializer。
    /// </summary>
    internal static class GameData
    {
        private static readonly object Gate = new object();
        private static bool _equipLoaded;
        private static bool _spellLoaded;
        private static bool _soulLoaded;
        private static bool _itemLoaded;
        private static Dictionary<int, EquipMeta> _equips;
        private static Dictionary<int, string> _spellNames;
        private static Dictionary<int, int> _spellTypes;
        private static List<int> _spellIds;
        private static Dictionary<int, string> _soulNames;
        private static Dictionary<int, string> _itemNames;
        private static Dictionary<int, int> _itemQualities;
        private static Dictionary<int, ItemTexts> _itemTexts;
        private static string _lastError;

        public static string LastError { get { return _lastError; } }

        public static string SlotName(int slot)
        {
            if (slot >= 0 && slot <= 7) return L.T("slot." + slot);
            return L.F("slot.n", slot);
        }

        /// <summary>控制台日志用 ASCII 槽位名，避免加载器控制台编码不一致导致乱码。</summary>
        public static string SlotKey(int slot)
        {
            switch (slot)
            {
                case 0: return "Head";
                case 1: return "Chest";
                case 2: return "Arms";
                case 3: return "Legs";
                case 4: return "Gourd";
                case 5: return "Weapon";
                case 6: return "Relic";
                case 7: return "Curio";
                default: return "Slot" + slot;
            }
        }

        public static bool TryGetEquip(int id, out EquipMeta meta)
        {
            EnsureEquips();
            meta = null;
            return _equips != null && _equips.TryGetValue(id, out meta);
        }

        public static string EquipName(int id)
        {
            EquipMeta meta;
            return TryGetEquip(id, out meta) ? meta.Display : id.ToString();
        }

        public static int EquipSlot(int id)
        {
            EquipMeta meta;
            return TryGetEquip(id, out meta) ? meta.Slot : -1;
        }

        public static string SpellName(int id)
        {
            EnsureSpells();
            string name;
            return _spellNames != null && _spellNames.TryGetValue(id, out name) ? name + "(" + id + ")" : id.ToString();
        }

        public static int SpellType(int id)
        {
            EnsureSpells();
            int type;
            return _spellTypes != null && _spellTypes.TryGetValue(id, out type) ? type : -1;
        }

        public static List<int> GetSpellIds()
        {
            EnsureSpells();
            return _spellIds ?? new List<int>();
        }

        /// <summary>
        /// UI 显示名：优先读运行时配置里的本地化 key，按当前游戏语言解析；
        /// 取不到再回退到 data/*.json（导出时是中文）。
        /// </summary>
        /// <summary>MOD 语言是否与游戏语言不同。不同时物品文本要优先找 MOD 语言的数据源。</summary>
        public static bool ModLanguageDiffersFromGame
        {
            get { return !string.Equals(L.CurrentCode, L.GameCode, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>MOD 选了中文而游戏是其它语言：data/*.json 的中文快照可以直接用。</summary>
        public static bool ModLanguageUsesLegacyChinese
        {
            get
            {
                return ModLanguageDiffersFromGame &&
                       L.CurrentCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string LocalizedName(int id)
        {
            // 名称只有一个权威来源：ItemDesc.Name。
            // 游戏原生装备页展示的也是物品表里的名字（装备 id 与物品 id 相同，
            // 310 件装备在 ItemDesc 中一应俱全），EquipDesc.EquipName 只是策划表里的
            // 中文工作名，不是原生 UI 的显示文本，不能拿来做多语言名称。
            //
            // 优先级：MOD 语言快照(ItemDesc.Name) -> 当前运行语言实时解析 ItemDesc.Name
            //        -> data/*.json 的 ItemDesc 名称（导出时是中文）-> 实在没有才用 EquipDesc。
            string localized = LocalizedData.ItemName(id);
            if (!string.IsNullOrEmpty(localized)) return localized;

            // 游戏是英文、MOD 是中文：data/*.json 本身就是中文快照，直接优先用它。
            if (ModLanguageUsesLegacyChinese)
            {
                string legacy = ItemName(id);
                if (!string.IsNullOrEmpty(legacy)) return legacy;
            }

            try
            {
                ResB1.ItemDesc item = b1.GameDBRuntime.GetItemDesc(id);
                string name = LocalizeKey(item != null ? item.Name : null);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }

            string fallback = ItemName(id);
            if (!string.IsNullOrEmpty(fallback)) return fallback;

            // 仅当物品表里确实没有这一行时（目前不存在）才退回装备表。
            try
            {
                ResB1.EquipDesc equip = b1.GameDBRuntime.GetEquipDesc(id);
                string name = LocalizeKey(equip != null ? equip.EquipName : null);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }

            return RawEquipName(id);
        }

        /// <summary>
        /// 配置表里存的是本地化 key（如 ItemDesc.1001.Name）；已经是成品文本时直接返回。
        /// key 解析失败时返回 null，方便调用方继续回退。
        /// </summary>
        public static string LocalizeKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            bool hasCjk = false;
            foreach (char ch in value)
            {
                if (ch >= 0x2E80) { hasCjk = true; break; }
            }
            if (hasCjk) return value;

            try
            {
                string text = value.ToFText(false).ToString();
                if (string.IsNullOrEmpty(text)) return null;
                if (string.Equals(text, value, StringComparison.Ordinal) && LooksLikeKey(value)) return null;
                return text;
            }
            catch
            {
                return LooksLikeKey(value) ? null : value;
            }
        }

        private static bool LooksLikeKey(string value)
        {
            return value.IndexOf('.') >= 0 && value.IndexOf(' ') < 0;
        }

        public static string SoulSkillName(int id)
        {
            EnsureSouls();
            string name;
            return _soulNames != null && _soulNames.TryGetValue(id, out name) ? name + "(" + id + ")" : id.ToString();
        }

        /// <summary>UI 用：不带 ID 的物品名（ItemDesc 是最全的名称来源）。</summary>
        public static string ItemName(int id)
        {
            EnsureItems();
            string name;
            return _itemNames != null && _itemNames.TryGetValue(id, out name) ? name : null;
        }

        /// <summary>物品稀有度（ItemDesc.f6）：1..6，0 表示未知。装备/珍玩/精魄/酒/泡酒物/法术通用。</summary>
        public static int ItemQuality(int id)
        {
            EnsureItems();
            int quality;
            return _itemQualities != null && _itemQualities.TryGetValue(id, out quality) ? quality : 0;
        }

        /// <summary>ItemDesc 文本：f8 背景短句 / f9 说明 / f15 效果 / f24 HUD 效果。</summary>
        public static ItemTexts ItemText(int id)
        {
            EnsureItems();
            ItemTexts texts;
            return _itemTexts != null && _itemTexts.TryGetValue(id, out texts) ? texts : null;
        }

        /// <summary>EquipDesc.f14：装备的“独门妙用”效果说明（JSON 已本地化）。</summary>
        public static string EquipEffectDesc(int id)
        {
            EquipMeta meta;
            return TryGetEquip(id, out meta) ? meta.EffectDesc : null;
        }

        public static string RawEquipName(int id)
        {
            EquipMeta meta;
            return TryGetEquip(id, out meta) ? meta.Name : null;
        }

        public static string RawSpellName(int id)
        {
            EnsureSpells();
            string name;
            return _spellNames != null && _spellNames.TryGetValue(id, out name) ? name : null;
        }

        public static string RawSoulName(int id)
        {
            EnsureSouls();
            string name;
            return _soulNames != null && _soulNames.TryGetValue(id, out name) ? name : null;
        }

        private static void EnsureItems()
        {
            if (_itemLoaded) return;
            lock (Gate)
            {
                if (_itemLoaded) return;
                _itemLoaded = true;
                try
                {
                    var names = new Dictionary<int, string>();
                    var qualities = new Dictionary<int, int>();
                    var texts = new Dictionary<int, ItemTexts>();
                    foreach (Dictionary<string, object> record in ReadRecords("ItemDesc.json"))
                    {
                        int id = Json.ToInt(Json.Get(record, "f1"));
                        if (id == 0) continue;

                        string name = Json.ToStringValue(Json.Get(record, "f2"));
                        if (!string.IsNullOrEmpty(name)) names[id] = name;

                        int quality = Json.ToInt(Json.Get(record, "f6"));
                        if (quality > 0) qualities[id] = quality;

                        texts[id] = new ItemTexts
                        {
                            Brief = Json.ToStringValue(Json.Get(record, "f8")),
                            Desc = Json.ToStringValue(Json.Get(record, "f9")),
                            Effect = Json.ToStringValue(Json.Get(record, "f15")),
                            HudEffect = Json.ToStringValue(Json.Get(record, "f24")),
                        };
                    }

                    _itemNames = names;
                    _itemQualities = qualities;
                    _itemTexts = texts;
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] ItemDesc load failed: " + ex);
                }
            }
        }

        private static void EnsureEquips()
        {
            if (_equipLoaded) return;
            lock (Gate)
            {
                if (_equipLoaded) return;
                _equipLoaded = true;
                try
                {
                    var map = new Dictionary<int, EquipMeta>();
                    foreach (Dictionary<string, object> record in ReadRecords("EquipDesc.json"))
                    {
                        int id = Json.ToInt(Json.Get(record, "f1"));
                        if (id == 0) continue;
                        map[id] = new EquipMeta
                        {
                            Id = id,
                            Slot = Json.ToInt(Json.Get(record, "f2")),
                            Name = Json.ToStringValue(Json.Get(record, "f3")),
                            Series = Json.ToInt(Json.Get(record, "f12")),
                            EffectDesc = Json.ToStringValue(Json.Get(record, "f14")),
                        };
                    }
                    _equips = map;
                    Log.Info("[BuildManager] EquipDesc loaded: " + map.Count + " records from " + ModPaths.DataDir);
                }
                catch (Exception ex)
                {
                    _lastError = "EquipDesc load failed: " + ex;
                    Log.Warn("[BuildManager] " + _lastError + " (data=" + ModPaths.DataDir + ")");
                }
            }
        }

        private static void EnsureSpells()
        {
            if (_spellLoaded) return;
            lock (Gate)
            {
                if (_spellLoaded) return;
                _spellLoaded = true;
                try
                {
                    var names = new Dictionary<int, string>();
                    var types = new Dictionary<int, int>();
                    var ids = new List<int>();

                    foreach (Dictionary<string, object> record in ReadRecords("SpellDesc.json"))
                    {
                        int id = Json.ToInt(Json.Get(record, "f1"));
                        if (id == 0) continue;
                        ids.Add(id);

                        string name = Json.ToStringValue(Json.Get(record, "f2"));
                        if (!string.IsNullOrEmpty(name)) names[id] = name;

                        types[id] = Json.ToInt(Json.Get(record, "f3"));
                    }

                    _spellNames = names;
                    _spellTypes = types;
                    _spellIds = ids;
                }
                catch (Exception ex)
                {
                    Log.Warn("[BuildManager] SpellDesc load failed: " + ex);
                }
            }
        }

        private static void EnsureSouls()
        {
            if (_soulLoaded) return;
            lock (Gate)
            {
                if (_soulLoaded) return;
                _soulLoaded = true;
                _soulNames = ReadNameMap("SoulSkillDesc.json");
            }
        }

        private static Dictionary<int, string> ReadNameMap(string fileName)
        {
            try
            {
                var map = new Dictionary<int, string>();
                foreach (Dictionary<string, object> record in ReadRecords(fileName))
                {
                    int id = Json.ToInt(Json.Get(record, "f1"));
                    string name = Json.ToStringValue(Json.Get(record, "f2"));
                    if (id != 0 && !string.IsNullOrEmpty(name)) map[id] = name;
                }
                return map;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] " + fileName + " load failed: " + ex);
                return null;
            }
        }

        private static List<Dictionary<string, object>> ReadRecords(string fileName)
        {
            string path = Path.Combine(ModPaths.DataDir, fileName);
            if (!File.Exists(path))
            {
                Log.Warn("[BuildManager] static table missing: " + path);
                return new List<Dictionary<string, object>>();
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            Dictionary<string, object> root = Json.AsObject(Json.Parse(text));
            List<object> records = Json.AsArray(Json.Get(root, "records"));
            var result = new List<Dictionary<string, object>>();
            if (records == null) return result;

            foreach (object item in records)
            {
                Dictionary<string, object> record = Json.AsObject(item);
                if (record != null) result.Add(record);
            }
            return result;
        }
    }
}
