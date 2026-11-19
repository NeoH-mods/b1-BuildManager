using System;
using System.Collections.Generic;
using CSharpModBase;

namespace BuildManager
{
    internal static class PresetService
    {
        public const int StepIntervalMs = 180;

        private const int PresetVersion = 3;
        private const int EquipSlotCount = 7;       // 0..6
        private const int AccessorySlotCount = 5;   // pos 0..4
        private const int SpellTypeFirst = 1;
        private const int SpellTypeLast = 11;

        public static void SavePreset(string presetKey, string displayName = null)
        {
            if (Sequencer.IsRunning)
            {
                Log.Warn("[BuildManager] an apply job is running; save later.");
                return;
            }

            string name = "preset" + presetKey;
            if (!GameApi.RequirePlayer("save " + name)) return;

            var data = new PresetData
            {
                Version = PresetVersion,
                Name = string.IsNullOrEmpty(displayName) ? name : displayName,
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Equips = new List<PresetEquip>(),
                Accessories = new List<PresetAccessory>(),
                Spells = new List<PresetSpell>(),
            };

            Dictionary<int, WornEquip> wornBySlot = new Dictionary<int, WornEquip>();
            foreach (WornEquip equip in GameApi.GetWornEquips()) wornBySlot[equip.Position] = equip;

            // 7 个普通装备槽全部落盘；空槽写 0，应用时会还原为空。
            for (int slot = 0; slot < EquipSlotCount; slot++)
            {
                if (wornBySlot.TryGetValue(slot, out WornEquip equip))
                {
                    data.Equips.Add(new PresetEquip { Slot = slot, EquipId = equip.Id, Uid = equip.Uid });
                }
                else
                {
                    data.Equips.Add(new PresetEquip { Slot = slot, EquipId = 0, Uid = 0 });
                }
            }

            // 珍玩固定写 5 个位置（pos 0..4）；空位写 0，应用时会卸下。
            Dictionary<int, WornAccessory> accessoryByPos = new Dictionary<int, WornAccessory>();
            foreach (WornAccessory accessory in GameApi.GetWornAccessories()) accessoryByPos[accessory.Pos] = accessory;

            for (int pos = 0; pos < AccessorySlotCount; pos++)
            {
                if (accessoryByPos.TryGetValue(pos, out WornAccessory accessory))
                {
                    data.Accessories.Add(new PresetAccessory { Pos = pos, EquipId = accessory.Id, Uid = accessory.Uid });
                }
                else
                {
                    data.Accessories.Add(new PresetAccessory { Pos = pos, EquipId = 0, Uid = 0 });
                }
            }

            WornSoulSkill soul = GameApi.GetWornSoulSkill();
            data.SoulSkill = new PresetSoulSkill { SoulSkillId = soul.Id, Uid = soul.Uid };

            int wineId = GameApi.GetCurrentWineId();
            var presetWine = new PresetWine { WineId = wineId, Uid = 0 };
            if (wineId != 0)
            {
                BagWine currentWine = GameApi.GetBagWines().Find(w => w.WineId == wineId);
                if (currentWine != null && currentWine.Partners != null)
                {
                    foreach (WinePartnerSnap partner in currentWine.Partners)
                    {
                        presetWine.Partners.Add(new PresetWinePartner { Pos = partner.Pos, ItemId = partner.ItemId });
                    }
                }
            }
            data.Wine = presetWine;

            // 法术固定写 type 1..11；空槽写 0，应用时会按 type 卸下。
            Dictionary<int, WornSpell> spellByType = new Dictionary<int, WornSpell>();
            foreach (WornSpell spell in GameApi.GetWornSpells())
            {
                if (spell.Type != 0) spellByType[spell.Type] = spell;
            }

            for (int type = SpellTypeFirst; type <= SpellTypeLast; type++)
            {
                if (spellByType.TryGetValue(type, out WornSpell spell) && spell.Id != 0)
                {
                    data.Spells.Add(new PresetSpell { Type = type, SpellId = spell.Id });
                }
                else
                {
                    data.Spells.Add(new PresetSpell { Type = type, SpellId = 0 });
                }
            }

            if (!PresetStore.Save(name, data)) return;

            Log.Info("[BuildManager] saved preset " + name + ": equips " + CountNonEmpty(data.Equips) + "/" + EquipSlotCount +
                     ", curios " + CountNonEmptyAccessories(data.Accessories) + "/" + AccessorySlotCount +
                     ", soul " + (data.SoulSkill.Uid != 0 ? data.SoulSkill.SoulSkillId + "/" + data.SoulSkill.Uid : "none") +
                     ", wine " + (data.Wine.WineId != 0 ? data.Wine.WineId.ToString() : "none") +
                     ", spells " + CountNonEmptySpells(data.Spells) + "/" + (SpellTypeLast - SpellTypeFirst + 1));

            DumpService.DumpCurrentBuild("save_" + name);
        }

