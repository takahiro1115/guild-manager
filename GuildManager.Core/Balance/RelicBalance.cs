using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 未鑑定遺物（レリック）と鑑定システム（→ 03 §4.7、Systems.AppraisalSystem）のバランス値。
    /// 値は docs/04_バランス表/relic.csv（key,value,unit,note 形式）から読み込む
    /// （→ 03 §10.1、BalanceDataの方針にならいフォールバックは持たない）。
    ///
    /// 「出土フィールド・深度に応じた抽選テーブル」は本ファイルに重複定義しない：
    ///  - 素材が出た場合の抽選対象は、遺物が持つ出土地・出土階層をそのまま
    ///    <see cref="MaterialBalance.GetEligibleMaterials"/> へ渡して引く（materials.csv が正本）。
    ///  - 武具が出た場合の抽選対象は、希少度ごとの EquipmentPool*（ItemCatalogのId列）。
    /// こうすることで、フィールドや素材を増やしたときに直すファイルが materials.csv 1つで済む
    /// （→ 03 §10.1「値の二重管理を避ける」）。
    ///
    /// 読み込み時に以下のフォーマット違反を検出し、例外（BalanceDataException）で起動失敗にする：
    ///  - 武具率＋素材率＋換金率が100にならない希少度がある
    ///  - EquipmentPool* に ItemCatalog に存在しないアイテムIdが含まれている／プールが空
    ///  - Min が Max を超えている獲得ゴールド・獲得個数の範囲
    ///  - BossMinimumRarity が ItemRarity として解釈できない
    /// </summary>
    public static class RelicBalance
    {
        private const string FileName = "relic.csv";

        /// <summary>希少度ごとの定義一式（→ relic.csv の *Common / *Rare / *Epic / *Legendary）。</summary>
        public class RarityProfile
        {
            public ItemRarity Rarity { get; init; }

            /// <summary>表示上の通称（銅・銀・金・虹）。</summary>
            public string Label { get; init; } = "";

            /// <summary>UI（Godot）でのBBCode色名。バランス値ではないためCSVには出さない。</summary>
            public string ColorName { get; init; } = "";

            public int AppraisalCost { get; init; }

            // 武具率＋素材率＋換金率＝100（読み込み時に検証する）。
            public int EquipmentRate { get; init; }
            public int MaterialRate { get; init; }
            public int GoldRate { get; init; }

            /// <summary>
            /// この希少度の鑑定で出土した武具を、ギルド保管庫から売却する際の1点あたりの額
            /// （→ 03 §4.8、Systems.EquipmentSystem.GetSellPrice）。カタログ品（定価の50%）とは
            /// 別系統の値で、希少度だけで決まる。
            /// </summary>
            public int SellPrice { get; init; }

            /// <summary>
            /// 鑑定で武具が出た場合に、まだ入手していない固定アーティファクト（紫、→ UniqueBalance・uniques.csv）へ
            /// 化ける確率（%、0〜100）。0なら判定の乱数も引かない（→ AppraisalSystem、03 §4.7.3、2026年9月・§0.45）。
            /// </summary>
            public int ArtifactRate { get; init; }

            public int GoldRewardMin { get; init; }
            public int GoldRewardMax { get; init; }
            public int MaterialCountMin { get; init; }
            public int MaterialCountMax { get; init; }

            /// <summary>武具が出た場合の抽選プール（ItemCatalogのId）。空にはならない（読み込み時に検証）。</summary>
            public IReadOnlyList<string> EquipmentPool { get; init; } = Array.Empty<string>();
        }

        private static readonly Dictionary<ItemRarity, RarityProfile> Profiles = BuildProfiles();

        // ---- 探索（採取）任務でのドロップ判定（→ Systems.GatheringResolver） ----

        /// <summary>採取でのドロップ基礎確率（%）。</summary>
        public static readonly int GatheringDropBasePct = BalanceData.GetInt(FileName, "GatheringDropBasePct");

        /// <summary>採取でのドロップ確率の上限（%）。</summary>
        public static readonly int GatheringDropMaxPct = BalanceData.GetInt(FileName, "GatheringDropMaxPct");

        /// <summary>採取スコア÷この値をドロップ確率へ加算する（DEX/AGI/隊長LDRが効く経路）。</summary>
        public static readonly double GatheringDropScoreDivisor = BalanceData.GetDouble(FileName, "GatheringDropScoreDivisor");

        /// <summary>深さの段×この値をドロップ確率へ加算する（§0.126。以前の「最高到達階層÷20」を置き換えた）。</summary>
        public static readonly int GatheringDropPerDepthPct = BalanceData.GetInt(FileName, "GatheringDropPerDepthPct");

        // ---- 希少度（採取・ボス撃破・依頼で共通、→ Systems.AppraisalSystem.CreateRelic。§0.126） ----

        /// <summary>希少度の帯の境目（深さの段がこの値以下なら、その帯。4つ。最後の値より深ければ帯5）。</summary>
        public static readonly IReadOnlyList<int> RarityDepthBandMax = ParseBandMax(BalanceData.GetString(FileName, "RarityDepthBandMax"));

        /// <summary>帯ごとの希少度の割合（%。銅・銀・金・虹の順で合計100）。添字0＝帯1。</summary>
        private static readonly IReadOnlyList<IReadOnlyList<int>> RarityWeightsByBand =
            Enumerable.Range(1, RarityDepthBandMax.Count + 1).Select(b => ParseWeights($"RarityWeightsBand{b}")).ToList();

        // ---- 階層ボス撃破ドロップ（→ Systems.DungeonExpeditionSystem） ----

        /// <summary>階層ボス撃破のドロップは、出土階層の帯よりこの数だけ深い帯の割合で引く（§0.126）。</summary>
        public static readonly int BossRarityBandBonus = BalanceData.GetInt(FileName, "BossRarityBandBonus");

        /// <summary>階層ボス撃破で2つ目の遺物が出ることがある深さの段の下限（§0.126）。</summary>
        public static readonly int BossExtraRelicMinDepth = BalanceData.GetInt(FileName, "BossExtraRelicMinDepth");

        /// <summary>2つ目の遺物が出る確率＝深さの段×この値（%、§0.126）。</summary>
        public static readonly int BossExtraRelicPctPerDepth = BalanceData.GetInt(FileName, "BossExtraRelicPctPerDepth");

        /// <summary>階層ボス撃破ドロップの最低保証希少度。</summary>
        public static readonly ItemRarity BossMinimumRarity = ParseRarity(BalanceData.GetString(FileName, "BossMinimumRarity"));

        private static IReadOnlyList<int> ParseBandMax(string raw)
        {
            var values = raw.Split(';').Select(s => int.Parse(s.Trim())).ToList();
            for (int i = 1; i < values.Count; i++)
                if (values[i] <= values[i - 1])
                    throw new BalanceDataException($"{FileName} の RarityDepthBandMax（{raw}）は小さい順に並べてください。");
            return values;
        }

        private static IReadOnlyList<int> ParseWeights(string key)
        {
            string raw = BalanceData.GetString(FileName, key);
            var values = raw.Split(';').Select(s => int.Parse(s.Trim())).ToList();
            if (values.Count != 4 || values.Any(v => v < 0) || values.Sum() != 100)
                throw new BalanceDataException($"{FileName} の {key}（{raw}）は「銅;銀;金;虹」の4つの0以上の数で、合計100にしてください。");
            return values;
        }

        private static Dictionary<ItemRarity, RarityProfile> BuildProfiles() => new()
        {
            [ItemRarity.Common] = BuildProfile(ItemRarity.Common, "Common", "銅", "sienna"),
            [ItemRarity.Rare] = BuildProfile(ItemRarity.Rare, "Rare", "銀", "silver"),
            [ItemRarity.Epic] = BuildProfile(ItemRarity.Epic, "Epic", "金", "gold"),
            [ItemRarity.Legendary] = BuildProfile(ItemRarity.Legendary, "Legendary", "虹", "magenta"),
        };

        private static RarityProfile BuildProfile(ItemRarity rarity, string suffix, string label, string colorName)
        {
            int equipmentRate = BalanceData.GetInt(FileName, "EquipmentRate" + suffix);
            int materialRate = BalanceData.GetInt(FileName, "MaterialRate" + suffix);
            int goldRate = BalanceData.GetInt(FileName, "GoldRate" + suffix);
            if (equipmentRate + materialRate + goldRate != 100)
            {
                throw new BalanceDataException(
                    $"{FileName} の{label}（{suffix}）の鑑定結果比率の合計が100になりません" +
                    $"（武具{equipmentRate}＋素材{materialRate}＋換金{goldRate}＝{equipmentRate + materialRate + goldRate}）。");
            }

            int goldMin = BalanceData.GetInt(FileName, "GoldRewardMin" + suffix);
            int goldMax = BalanceData.GetInt(FileName, "GoldRewardMax" + suffix);
            if (goldMin > goldMax)
                throw new BalanceDataException($"{FileName} の GoldRewardMin{suffix}（{goldMin}）が GoldRewardMax{suffix}（{goldMax}）を超えています。");

            int countMin = BalanceData.GetInt(FileName, "MaterialCountMin" + suffix);
            int countMax = BalanceData.GetInt(FileName, "MaterialCountMax" + suffix);
            if (countMin > countMax)
                throw new BalanceDataException($"{FileName} の MaterialCountMin{suffix}（{countMin}）が MaterialCountMax{suffix}（{countMax}）を超えています。");

            int artifactRate = BalanceData.GetInt(FileName, "ArtifactRate" + suffix);
            if (artifactRate is < 0 or > 100)
                throw new BalanceDataException($"{FileName} の ArtifactRate{suffix}（{artifactRate}）は0〜100にしてください。");

            return new RarityProfile
            {
                Rarity = rarity,
                Label = label,
                ColorName = colorName,
                AppraisalCost = BalanceData.GetInt(FileName, "AppraisalCost" + suffix),
                SellPrice = BalanceData.GetInt(FileName, "SellPrice" + suffix),
                ArtifactRate = artifactRate,
                EquipmentRate = equipmentRate,
                MaterialRate = materialRate,
                GoldRate = goldRate,
                GoldRewardMin = goldMin,
                GoldRewardMax = goldMax,
                MaterialCountMin = countMin,
                MaterialCountMax = countMax,
                EquipmentPool = ParseEquipmentPool(BalanceData.GetString(FileName, "EquipmentPool" + suffix), suffix),
            };
        }

        /// <summary>「IronSword;LeatherArmor」形式をItemIdの一覧へ変換する（カタログに無いIdは例外）。</summary>
        private static IReadOnlyList<string> ParseEquipmentPool(string raw, string suffix)
        {
            var ids = raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .ToList();

            if (ids.Count == 0)
                throw new BalanceDataException($"{FileName} の EquipmentPool{suffix} が空です（武具の抽選プールは最低1件必要）。");

            foreach (var id in ids)
            {
                if (ItemCatalog.FindById(id) == null)
                    throw new BalanceDataException($"{FileName} の EquipmentPool{suffix} のアイテムId「{id}」はItemCatalogに存在しません。");
            }

            return ids;
        }

        private static ItemRarity ParseRarity(string raw)
        {
            if (Enum.TryParse<ItemRarity>(raw, out var rarity))
                return rarity;
            throw new BalanceDataException($"{FileName} の BossMinimumRarity「{raw}」をItemRarityとして解釈できません。");
        }

        /// <summary>希少度ごとの定義一式。</summary>
        public static RarityProfile Get(ItemRarity rarity) => Profiles[rarity];

        /// <summary>全希少度の定義（Common→Legendaryの順）。UIの凡例表示用。</summary>
        public static IReadOnlyList<RarityProfile> GetAll() =>
            Profiles.Values.OrderBy(p => p.Rarity).ToList();

        /// <summary>鑑定費用（→ UnidentifiedItem.AppraisalCost）。</summary>
        public static int GetAppraisalCost(ItemRarity rarity) => Profiles[rarity].AppraisalCost;

        /// <summary>鑑定で出土した武具1点の売却額（→ Systems.EquipmentSystem.GetSellPrice、03 §4.8）。</summary>
        public static int GetSellPrice(ItemRarity rarity) => Profiles[rarity].SellPrice;

        // ---- アフィックスによる売却加算（→ Systems.EquipmentSystem.GetSellPrice、03 §4.8.2、2026年9月・§0.40） ----

        /// <summary>Tier（1〜3）→ そのTierのアフィックス1枠あたりの売却加算額（G）。キーは AffixSellBonusTier{n}。</summary>
        private static readonly Dictionary<int, int> AffixSellBonuses = BuildAffixSellBonuses();

        private static Dictionary<int, int> BuildAffixSellBonuses()
        {
            var result = new Dictionary<int, int>();
            for (int tier = AffixBalance.MinTier; tier <= AffixBalance.MaxTier; tier++)
            {
                string key = $"AffixSellBonusTier{tier}";
                int bonus = BalanceData.GetInt(FileName, key);
                if (bonus < 0)
                    throw new BalanceDataException($"{FileName} の {key}（{bonus}）は0以上にしてください。");
                result[tier] = bonus;
            }
            return result;
        }

        /// <summary>
        /// 指定Tierのアフィックス1枠あたりの売却加算額（→ relic.csv の AffixSellBonusTier*）。
        /// Tierが1〜3の外なら ArgumentOutOfRangeException（affixes.csv の読み込み時に1〜3へ限定済みのため、通常は起きない）。
        /// </summary>
        public static int GetAffixSellBonus(int tier) =>
            AffixSellBonuses.TryGetValue(tier, out int bonus)
                ? bonus
                : throw new ArgumentOutOfRangeException(nameof(tier), tier, "アフィックスのTierは1〜3");

        /// <summary>表示上の通称（銅・銀・金・虹）。</summary>
        public static string GetRarityLabel(ItemRarity rarity) => Profiles[rarity].Label;

        /// <summary>UI（BBCode）での色名。</summary>
        public static string GetRarityColorName(ItemRarity rarity) => Profiles[rarity].ColorName;

        /// <summary>
        /// 深さの段（§0.126）＝出土階層÷10（切り捨て）＋(フィールドの順−1)、最低1。主の体力の「段」と同じ数え方
        /// （森の10F＝1・森の100F＝10・深淵の100F＝14）。
        /// </summary>
        public static int DepthStep(int fieldOrder, int floor) => Math.Max(1, floor / 10 + (Math.Max(1, fieldOrder) - 1));

        /// <summary>深さの段が入る希少度の帯（1〜5、→ RarityDepthBandMax）。</summary>
        public static int DepthBand(int depthStep)
        {
            for (int i = 0; i < RarityDepthBandMax.Count; i++)
                if (depthStep <= RarityDepthBandMax[i]) return i + 1;
            return RarityDepthBandMax.Count + 1;
        }

        /// <summary>帯ごとの希少度の割合（%。銅・銀・金・虹の順）。帯は1〜5に収める。</summary>
        public static IReadOnlyList<int> RarityWeights(int band) =>
            RarityWeightsByBand[Math.Clamp(band, 1, RarityWeightsByBand.Count) - 1];

        /// <summary>
        /// 希少度ロール（§0.126）：深さの段の帯（＋bandBonus。ボス撃破・依頼の報酬は1つ深い帯）の割合で、
        /// 1〜100の乱数から希少度を決める（→ Systems.AppraisalSystem.CreateRelic）。浅い帯では金・虹が出ない。
        /// 以前は「乱数＋出土階層÷10＋上乗せ」を閾値と比べていて、深さの効きが弱く、浅い主からも金・虹がよく出た。
        /// </summary>
        public static ItemRarity RollRarity(int roll1To100, int depthStep, int bandBonus)
        {
            var weights = RarityWeights(DepthBand(depthStep) + bandBonus);
            int cumulative = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (roll1To100 <= cumulative) return (ItemRarity)i;
            }
            return ItemRarity.Legendary;
        }

        /// <summary>
        /// 探索（採取）任務での未鑑定遺物ドロップ確率（%）。基礎確率に採取スコアと深さの段（§0.126）の上乗せを
        /// 乗せ、上限（GatheringDropMaxPct）でクランプする（→ Systems.GatheringResolver）。
        /// </summary>
        public static int GetGatheringDropPercent(double gatheringScore, int depthStep)
        {
            int pct = GatheringDropBasePct
                + (int)(gatheringScore / GatheringDropScoreDivisor)
                + depthStep * GatheringDropPerDepthPct;

            return Math.Clamp(pct, GatheringDropBasePct, GatheringDropMaxPct);
        }

        /// <summary>階層ボス撃破で2つ目の遺物が出る確率（%、§0.126）。深さの段が BossExtraRelicMinDepth 未満なら0。</summary>
        public static int BossExtraRelicPercent(int depthStep) =>
            depthStep < BossExtraRelicMinDepth ? 0 : Math.Min(100, depthStep * BossExtraRelicPctPerDepth);
    }
}
