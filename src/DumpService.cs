using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CSharpModBase;

namespace BuildManager
{
    internal static class DumpService
    {
        // 53/54 (AtkMul/DefMul) 在本版本 AttrList 中不存在，实测恒为 0；
        // 换装的数值验证改用 103/104 (AtkBase/DefBase) 与 101 (HpMaxBase)。
        private const int AttrHpMaxBase = 101;
        private const int AttrAtkBase = 103;
        private const int AttrDefBase = 104;

        public static string DumpCurrentBuild(string tag = null)
        {
            if (!GameApi.RequirePlayer("dump 当前配装")) return null;
            b1.GamePlayer player = B1UI.GSG.GamePlayer;

            var sb = new StringBuilder();
            sb.AppendLine("BuildManager C# dump");
            sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("modDir=" + ModPaths.ModDir);
            sb.AppendLine("dataDir=" + ModPaths.DataDir);
            try { sb.AppendLine("roleId=" + player.OnlineRoleId); } catch { }
            sb.AppendLine();

            List<WornEquip> worn = GameApi.GetWornEquips();
            sb.AppendLine("[WORN EQUIP] count=" + worn.Count);
            var wornBySlot = new Dictionary<int, WornEquip>();
            foreach (WornEquip equip in worn)
            {
                wornBySlot[equip.Position] = equip;
                sb.AppendLine(string.Format(
                    "slot={0}({1}) id={2} name={3} uid={4}",
                    equip.Position, GameData.SlotName(equip.Position), equip.Id,
                    GameData.EquipName(equip.Id), equip.Uid));
            }
            sb.AppendLine();

            List<WornAccessory> accessories = GameApi.GetWornAccessories();
            sb.AppendLine("[ACCESSORY] count=" + accessories.Count);
            foreach (WornAccessory accessory in accessories)
            {
                sb.AppendLine(string.Format(
                    "pos={0} id={1} name={2} uid={3}",
                    accessory.Pos, accessory.Id, GameData.EquipName(accessory.Id), accessory.Uid));
            }
            sb.AppendLine();

            WornSoulSkill soul = GameApi.GetWornSoulSkill();
            sb.AppendLine("[SOUL SKILL] id=" + soul.Id + " name=" + GameData.SoulSkillName(soul.Id) + " uid=" + soul.Uid);

            int wineId = GameApi.GetCurrentWineId();
            sb.AppendLine("[WINE] wineId=" + wineId);

            BagWine currentWine = GameApi.GetBagWines().Find(w => w.WineId == wineId);
            sb.AppendLine("[WINE PARTNERS] wineId=" + wineId + " count=" +
                          (currentWine != null && currentWine.Partners != null ? currentWine.Partners.Count : 0));
            if (currentWine != null && currentWine.Partners != null)
            {
                foreach (WinePartnerSnap partner in currentWine.Partners)
                {
                    sb.AppendLine("pos=" + partner.Pos + " itemId=" + partner.ItemId);
                }
            }
            sb.AppendLine();

            List<WornSpell> spells = GameApi.GetWornSpells();
            sb.AppendLine("[SPELLS] count=" + spells.Count);
            foreach (WornSpell spell in spells)
            {
                sb.AppendLine(string.Format(
                    "type={0} spellId={1} name={2}",
                    spell.Type, spell.Id, GameData.SpellName(spell.Id)));
            }
            sb.AppendLine();

            List<int> learnedSpells = new List<int>();
            int spellTotal = 0;
            try
            {
                foreach (int spellId in GameData.GetSpellIds())
                {
                    if (spellId == 0) continue;
                    spellTotal++;
                    try
                    {
                        if (player.ActorHelper != null && player.ActorHelper.IsSpellExist(spellId))
                        {
                            learnedSpells.Add(spellId);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("[LEARNED SPELLS] error: " + ex.Message);
            }
            sb.AppendLine("[LEARNED SPELLS] " + learnedSpells.Count + "/" + spellTotal);
            foreach (int spellId in learnedSpells)
            {
                sb.AppendLine("spellId=" + spellId + " name=" + GameData.SpellName(spellId) +
                              " type=" + GameData.SpellType(spellId));
            }
            sb.AppendLine();

            string atkText = AttrValue(AttrAtkBase);
            string defText = AttrValue(AttrDefBase);
            sb.AppendLine("[ATTR] AtkBase=" + atkText + " DefBase=" + defText +
                          " HpMaxBase=" + AttrValue(AttrHpMaxBase));
            sb.AppendLine();

            List<AttrEntry> attrEntries = GameApi.GetEffectAttrs();
            sb.AppendLine("[ATTR LIST] count=" + attrEntries.Count);
            foreach (AttrEntry entry in attrEntries)
            {
                sb.AppendLine(string.Format(
                    "type={0}({1}) value={2}",
                    entry.Type, AttrName(entry.Type),
                    entry.Value.ToString("0.###", CultureInfo.InvariantCulture)));
            }
            sb.AppendLine();

            List<BagEquip> bagEquips = GameApi.GetBagEquips();
            List<BagSoulSkill> bagSouls = GameApi.GetBagSoulSkills();
            List<BagWine> bagWines = GameApi.GetBagWines();
            sb.AppendLine(string.Format(
                "[BAG] equips={0} soulSkills={1} wines={2}",
                bagEquips.Count, bagSouls.Count, bagWines.Count));
            sb.AppendLine();

            sb.AppendLine("[BAG EQUIP UID MAP]");
            bagEquips.Sort((a, b) => a.Id != b.Id ? a.Id.CompareTo(b.Id) : a.Uid.CompareTo(b.Uid));
            foreach (BagEquip equip in bagEquips)
            {
                sb.AppendLine(string.Format(
                    "id={0} name={1} slot={2} uid={3}",
                    equip.Id, GameData.EquipName(equip.Id), GameData.EquipSlot(equip.Id), equip.Uid));
            }
            sb.AppendLine();

            sb.AppendLine("[BAG SOUL SKILL UID MAP]");
            foreach (BagSoulSkill item in bagSouls)
            {
                sb.AppendLine("id=" + item.Id + " name=" + GameData.SoulSkillName(item.Id) + " uid=" + item.Uid);
            }
            sb.AppendLine();

            sb.AppendLine("[BAG WINE UID MAP]");
            foreach (BagWine item in bagWines)
            {
                var partnerText = new StringBuilder();
                if (item.Partners != null)
                {
                    foreach (WinePartnerSnap partner in item.Partners)
                    {
                        if (partnerText.Length > 0) partnerText.Append(' ');
                        partnerText.Append(partner.Pos).Append(':').Append(partner.ItemId);
                    }
                }
                sb.AppendLine("wineId=" + item.WineId + " uid=" + item.Uid +
                              " partners=[" + partnerText + "]");
            }

            string fileName = "dump_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            if (!string.IsNullOrEmpty(tag)) fileName += "_" + SanitizeTag(tag);
            string path = Path.Combine(ModPaths.DumpDir, fileName + ".txt");

            try
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] dump file write failed: " + ex);
                path = null;
            }

            Log.Info("[BuildManager] dump done: worn=" + worn.Count + ", curios=" + accessories.Count +
                     ", spells=" + spells.Count + ", bagEquips=" + bagEquips.Count +
                     ", AtkBase=" + atkText + ", DefBase=" + defText);
            foreach (WornEquip equip in worn)
            {
                Log.Info("  [" + GameData.SlotKey(equip.Position) + "] id=" + equip.Id + " uid=" + equip.Uid);
            }
            if (path != null) Log.Info("[BuildManager] dump file: " + path);

            return path;
        }

        private static string AttrValue(int attrId)
        {
            float effect = GameApi.GetEffectAttrFloat(attrId);
            return float.IsNaN(effect) ? "N/A" : effect.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string AttrName(int type)
        {
            try
            {
                string name = Enum.GetName(typeof(BtlShare.EBGUAttrFloat), type);
                return name ?? type.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return type.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string SanitizeTag(string tag)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                tag = tag.Replace(invalid, '_');
            }
            return tag;
        }
    }
}