        public static void ApplyPreset(string presetKey)
        {
            string name = "preset" + presetKey;
            if (Sequencer.IsRunning)
            {
                Log.Warn("[BuildManager] a previous apply job is still running; ignored.");
                return;
            }

            if (!GameApi.CanSwitchWear()) return;

            PresetData data = PresetStore.Load(name);
            if (data == null) return;

            if (data.Version > PresetVersion)
            {
                Log.Warn("[BuildManager] preset version " + data.Version + " is newer than v" + PresetVersion + "; unknown fields may be ignored.");
            }
            bool exact = data.Version >= PresetVersion;
            if (data.Equips == null) data.Equips = new List<PresetEquip>();
            if (data.Accessories == null) data.Accessories = new List<PresetAccessory>();
            if (data.Spells == null) data.Spells = new List<PresetSpell>();

            Log.Info("[BuildManager] preset " + name + " version=" + data.Version +
                     (exact ? " (exact restore: empty slots are cleared)" : " (legacy: empty slots are left unchanged)"));

            List<BagEquip> bagEquips = GameApi.GetBagEquips();
            List<BagSoulSkill> bagSouls = GameApi.GetBagSoulSkills();
            List<BagWine> bagWines = GameApi.GetBagWines();

            Dictionary<int, WornEquip> wornBySlot = new Dictionary<int, WornEquip>();
            HashSet<ulong> wornUids = new HashSet<ulong>();
            foreach (WornEquip equip in GameApi.GetWornEquips())
            {
                wornBySlot[equip.Position] = equip;
                if (equip.Uid != 0) wornUids.Add(equip.Uid);
            }

            Dictionary<int, WornAccessory> accessoriesByPos = new Dictionary<int, WornAccessory>();
            foreach (WornAccessory accessory in GameApi.GetWornAccessories())
            {
                accessoriesByPos[accessory.Pos] = accessory;
            }

            var steps = new List<Action>();
            int same = 0;
            int healed = 0;
            int missing = 0;
            int empty = 0;
            var missingDetails = new List<string>();

            foreach (PresetEquip preset in data.Equips)
            {
                if (preset.Uid == 0 && preset.EquipId == 0)
                {
                    empty++;
                    if (wornBySlot.TryGetValue(preset.Slot, out WornEquip current) && current.Uid != 0)
                    {
                        if (exact)
                        {
                            int clearSlot = preset.Slot;
                            ulong clearUid = current.Uid;
                            steps.Add(() =>
                            {
                                Log.Info("[BuildManager] clear " + GameData.SlotKey(clearSlot) + " uid=" + clearUid);
                                GameApi.UnwearEquip(clearUid);
                            });
                        }
                        else
                        {
                            Log.Warn("[BuildManager] preset slot " + GameData.SlotKey(preset.Slot) +
                                     " is empty but currently wearing id=" + current.Id +
                                     "; legacy v2 preset leaves it unchanged.");
                        }
                    }
                    continue;
                }

                if (wornBySlot.TryGetValue(preset.Slot, out WornEquip worn) &&
                    worn.Uid != 0 && worn.Uid == preset.Uid)
                {
                    same++;
                    continue;
                }

                BagEquip resolved;
                ulong uid = ResolveBagUid(preset.Uid, preset.EquipId, bagEquips, out bool didHeal, out resolved);
                if (uid == 0)
                {
                    missing++;
                    missingDetails.Add(GameData.SlotName(preset.Slot) + " " + (GameData.LocalizedName(preset.EquipId) ?? GameData.EquipName(preset.EquipId)));
                    Log.Warn("[BuildManager] missing equip: slot " + GameData.SlotKey(preset.Slot) +
                             " id=" + preset.EquipId + " uid=" + preset.Uid + ", skipped (slot unchanged).");
                    continue;
                }

                if (didHeal)
                {
                    healed++;
                    Log.Warn("[BuildManager] stale uid healed by equipId: id=" + preset.EquipId +
                             " " + preset.Uid + " -> " + uid);
                }

                if (resolved != null && preset.EquipId != 0 && resolved.Id != preset.EquipId)
                {
                    Log.Warn("[BuildManager] equip changed since save (upgraded/rebuilt): slot=" +
                             GameData.SlotKey(preset.Slot) + " presetId=" + preset.EquipId +
                             " currentId=" + resolved.Id + "; using current version.");
                }

                int slot = preset.Slot;
                int equipId = preset.EquipId;
                ulong captured = uid;
                steps.Add(() =>
                {
                    Log.Info("[BuildManager] apply " + GameData.SlotKey(slot) + " id=" + equipId);
                    GameApi.WearEquip(captured);
                });
            }

            Dictionary<int, PresetAccessory> presetAccessoriesByPos = new Dictionary<int, PresetAccessory>();
            foreach (PresetAccessory preset in data.Accessories) presetAccessoriesByPos[preset.Pos] = preset;

            List<int> accessoryPositions = new List<int>();
            if (exact && data.Accessories.Count > 0)
            {
                for (int pos = 0; pos < AccessorySlotCount; pos++) accessoryPositions.Add(pos);
            }
            else
            {
                if (exact && data.Accessories.Count == 0)
                {
                    Log.Warn("[BuildManager] preset has no accessory entries; curios left unchanged.");
                }
                accessoryPositions.AddRange(presetAccessoriesByPos.Keys);
            }

            foreach (int pos in accessoryPositions)
            {
                PresetAccessory preset;
                bool hasPreset = presetAccessoriesByPos.TryGetValue(pos, out preset);

                if (!hasPreset || (preset.Uid == 0 && preset.EquipId == 0))
                {
                    if (exact && accessoriesByPos.TryGetValue(pos, out WornAccessory currentCurio) && currentCurio.Uid != 0)
                    {
                        int clearPos = pos;
                        int clearId = currentCurio.Id;
                        ulong clearUid = currentCurio.Uid;
                        steps.Add(() =>
                        {
                            Log.Info("[BuildManager] clear curio pos=" + clearPos + " id=" + clearId);
                            GameApi.UnwearAccessory(clearPos, clearId, clearUid);
                        });
                    }
                    else if (!exact)
                    {
                        Log.Warn("[BuildManager] legacy v2 preset leaves curio pos=" + pos + " unchanged.");
                    }
                    continue;
                }

                if (accessoriesByPos.TryGetValue(preset.Pos, out WornAccessory current) &&
                    current.Uid != 0 && current.Uid == preset.Uid)
                {
                    same++;
                    continue;
                }

                ulong uid = ResolveBagUid(preset.Uid, preset.EquipId, bagEquips, out bool didHeal, out _);
                if (uid == 0)
                {
                    missing++;
                    missingDetails.Add(GameData.SlotName(7) + " " + (GameData.LocalizedName(preset.EquipId) ?? GameData.EquipName(preset.EquipId)));
                    Log.Warn("[BuildManager] missing curio: pos=" + preset.Pos +
                             " id=" + preset.EquipId + " uid=" + preset.Uid + ", skipped (pos unchanged).");
                    continue;
                }

                if (didHeal)
                {
                    healed++;
                    Log.Warn("[BuildManager] stale curio uid healed by equipId: " + preset.Uid + " -> " + uid);
                }

                int wearPos = preset.Pos;
                int equipId = preset.EquipId;
                ulong captured = uid;
                steps.Add(() =>
                {
                    Log.Info("[BuildManager] apply curio pos=" + wearPos + " id=" + equipId);
                    GameApi.WearAccessory(wearPos, equipId, captured);
                });
            }

            WornSoulSkill wornSoul = GameApi.GetWornSoulSkill();
            if (data.SoulSkill != null && data.SoulSkill.Uid != 0)
            {
                if (wornSoul.Uid == data.SoulSkill.Uid)
                {
                    same++;
                }
                else
                {
                    ulong uid = ResolveSoulUid(data.SoulSkill.Uid, data.SoulSkill.SoulSkillId, bagSouls, out bool didHeal);
                    if (uid == 0)
                    {
                        missing++;
                        missingDetails.Add("精魄 " + GameData.SoulSkillName(data.SoulSkill.SoulSkillId));
                        Log.Warn("[BuildManager] missing soul skill: id=" + data.SoulSkill.SoulSkillId +
                                 ", skipped (soul unchanged).");
                    }
                    else
                    {
                        if (didHeal) healed++;
                        int soulId = data.SoulSkill.SoulSkillId;
                        ulong captured = uid;
                        steps.Add(() =>
                        {
                            Log.Info("[BuildManager] apply soul skill id=" + soulId);
                            GameApi.WearSoulSkill(captured);
                        });
                    }
                }
            }
            else if (exact && wornSoul.Uid != 0)
            {
                Log.Warn("[BuildManager] preset has no soul skill, but the game protocol has no unset path; " +
                         "keeping current soul uid=" + wornSoul.Uid + ".");
            }

            int currentWine = GameApi.GetCurrentWineId();
            if (data.Wine != null && data.Wine.WineId != 0)
            {
                if (currentWine == data.Wine.WineId)
                {
                    same++;
                }
                else if (bagWines.Exists(w => w.WineId == data.Wine.WineId))
                {
                    int wineId = data.Wine.WineId;
                    steps.Add(() =>
                    {
                        Log.Info("[BuildManager] apply wine wineId=" + wineId);
                        GameApi.SetWine(wineId);
                    });
                }
                else
                {
                    missing++;
                    missingDetails.Add("酒 " + data.Wine.WineId);
                    Log.Warn("[BuildManager] wine not in bag: wineId=" + data.Wine.WineId +
                             ", skipped (wine unchanged).");
                }
            }
            else if (exact && currentWine != 0)
            {
                Log.Warn("[BuildManager] preset has no wine, but the game protocol has no unset path; " +
                         "keeping current wineId=" + currentWine + ".");
            }

            if (data.Wine != null && data.Wine.WineId != 0 &&
                data.Wine.Partners != null && data.Wine.Partners.Count > 0)
            {
                BagWine targetWine = bagWines.Find(w => w.WineId == data.Wine.WineId);
                if (targetWine == null)
                {
                    // 酒本身缺失，上面已经报过 missing，这里不再重复
                }
                else
                {
                    int partnerMissing;
                    List<WinePartnerSnap> partners = BuildEffectiveWinePartners(
                        targetWine.Partners, data.Wine.Partners, bagWines, missingDetails, out partnerMissing);
                    missing += partnerMissing;

                    if (!WineSnapsEqual(targetWine.Partners, partners))
                    {
                        int targetWineId = data.Wine.WineId;
                        steps.Add(() =>
                        {
                            Log.Info("[BuildManager] apply wine partners wineId=" + targetWineId);
                            GameApi.SetWinePartners(targetWineId, partners);
                        });
                    }
                    else
                    {
                        same++;
                    }
                }
            }
            else if (exact && data.Wine != null && data.Wine.WineId != 0)
            {
                Log.Warn("[BuildManager] preset has no wine partner data; partner items left unchanged " +
                         "(re-save the preset to include them).");
            }

            Dictionary<int, int> wornSpellByType = new Dictionary<int, int>();
            foreach (WornSpell spell in GameApi.GetWornSpells())
            {
                if (spell.Type != 0) wornSpellByType[spell.Type] = spell.Id;
            }

            Dictionary<int, int> presetSpellByType = new Dictionary<int, int>();
            foreach (PresetSpell preset in data.Spells)
            {
                presetSpellByType[preset.Type] = preset.SpellId;
            }

            if (exact && data.Spells.Count > 0)
            {
                for (int type = SpellTypeFirst; type <= SpellTypeLast; type++)
                {
                    int target;
                    presetSpellByType.TryGetValue(type, out target);
                    int current;
                    wornSpellByType.TryGetValue(type, out current);

                    if (target == current)
                    {
                        if (target != 0) same++;
                        continue;
                    }

                    if (target == 0)
                    {
                        if (current != 0)
                        {
                            int clearType = type;
                            steps.Add(() =>
                            {
                                Log.Info("[BuildManager] clear spell type=" + clearType);
                                GameApi.UnsetSpell(clearType);
                            });
                        }
                        continue;
                    }

                    int spellId = target;
                    if (!GameApi.IsSpellLearned(spellId))
                    {
                        missing++;
                        missingDetails.Add("法术 " + GameData.SpellName(spellId));
                        Log.Warn("[BuildManager] missing spell: id=" + spellId +
                                 " type=" + type + ", skipped (spell type unchanged).");
                        continue;
                    }

                    steps.Add(() =>
                    {
                        Log.Info("[BuildManager] apply spell id=" + spellId);
                        GameApi.SetSpell(spellId);
                    });
                }
            }
            else
            {
                if (exact && data.Spells.Count == 0)
                {
                    Log.Warn("[BuildManager] preset has no spell entries; spells left unchanged.");
                }

                HashSet<int> wornSpellIds = new HashSet<int>();
                foreach (int spellId in wornSpellByType.Values)
                {
                    if (spellId != 0) wornSpellIds.Add(spellId);
                }

                foreach (PresetSpell preset in data.Spells)
                {
                    if (preset.SpellId == 0) continue;
                    if (!wornSpellIds.Add(preset.SpellId))
                    {
                        same++;
                        continue;
                    }

                    int spellId = preset.SpellId;
                    if (!GameApi.IsSpellLearned(spellId))
                    {
                        missing++;
                        missingDetails.Add("法术 " + GameData.SpellName(spellId));
                        Log.Warn("[BuildManager] missing spell: id=" + spellId +
                                 ", skipped (spell type unchanged).");
                        continue;
                    }

                    steps.Add(() =>
                    {
                        Log.Info("[BuildManager] apply spell id=" + spellId);
                        GameApi.SetSpell(spellId);
                    });
                }
            }

            Log.Info("[BuildManager] preset " + name + " diff: steps=" + steps.Count +
                     ", same=" + same + ", healed=" + healed + ", missing=" + missing + ", empty=" + empty + ".");

            if (missingDetails.Count > 0)
            {
                MissingNotifier.ShowMissing(missingDetails);
            }

            Sequencer.Run("apply " + name, steps, StepIntervalMs,
                () => DumpService.DumpCurrentBuild("apply_" + name));
        }

