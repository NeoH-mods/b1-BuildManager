using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CSharpModBase;

namespace BuildManager
{
    internal sealed class PresetEquip
    {
        public int Slot;
        public int EquipId;
        public ulong Uid;
    }

    internal sealed class PresetAccessory
    {
        public int Pos;
        public int EquipId;
        public ulong Uid;
    }

    internal sealed class PresetSoulSkill
    {
        public int SoulSkillId;
        public ulong Uid;
    }

    internal sealed class PresetWine
    {
        public int WineId;
        public ulong Uid;
        public List<PresetWinePartner> Partners = new List<PresetWinePartner>();
    }

    internal sealed class PresetWinePartner
    {
        public int Pos;
        public int ItemId;
    }

    internal sealed class PresetSpell
    {
        public int Type;
        public int SpellId;
    }

    internal sealed class PresetData
    {
        public int Version = 3;
        public string Name;
        public string SavedAt;
        public List<PresetEquip> Equips = new List<PresetEquip>();
        public List<PresetAccessory> Accessories = new List<PresetAccessory>();
        public PresetSoulSkill SoulSkill;
        public PresetWine Wine;
        public List<PresetSpell> Spells = new List<PresetSpell>();
    }

    internal static class PresetStore
    {
        public static string PathFor(string name)
        {
            return Path.Combine(ModPaths.PresetsDir, Sanitize(name) + ".json");
        }

        public static bool Exists(string name)
        {
            return File.Exists(PathFor(name));
        }

        public static bool Delete(string name)
        {
            string path = PathFor(name);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                Log.Info("[BuildManager] preset deleted: " + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] delete preset failed " + path + ": " + ex);
                return false;
            }
        }

