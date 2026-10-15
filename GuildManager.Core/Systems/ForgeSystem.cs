using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// イグニアの炉（イグニアとの決着の見返り、§0.131）：保管庫の武具のアフィックスを1段（Tier）強くする。
    /// 1回で1つ：段のいちばん低いアフィックス（同じ段なら接頭辞）を、同じ位置・同じ能力の1つ上の段に替える。
    /// 値は上の段の中ほど（前の値より必ず1以上大きい）。焔の鱗と金を使う。乱数は使わない。
    /// </summary>
    public static class ForgeSystem
    {
        /// <summary>この武具で次に鍛えられるアフィックス（今の定義・値と、上の段の定義）。鍛えられなければ null。</summary>
        public static (AffixDefinition From, int Value, AffixDefinition To)? NextUpgrade(EquipmentItem item)
        {
            if (item.IsUnique) return null;
            var slots = new[] { (Id: item.PrefixId, Value: item.PrefixValue), (Id: item.SuffixId, Value: item.SuffixValue) };
            return slots
                .Select(s => AffixBalance.FindById(s.Id) is { } from ? (From: from, s.Value) : default)
                .Where(x => x.From != null)
                .Select(x => (x.From, x.Value, To: AffixBalance.All.FirstOrDefault(a => a.Type == x.From.Type && a.TargetStat == x.From.TargetStat && a.Tier == x.From.Tier + 1)))
                .Where(x => x.To != null)
                .OrderBy(x => x.From.Tier).ThenBy(x => x.From.Type)
                .Select(x => ((AffixDefinition From, int Value, AffixDefinition To)?)(x.From, x.Value, x.To!))
                .FirstOrDefault();
        }

        /// <summary>鍛えたあとの値：上の段の中ほど（前の値より1以上大きい）。</summary>
        public static int UpgradedValue(int oldValue, AffixDefinition to) => Math.Max(oldValue + 1, (to.MinValue + to.MaxValue + 1) / 2);

        /// <summary>鍛えられない理由（鍛えられるなら null）。</summary>
        public static string? BlockReason(GameState state, EquipmentItem item)
        {
            if (!RulerSystem.IsForgeOpen(state)) return "イグニアの炉がまだ無い";
            if (!state.Armory.Contains(item)) return "保管庫の武具だけ鍛えられる";
            if (NextUpgrade(item) == null) return item.HasAffix ? "アフィックスはもういちばん上の段" : "アフィックスが無い";
            if (state.Materials.GetValueOrDefault(RulerSystem.ScaleMaterialId) < RulerBalance.IgniaForgeScaleCost)
                return $"焔の鱗が{RulerBalance.IgniaForgeScaleCost}枚要る";
            if (state.Gold < RulerBalance.IgniaForgeGoldCost) return $"{RulerBalance.IgniaForgeGoldCost}G 要る";
            return null;
        }

        /// <summary>鍛える（焔の鱗と金を使う）。鍛えられなければ false で何もしない。</summary>
        public static bool TryForge(GameState state, EquipmentItem item)
        {
            if (BlockReason(state, item) != null || NextUpgrade(item) is not { } up) return false;
            state.Materials[RulerSystem.ScaleMaterialId] -= RulerBalance.IgniaForgeScaleCost;
            state.Gold -= RulerBalance.IgniaForgeGoldCost;

            int value = UpgradedValue(up.Value, up.To);
            if (up.From.IsHpBonus) item.AffixHpBonus += value - up.Value;
            else item.AffixStatBonuses[up.From.TargetStat] = item.GetAffixStatBonus(up.From.TargetStat) + value - up.Value;
            if (up.From.Type == AffixType.Prefix) { item.PrefixId = up.To.Id; item.PrefixValue = value; }
            else { item.SuffixId = up.To.Id; item.SuffixValue = value; }
            return true;
        }
    }
}