        /// <summary>
        /// C2 冒烟：从背包挑一件当前没穿的兵器穿上。
        /// </summary>
        public static void TestWearOneWeapon()
        {
            if (Sequencer.IsRunning)
            {
                Log.Warn("[BuildManager] a task is running; try again later.");
                return;
            }

            if (!GameApi.CanSwitchWear()) return;

            HashSet<ulong> wornUids = new HashSet<ulong>();
            foreach (WornEquip equip in GameApi.GetWornEquips()) wornUids.Add(equip.Uid);

            BagEquip candidate = null;
            foreach (BagEquip equip in GameApi.GetBagEquips())
            {
                if (equip.Uid == 0 || wornUids.Contains(equip.Uid)) continue;
                int slot = GameApi.GetEquipSlotId(equip.Id);
                if (slot != (int)BtlB1.EquipPosition.Weapon) continue;
                candidate = equip;
                break;
            }

            if (candidate == null)
            {
                Log.Warn("[BuildManager] no unworn weapon in bag; C2 wear test skipped.");
                return;
            }

            Log.Info("[BuildManager] C2 wear test target: id=" + candidate.Id + " uid=" + candidate.Uid);
            ulong uid = candidate.Uid;
            Sequencer.Run("C2 wear test", new List<Action> { () => GameApi.WearEquip(uid) }, 700,
                () => DumpService.DumpCurrentBuild("wear_test"));
        }