        public static bool Save(string name, PresetData data)
        {
            string path = PathFor(name);
            string tmp = path + ".tmp";

            try
            {
                string json = Serialize(data);
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }

                Log.Info("[BuildManager] preset saved: " + path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] save preset failed " + path + ": " + ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }

        public static PresetData Load(string name)
        {
            string path = PathFor(name);
            if (!File.Exists(path))
            {
                Log.Warn("[BuildManager] preset not found: " + path + " (save it first)");
                return null;
            }

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                Dictionary<string, object> root = Json.AsObject(Json.Parse(text));
                if (root == null) throw new FormatException("preset root is not a JSON object");

                var data = new PresetData
                {
                    Version = Json.ToInt(Json.Get(root, "Version")),
                    Name = Json.ToStringValue(Json.Get(root, "Name")),
                    SavedAt = Json.ToStringValue(Json.Get(root, "SavedAt")),
                    Equips = ReadEquips(Json.AsArray(Json.Get(root, "Equips"))),
                    Accessories = ReadAccessories(Json.AsArray(Json.Get(root, "Accessories"))),
                    SoulSkill = ReadSoulSkill(Json.AsObject(Json.Get(root, "SoulSkill"))),
                    Wine = ReadWine(Json.AsObject(Json.Get(root, "Wine"))),
                    Spells = ReadSpells(Json.AsArray(Json.Get(root, "Spells"))),
                };
                return data;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] load preset failed " + path + ": " + ex);
                return null;
            }
        }

        private static string Serialize(PresetData data)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            bool first = true;

            AppendKey(sb, ref first, "Version");
            sb.Append(data.Version);

            AppendKey(sb, ref first, "Name");
            sb.Append(Json.Escape(data.Name));

            AppendKey(sb, ref first, "SavedAt");
            sb.Append(Json.Escape(data.SavedAt));

            AppendKey(sb, ref first, "Equips");
            sb.Append('[');
            List<PresetEquip> equips = data.Equips ?? new List<PresetEquip>();
            for (int i = 0; i < equips.Count; i++)
            {
                if (i > 0) sb.Append(',');
                PresetEquip equip = equips[i];
                sb.Append("{\"Slot\":").Append(equip.Slot)
                  .Append(",\"EquipId\":").Append(equip.EquipId)
                  .Append(",\"Uid\":").Append(equip.Uid).Append('}');
            }
            sb.Append(']');

            AppendKey(sb, ref first, "Accessories");
            sb.Append('[');
            List<PresetAccessory> accessories = data.Accessories ?? new List<PresetAccessory>();
            for (int i = 0; i < accessories.Count; i++)
            {
                if (i > 0) sb.Append(',');
                PresetAccessory accessory = accessories[i];
                sb.Append("{\"Pos\":").Append(accessory.Pos)
                  .Append(",\"EquipId\":").Append(accessory.EquipId)
                  .Append(",\"Uid\":").Append(accessory.Uid).Append('}');
            }
            sb.Append(']');

            AppendKey(sb, ref first, "SoulSkill");
            if (data.SoulSkill == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append("{\"SoulSkillId\":").Append(data.SoulSkill.SoulSkillId)
                  .Append(",\"Uid\":").Append(data.SoulSkill.Uid).Append('}');
            }

            AppendKey(sb, ref first, "Wine");
            if (data.Wine == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append("{\"WineId\":").Append(data.Wine.WineId)
                  .Append(",\"Uid\":").Append(data.Wine.Uid)
                  .Append(",\"Partners\":[");
                List<PresetWinePartner> partners = data.Wine.Partners ?? new List<PresetWinePartner>();
                for (int i = 0; i < partners.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    PresetWinePartner partner = partners[i];
                    sb.Append("{\"Pos\":").Append(partner.Pos)
                      .Append(",\"ItemId\":").Append(partner.ItemId).Append('}');
                }
                sb.Append("]}");
            }

            AppendKey(sb, ref first, "Spells");
            sb.Append('[');
            List<PresetSpell> spells = data.Spells ?? new List<PresetSpell>();
            for (int i = 0; i < spells.Count; i++)
            {
                if (i > 0) sb.Append(',');
                PresetSpell spell = spells[i];
                sb.Append("{\"Type\":").Append(spell.Type)
                  .Append(",\"SpellId\":").Append(spell.SpellId).Append('}');
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendKey(StringBuilder sb, ref bool first, string key)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Json.Escape(key)).Append(':');
        }

        private static List<PresetEquip> ReadEquips(List<object> items)
        {
            var result = new List<PresetEquip>();
            if (items == null) return result;
            foreach (object item in items)
            {
                Dictionary<string, object> obj = Json.AsObject(item);
                if (obj == null) continue;
                result.Add(new PresetEquip
                {
                    Slot = Json.ToInt(Json.Get(obj, "Slot")),
                    EquipId = Json.ToInt(Json.Get(obj, "EquipId")),
                    Uid = Json.ToUlong(Json.Get(obj, "Uid")),
                });
            }
            return result;
        }

        private static List<PresetAccessory> ReadAccessories(List<object> items)
        {
            var result = new List<PresetAccessory>();
            if (items == null) return result;
            foreach (object item in items)
            {
                Dictionary<string, object> obj = Json.AsObject(item);
                if (obj == null) continue;
                result.Add(new PresetAccessory
                {
                    Pos = Json.ToInt(Json.Get(obj, "Pos")),
                    EquipId = Json.ToInt(Json.Get(obj, "EquipId")),
                    Uid = Json.ToUlong(Json.Get(obj, "Uid")),
                });
            }
            return result;
        }

        private static PresetSoulSkill ReadSoulSkill(Dictionary<string, object> obj)
        {
            if (obj == null) return null;
            return new PresetSoulSkill
            {
                SoulSkillId = Json.ToInt(Json.Get(obj, "SoulSkillId")),
                Uid = Json.ToUlong(Json.Get(obj, "Uid")),
            };
        }

        private static PresetWine ReadWine(Dictionary<string, object> obj)
        {
            if (obj == null) return null;

            var wine = new PresetWine
            {
                WineId = Json.ToInt(Json.Get(obj, "WineId")),
                Uid = Json.ToUlong(Json.Get(obj, "Uid")),
            };

            List<object> partners = Json.AsArray(Json.Get(obj, "Partners"));
            if (partners != null)
            {
                foreach (object item in partners)
                {
                    Dictionary<string, object> partner = Json.AsObject(item);
                    if (partner == null) continue;
                    wine.Partners.Add(new PresetWinePartner
                    {
                        Pos = Json.ToInt(Json.Get(partner, "Pos")),
                        ItemId = Json.ToInt(Json.Get(partner, "ItemId")),
                    });
                }
            }

            return wine;
        }

        private static List<PresetSpell> ReadSpells(List<object> items)
        {
            var result = new List<PresetSpell>();
            if (items == null) return result;
            foreach (object item in items)
            {
                Dictionary<string, object> obj = Json.AsObject(item);
                if (obj == null) continue;
                result.Add(new PresetSpell
                {
                    Type = Json.ToInt(Json.Get(obj, "Type")),
                    SpellId = Json.ToInt(Json.Get(obj, "SpellId")),
                });
            }
            return result;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "preset";
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name;
        }
    }
}
