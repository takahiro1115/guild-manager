namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 顧問制度（教官・参謀・スカウト）関連の暫定バランス値。仕様書 03 §7 参照。
    /// いずれも「生涯ピーク値 × 係数」の連続比例でボーナスを算出する（一致判定は行わない）。
    /// 現状はすべて仮値の定数（→ BAL: 顧問）。
    /// </summary>
    public static class AdvisorBalance
    {
        /// <summary>
        /// 教官ボーナス係数。配置先施設の対象ステータス（1つまたは2つの平均）の生涯ピーク値に
        /// 掛け、成長ロールの確率倍率（GrowthBalance.TrainingFacilityMultiplier）に加算する。
        /// → BAL: 顧問/教官伝授。仮値：ピーク100で+0.5（成長ロール確率が1.5倍相当になる）。
        /// </summary>
        public const double TrainerBonusCoefficient = 0.005;

        /// <summary>
        /// 参謀ボーナス係数。生涯ピーク7能力平均に掛け、PartyScout（§4.1）と
        /// SurvivalThreshold（§4.3）の両方に加算する。→ BAL: 顧問/参謀補正。
        /// 仮値：ピーク平均100で+20（他の項と同程度の桁になるよう調整した仮値）。
        /// </summary>
        public const double AdvisorBonusCoefficient = 0.2;

        /// <summary>
        /// スカウトボーナス係数。生涯ピークのLDR・DEX平均に掛け、新春採用試験の
        /// 有望新人応募率（RecruitmentSystem.HighPotentialBaseChance）に加算する。
        /// → BAL: 顧問/スカウト補正。仮値：ピーク平均100で+0.3（30ポイント加算）。
        /// </summary>
        public const double ScoutMasterBonusCoefficient = 0.003;
    }
}