        /// <summary>
        /// C2 冒烟：卸下当前兵器。用于验证 RequestActorUnWearEquip。
        /// </summary>
        public static void TestUnwearWeapon()
        {
            if (Sequencer.IsRunning)
            {
                Log.Warn("[BuildManager] a task is running; try again later.");
                return;
            }

            if (!GameApi.CanSwitchWear()) return;

            WornEquip weapon = null;
            foreach (WornEquip equip in GameApi.GetWornEquips())
            {
                if (equip.Position == (int)BtlB1.EquipPosition.Weapon)
                {
                    weapon = equip;
                    break;
                }
            }

            if (weapon == null || weapon.Uid == 0)
            {
                Log.Warn("[BuildManager] no weapon worn; C2 unwear test skipped.");
                return;
            }

            Log.Info("[BuildManager] C2 unwear test target: id=" + weapon.Id + " uid=" + weapon.Uid);
            ulong uid = weapon.Uid;
            Sequencer.Run("C2 unwear test", new List<Action> { () => GameApi.UnwearEquip(uid) }, 700,
                () => DumpService.DumpCurrentBuild("unwear_test"));
        }

        private static ulong ResolveBagUid(ulong oldUid, int equipId, List<BagEquip> bagEquips,
                                           out bool healed, out BagEquip resolved)
        {
            healed = false;
            resolved = null;

            if (oldUid != 0)
            {
                resolved = bagEquips.Find(e => e.Uid == oldUid);
                if (resolved != null) return oldUid;
            }

            if (equipId != 0)
            {
                resolved = bagEquips.Find(e => e.Id == equipId);
                if (resolved != null)
                {
                    healed = true;
                    return resolved.Uid;
                }
            }
            return 0;
        }

