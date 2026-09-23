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

        /// <summary>フィールドの最高到達階層÷この値をドロップ確率へ加算する。</summary>
        public static readonly double GatheringDropFloorDivisor = BalanceData.GetDouble(FileName, "GatheringDropFloorDivisor");

        // ---- 希少度ロール（採取・ボス撃破で共通、→ Systems.AppraisalSystem.CreateRelic） ----

        /// <summary>希少度ロールへの深度ボーナス＝出土階層÷この値。</summary>
        public static readonly double RarityFloorBonusDivisor = BalanceData.GetDouble(FileName, "RarityFloorBonusDivisor");

        public static readonly int RareRollThreshold = BalanceData.GetInt(FileName, "RareRollThreshold");
        public static readonly int EpicRollThreshold = BalanceData.GetInt(FileName, "EpicRollThreshold");
        public static readonly int LegendaryRollThreshold = BalanceData.GetInt(FileName, "LegendaryRollThreshold");

        // ---- 階層ボス撃破ドロップ（→ Systems.DungeonExpeditionSystem） ----

        /// <summary>階層ボス撃破ドロップの希少度ロールへ加算するボーナス。</summary>
        public static readonly int BossRelicRollBonus = BalanceData.GetInt(FileName, "BossRelicRollBonus");

        /// <summary>階層ボス撃破ドロップの最低保証希少度。</summary>
        public static readonly ItemRarity BossMinimumRarity = ParseRarity(BalanceData.GetString(FileName, "BossMinimumRarity"));

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

            return new RarityProfile
            {
                Rarity = rarity,
                Label = label,
                ColorName = colorName,
                AppraisalCost = BalanceData.GetInt(FileName, "AppraisalCost" + suffix),
                SellPrice = BalanceData.GetInt(FileName, "SellPrice" + suffix),
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

        /// <summary>表示上の通称（銅・銀・金・虹）。</summary>
        public static string GetRarityLabel(ItemRarity rarity) => Profiles[rarity].Label;

        /// <summary>UI（BBCode）での色名。</summary>
        public static string GetRarityColorName(ItemRarity rarity) => Profiles[rarity].ColorName;

        /// <summary>
        /// 希少度ロール：1〜100の乱数に、出土階層の深度ボーナスと任務ボーナス（ボス撃破等）を
        /// 加算した値を閾値と比べて希少度を決める（→ Systems.AppraisalSystem.CreateRelic）。
        /// </summary>
        public static ItemRarity RollRarity(int roll1To100, int floor, int rollBonus)
        {
            int score = roll1To100 + (int)(floor / RarityFloorBonusDivisor) + rollBonus;

            if (score >= LegendaryRollThreshold) return ItemRarity.Legendary;
            if (score >= EpicRollThreshold) return ItemRarity.Epic;
            if (score >= RareRollThreshold) return ItemRarity.Rare;
            return ItemRarity.Common;
        }

        /// <summary>
        /// 探索（採取）任務での未鑑定遺物ドロップ確率（%）。基礎確率に採取スコア・到達階層のボーナスを
        /// 乗せ、上限（GatheringDropMaxPct）でクランプする（→ Systems.GatheringResolver）。
        /// </summary>
        public static int GetGatheringDropPercent(double gatheringScore, int reachedFloor)
        {
            int pct = GatheringDropBasePct
                + (int)(gatheringScore / GatheringDropScoreDivisor)
                + (int)(reachedFloor / GatheringDropFloorDivisor);

            return Math.Clamp(pct, GatheringDropBasePct, GatheringDropMaxPct);
        }
    }
}
