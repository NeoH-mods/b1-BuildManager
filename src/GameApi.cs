using System;
using System.Collections.Generic;
using CSharpModBase;

namespace BuildManager
{
    internal sealed class WornEquip
    {
        public int Position;
        public int Id;
        public ulong Uid;
    }

    internal sealed class WornAccessory
    {
        public int Pos;
        public int Id;
        public ulong Uid;
    }

    internal sealed class WornSoulSkill
    {
        public int Id;
        public ulong Uid;
    }

    internal sealed class WornSpell
    {
        public int Type;
        public int Id;
    }

    internal sealed class BagEquip
    {
        public int Id;
        public ulong Uid;
    }

    internal sealed class BagSoulSkill
    {
        public int Id;
        public ulong Uid;
    }

    internal sealed class BagWine
    {
        public int WineId;
        public ulong Uid;
        public List<WinePartnerSnap> Partners = new List<WinePartnerSnap>();
    }

    internal sealed class WinePartnerSnap
    {
        public int Pos;
        public int ItemId;
    }

    internal sealed class AttrEntry
    {
        public int Type;
        public float Value;
    }

    /// <summary>
    /// 游戏侧只读/写接口封装。所有调用都要求已经在游戏内（GamePlayer != null）。
    /// 写入走游戏自己的 UI 工具链（GSB1UIUtil），与玩家手动换装同一路径。
    ///
    /// 注意：本类的字段/方法签名刻意不出现游戏类型（b1.GamePlayer 等只作为局部变量），
    /// 这样 mod 在游戏托管程序集尚未加载时也能通过 Assembly.GetTypes() 的元数据解析，
    /// 游戏类型只在热键真正触发、方法被 JIT 时才解析。
    /// </summary>
    internal static class GameApi
    {
        public static bool RequirePlayer(string action)
        {
            b1.GamePlayer player = B1UI.GSG.GamePlayer;
            if (player == null)
            {
                Log.Warn("[BuildManager] " + action + ": not in game (GamePlayer is null), skipped.");
                return false;
            }

            try
            {
                if (!player.IsLocalPlayer())
                {
                    Log.Warn("[BuildManager] " + action + ": not local player, skipped.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] " + action + ": IsLocalPlayer failed: " + ex.Message);
                return false;
            }

            return true;
        }

        public static bool CanSwitchWear()
        {
            if (!RequirePlayer("switch-wear check")) return false;

            b1.GamePlayer player = B1UI.GSG.GamePlayer;
            // 游戏自己的 SwitchWearEquip/SwitchWearAccessory 只调用 CheckCanSwitchWear
            //（BossRush/BossRoom/传送中），并不检查 GSG.IsInBattle。这里照游戏原逻辑放行；
            // 该字段在土地庙调息后仍可能为 true，用它拦截会把合法场景误判成战斗。
            try
            {
                if (!B1UI.GSUI.GSB1UIUtil.CheckCanSwitchWear(player.WorldContext))
                {
                    Log.Warn("[BuildManager] game says equipment switch is not allowed here; aborted.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] CheckCanSwitchWear threw, continuing anyway: " + ex.Message);
            }

            return true;
        }

        // ---------------- 读 ----------------

        public static List<WornEquip> GetWornEquips()
        {
            var result = new List<WornEquip>();
            try
            {
                if (!RequirePlayer("read worn equips")) return result;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.Actor?.Wear?.EquipList?.ValueList;
                if (list == null) return result;

                foreach (CommB1.DSWearEquip item in list)
                {
                    if (item == null) continue;
                    result.Add(new WornEquip
                    {
                        Position = (int)item.Position,
                        Id = item.Id,
                        Uid = item.Uid,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read worn equips failed: " + ex);
            }
            return result;
        }

        public static List<WornAccessory> GetWornAccessories()
        {
            var result = new List<WornAccessory>();
            try
            {
                if (!RequirePlayer("read curios")) return result;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.Actor?.Wear?.WearAccessory?.Accessorylist?.ValueList;
                if (list == null) return result;

                foreach (CommB1.DSAccessoryProp item in list)
                {
                    if (item == null) continue;
                    result.Add(new WornAccessory
                    {
                        Pos = item.Pos,
                        Id = item.Id,
                        Uid = item.Uid,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read curios failed: " + ex);
            }
            return result;
        }

        public static WornSoulSkill GetWornSoulSkill()
        {
            try
            {
                if (!RequirePlayer("read soul skill")) return new WornSoulSkill();
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                CommB1.DSWearSoulSkill soul = player.Actor?.Wear?.WearSoulSkill;
                if (soul == null) return new WornSoulSkill();
                return new WornSoulSkill { Id = soul.SoulSkillId, Uid = soul.Uid };
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read soul skill failed: " + ex);
                return new WornSoulSkill();
            }
        }

        public static int GetCurrentWineId()
        {
            try
            {
                if (!RequirePlayer("read wine")) return 0;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                return player.Actor?.Wear?.WineId ?? 0;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read wine failed: " + ex);
                return 0;
            }
        }

        public static List<WornSpell> GetWornSpells()
        {
            var result = new List<WornSpell>();
            try
            {
                if (!RequirePlayer("read spells")) return result;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.Actor?.Wear?.SpellList?.ValueList;
                if (list == null) return result;

                foreach (CommB1.DSSpellItem item in list)
                {
                    if (item == null) continue;
                    result.Add(new WornSpell { Type = (int)item.Type, Id = item.SpellId });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read spells failed: " + ex);
            }
            return result;
        }

        public static List<BagEquip> GetBagEquips()
        {
            var result = new List<BagEquip>();
            try
            {
                if (!RequirePlayer("read bag equips")) return result;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.Bag?.EquipList?.ValueList;
                if (list == null) return result;

                foreach (CommB1.DSRoleEquip item in list)
                {
                    if (item == null) continue;
                    result.Add(new BagEquip { Id = item.EquipId, Uid = item.Uid });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read bag equips failed: " + ex);
            }
            return result;
        }

        public static List<BagSoulSkill> GetBagSoulSkills()
        {
            var result = new List<BagSoulSkill>();
            try
            {
                if (!RequirePlayer("read bag soul skills")) return result;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.Bag?.SoulSkillList?.ValueList;
                if (list == null) return result;

                foreach (CommB1.DSRoleSoulSkill item in list)
                {
                    if (item == null) continue;
                    result.Add(new BagSoulSkill { Id = item.SoulSkillId, Uid = item.Uid });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read bag soul skills failed: " + ex);
            }
            return result;
        }

        public static List<BagWine> GetBagWines()
        {
            var result = new List<BagWine>();
            try
            {
                if (!RequirePlayer("read bag wines")) return result;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.Bag?.WineList?.ValueList;
                if (list == null) return result;

                foreach (CommB1.DSRoleWine item in list)
                {
                    if (item == null) continue;

                    var partners = new List<WinePartnerSnap>();
                    var partnerList = item.ItemList?.ValueList;
                    if (partnerList != null)
                    {
                        foreach (CommB1.DSWinePartner partner in partnerList)
                        {
                            if (partner == null) continue;
                            partners.Add(new WinePartnerSnap { Pos = partner.Pos, ItemId = partner.ItemId });
                        }
                    }

                    result.Add(new BagWine { WineId = item.WineId, Uid = item.Uid, Partners = partners });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] read bag wines failed: " + ex);
            }
            return result;
        }

        /// <summary>当前存档是否已经学会该法术（用于应用配装前预判，避免发无效请求）。</summary>
        public static bool IsSpellLearned(int spellId)
        {
            if (spellId == 0) return false;

            try
            {
                if (!RequirePlayer("check spell learned")) return false;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                CommB1.PlayerActor actor = player?.ActorHelper;
                return actor != null && actor.IsSpellExist(spellId);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] check spell learned " + spellId + " failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>当前存档拥有的道具数量（装备/法术/精魄/消耗品由游戏自己的分类逻辑处理）。</summary>
        public static int GetItemCount(int itemId)
        {
            if (itemId == 0) return 0;

            try
            {
                if (!RequirePlayer("check item count")) return 0;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                CommB1.PlayerBag bag = player?.BagHelper;
                return bag == null ? 0 : bag.GetBagItemNum(itemId);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] check item count " + itemId + " failed: " + ex.Message);
                return 0;
            }
        }

        /// <summary>装备 ID → EquipPosition（游戏 DB 优先，静态表兜底）。</summary>
        public static int GetEquipSlotId(int equipId)
        {
            if (equipId == 0) return -1;
            try
            {
                return (int)b1.GameDBRuntime.GetEquipPosition(equipId);
            }
            catch
            {
                return GameData.EquipSlot(equipId);
            }
        }

        /// <summary>导出 AttrMgr.AttrList 全量条目，用于确认属性读数通道。</summary>
        public static List<AttrEntry> GetEffectAttrs()
        {
            var result = new List<AttrEntry>();
            try
            {
                if (!RequirePlayer("read effect attr list")) return result;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.AttrMgr?.AttrList?.Attrs?.ValueList;
                if (list == null) return result;

                foreach (CommB1.DSEffectAttrFloat item in list)
                {
                    if (item == null) continue;
                    result.Add(new AttrEntry { Type = (int)item.Type, Value = item.Value });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] read effect attr list failed: " + ex.Message);
            }
            return result;
        }

        /// <summary>从 AttrList（EffectAttrFloat）读取最终属性值。找不到返回 float.NaN。</summary>
        public static float GetEffectAttrFloat(int attrId)
        {
            try
            {
                if (!RequirePlayer("read effect attrs")) return float.NaN;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var list = player.AttrMgr?.AttrList?.Attrs?.ValueList;
                if (list == null) return float.NaN;

                BtlShare.EBGUAttrFloat target = (BtlShare.EBGUAttrFloat)attrId;
                foreach (CommB1.DSEffectAttrFloat item in list)
                {
                    if (item != null && item.Type == target)
                    {
                        return item.Value;
                    }
                }
                return float.NaN;
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] read effect attr " + attrId + " failed: " + ex.Message);
                return float.NaN;
            }
        }

        /// <summary>AttrId: 53=AtkMul, 54=DefMul（沿用 Lua 版结论）。失败返回 int.MinValue。</summary>
        public static int GetDynamicAttr(int attrId)
        {
            try
            {
                if (!RequirePlayer("read attrs")) return int.MinValue;
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                if (player.AttrMgr == null) return int.MinValue;
                return player.AttrMgr.GetDynamicAttrValue((BtlShare.EBGUAttrFloat)attrId);
            }
            catch (Exception ex)
            {
                Log.Warn("[BuildManager] read attr " + attrId + " failed: " + ex.Message);
                return int.MinValue;
            }
        }

        // ---------------- 写 ----------------

        public static bool WearEquip(ulong uid)
        {
            if (!RequirePlayer("wear equip")) return false;
            if (uid == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                B1UI.GSUI.GSB1UIUtil.SwitchWearEquip(player.WorldContext, uid,
                    (code, req, res) => LogRpcResult("穿戴 uid=" + uid, (int)code));
                Log.Info("[BuildManager] wear request sent uid=" + uid);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] wear uid=" + uid + " failed: " + ex);
                return false;
            }
        }

        public static bool UnwearEquip(ulong uid)
        {
            if (!CanSwitchWear()) return false;
            if (uid == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var req = new CsB1.CSMsgActorUnWearEquipReq { EquipUid = uid };
                CsB1.CSRpc.RequestActorUnWearEquip(player, req,
                    (code, rq, rs) => LogRpcResult("卸下 uid=" + uid, (int)code), true);
                Log.Info("[BuildManager] unwear request sent uid=" + uid);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] unwear uid=" + uid + " failed: " + ex);
                return false;
            }
        }

        public static bool WearAccessory(int pos, int itemId, ulong uid)
        {
            if (!RequirePlayer("wear curio")) return false;
            if (uid == 0 || itemId == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                B1UI.GSUI.GSB1UIUtil.SwitchWearAccessory(player.WorldContext, uid, itemId, pos);
                Log.Info("[BuildManager] curio wear request sent pos=" + pos + " uid=" + uid + " id=" + itemId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] curio wear failed pos=" + pos + " uid=" + uid + ": " + ex);
                return false;
            }
        }

        public static bool UnwearAccessory(int pos, int itemId, ulong uid)
        {
            if (!CanSwitchWear()) return false;
            if (uid == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                B1UI.GSUI.GSB1UIUtil.UnWearAccessory(player.WorldContext, uid, itemId, pos,
                    (code, req, res) => LogRpcResult("卸下珍玩 pos=" + pos + " uid=" + uid, (int)code));
                Log.Info("[BuildManager] curio unwear request sent pos=" + pos + " uid=" + uid);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] curio unwear failed pos=" + pos + " uid=" + uid + ": " + ex);
                return false;
            }
        }

        public static bool WearSoulSkill(ulong uid)
        {
            if (!RequirePlayer("switch soul skill")) return false;
            if (uid == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                if (player.BagHelper != null && !player.BagHelper.HaveSoulBottle())
                {
                    Log.Warn("[BuildManager] no soul bottle yet; cannot switch soul skill uid=" + uid + ".");
                    return false;
                }
                B1UI.GSUI.GSB1UIUtil.WearSoulSkill(player.WorldContext, uid);
                Log.Info("[BuildManager] soul skill switch request sent uid=" + uid);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] soul skill switch failed uid=" + uid + ": " + ex);
                return false;
            }
        }

        public static bool SetWine(int wineId)
        {
            if (!RequirePlayer("switch wine")) return false;
            if (wineId == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var req = new CsB1.CSMsgActorWineSettingReq { WineId = wineId };
                CsB1.CSRpc.RequestActorWineSetting(player, req,
                    (code, rq, rs) => LogRpcResult("switch wine wineId=" + wineId, (int)code), true);
                Log.Info("[BuildManager] wine switch request sent wineId=" + wineId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] wine switch failed wineId=" + wineId + ": " + ex);
                return false;
            }
        }

        /// <summary>设置某瓶酒的泡酒物（Pos/ItemId；ItemId=0 表示该位清空）。</summary>
        public static bool SetWinePartners(int wineId, List<WinePartnerSnap> partners)
        {
            if (!RequirePlayer("set wine partners")) return false;
            if (wineId == 0 || partners == null) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var wine = new ArchiveB1.RoleWine { WineId = wineId };
                foreach (WinePartnerSnap partner in partners)
                {
                    if (partner == null) continue;
                    wine.ItemList.Add(new ArchiveB1.WinePartner { Pos = partner.Pos, ItemId = partner.ItemId });
                }

                var req = new CsB1.CSMsgBagWinePartnerSettingReq { WineSetting = wine };
                CsB1.CSRpc.RequestBagWinePartnerSetting(player, req,
                    (code, rq, rs) => LogRpcResult("set wine partners wineId=" + wineId, (int)code), true);
                Log.Info("[BuildManager] wine partner request sent wineId=" + wineId + " count=" + partners.Count);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] wine partner set failed wineId=" + wineId + ": " + ex);
                return false;
            }
        }

        public static bool SetSpell(int spellId)
        {
            if (!RequirePlayer("equip spell")) return false;
            if (spellId == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var req = new CsB1.CSMsgActorSetSpellReq { SpellId = spellId };
                CsB1.CSRpc.RequestActorSetSpell(player, req,
                    (code, rq, rs) => LogRpcResult("equip spell spellId=" + spellId, (int)code), true);
                Log.Info("[BuildManager] spell equip request sent spellId=" + spellId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] spell equip failed spellId=" + spellId + ": " + ex);
                return false;
            }
        }

        /// <summary>按 SpellType 卸下法术（空槽还原）。</summary>
        public static bool UnsetSpell(int spellType)
        {
            if (!RequirePlayer("unset spell")) return false;
            if (spellType == 0) return false;

            try
            {
                b1.GamePlayer player = B1UI.GSG.GamePlayer;
                var req = new CsB1.CSMsgActorUnSetSpellReq { SpellType = (BtlB1.SpellType)spellType };
                CsB1.CSRpc.RequestActorUnSetSpell(player, req,
                    (code, rq, rs) => LogRpcResult("unset spell type=" + spellType, (int)code), true);
                Log.Info("[BuildManager] spell unset request sent type=" + spellType);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BuildManager] spell unset failed type=" + spellType + ": " + ex);
                return false;
            }
        }

        private static void LogRpcResult(string action, int code)
        {
            if (code == 0) Log.Info("[BuildManager] " + action + " ok");
            else Log.Warn("[BuildManager] " + action + " returned error code " + code);
        }
    }
}