        private static ulong ResolveSoulUid(ulong oldUid, int soulId, List<BagSoulSkill> bagSouls, out bool healed)
        {
            healed = false;

            if (oldUid != 0 && bagSouls.Exists(s => s.Uid == oldUid)) return oldUid;
            if (soulId != 0)
            {
                BagSoulSkill match = bagSouls.Find(s => s.Id == soulId);
                if (match != null)
                {
                    healed = true;
                    return match.Uid;
                }
            }
            return 0;
        }

        private static int CountNonEmpty(List<PresetEquip> equips)
        {
            int count = 0;
            foreach (PresetEquip equip in equips)
            {
                if (equip.Uid != 0) count++;
            }
            return count;
        }

        private static int CountNonEmptyAccessories(List<PresetAccessory> accessories)
        {
            int count = 0;
            foreach (PresetAccessory accessory in accessories)
            {
                if (accessory.Uid != 0) count++;
            }
            return count;
        }

        private static int CountNonEmptySpells(List<PresetSpell> spells)
        {
            int count = 0;
            foreach (PresetSpell spell in spells)
            {
                if (spell.SpellId != 0) count++;
            }
            return count;
        }

        /// <summary>
        /// 生成最终要提交的泡酒物列表：
        /// - 方案里有、当前存档也有的道具正常还原；
        /// - 方案里有、当前存档没有的道具记为缺失，保留该位置原来的泡酒物；
        /// - 当前酒里已有、方案没记录的位置也保留，避免被协议默认清空。
        /// </summary>
        private static List<WinePartnerSnap> BuildEffectiveWinePartners(
            List<WinePartnerSnap> current,
            List<PresetWinePartner> preset,
            List<BagWine> allWines,
            List<string> missingDetails,
            out int missing)
        {
            missing = 0;

            var byPos = new Dictionary<int, int>();
            if (current != null)
            {
                foreach (WinePartnerSnap partner in current) byPos[partner.Pos] = partner.ItemId;
            }

            foreach (PresetWinePartner partner in preset)
            {
                if (partner.ItemId == 0)
                {
                    byPos[partner.Pos] = 0;
                    continue;
                }

                if (IsWinePartnerAvailable(partner.ItemId, allWines))
                {
                    byPos[partner.Pos] = partner.ItemId;
                    continue;
                }

                missing++;
                missingDetails.Add("泡酒物 " + partner.ItemId);
                Log.Warn("[BuildManager] missing wine partner: pos=" + partner.Pos +
                         " id=" + partner.ItemId + ", skipped (pos unchanged).");
            }

            var result = new List<WinePartnerSnap>();
            foreach (KeyValuePair<int, int> pair in byPos)
            {
                result.Add(new WinePartnerSnap { Pos = pair.Key, ItemId = pair.Value });
            }
            return result;
        }

        /// <summary>
        /// 泡酒物放在酒里后可能不再计入背包数量，所以除了查背包，还要查所有酒当前的泡酒物列表。
        /// 只要当前存档里存在这个道具，就不算“未获得”，具体能否放到目标酒交给游戏协议判断。
        /// </summary>
        private static bool IsWinePartnerAvailable(int itemId, List<BagWine> allWines)
        {
            if (itemId == 0) return true;
            if (GameApi.GetItemCount(itemId) > 0) return true;

            if (allWines != null)
            {
                foreach (BagWine wine in allWines)
                {
                    if (wine.Partners == null) continue;
                    foreach (WinePartnerSnap partner in wine.Partners)
                    {
                        if (partner.ItemId == itemId) return true;
                    }
                }
            }
            return false;
        }

        private static bool WineSnapsEqual(List<WinePartnerSnap> current, List<WinePartnerSnap> target)
        {
            var currentByPos = new Dictionary<int, int>();
            if (current != null)
            {
                foreach (WinePartnerSnap partner in current) currentByPos[partner.Pos] = partner.ItemId;
            }

            var targetByPos = new Dictionary<int, int>();
            if (target != null)
            {
                foreach (WinePartnerSnap partner in target) targetByPos[partner.Pos] = partner.ItemId;
            }

            var positions = new HashSet<int>(currentByPos.Keys);
            positions.UnionWith(targetByPos.Keys);

            foreach (int pos in positions)
            {
                int currentItem;
                currentByPos.TryGetValue(pos, out currentItem);
                int targetItem;
                targetByPos.TryGetValue(pos, out targetItem);
                if (currentItem != targetItem) return false;
            }
            return true;
        }
    }
}
