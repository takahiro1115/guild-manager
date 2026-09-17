namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 大迷宮「探索（採取）」任務（→ Systems.GatheringResolver）のバランス値。
    /// 値は docs/04_バランス表/gathering.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 採取スコア＝(Σ(AGI×係数＋DEX×係数)＋部隊長LDR×係数)×部隊の平均HP比率。
    /// 調査・道中進軍（ScoutingBalance・DungeonTraversalBalance）と違い、消耗（HP比率）が
    /// スコアそのものに直接効く＝弱った部隊は採取の効率も落ちる設計。
    /// </summary>
    public static class GatheringBalance
    {
        private const string FileName = "gathering.csv";

        public static readonly double AgiCoefficient = BalanceData.GetDouble(FileName, "AgiCoefficient");
        public static readonly double DexCoefficient = BalanceData.GetDouble(FileName, "DexCoefficient");
        public static readonly double LeaderLdrCoefficient = BalanceData.GetDouble(FileName, "LeaderLdrCoefficient");

        /// <summary>盗賊(Thief)・斥候(Ranger)1名につき採取スコアへ加算する固定ボーナス。</summary>
        public static readonly double ThiefRangerScoreBonus = BalanceData.GetDouble(FileName, "ThiefRangerScoreBonus");

        /// <summary>基本素材獲得数＝Max(1, 選ばれた素材のBaseYield＋(int)(採取スコア÷この値)＋(到達階層÷ReachedFloorDivisor))。</summary>
        public static readonly double MaterialYieldDivisor = BalanceData.GetDouble(FileName, "MaterialYieldDivisor");

        /// <summary>到達階層による獲得数ボーナスの除数。</summary>
        public static readonly int ReachedFloorDivisor = BalanceData.GetInt(FileName, "ReachedFloorDivisor");

        /// <summary>換金ゴールド＝採取スコア×この係数。</summary>
        public static readonly double GoldPerScore = BalanceData.GetDouble(FileName, "GoldPerScore");

        // ---- HP消費（採取は低リスク経路：致死判定には接続しない） ----
        public static readonly int HpLossPctMin = BalanceData.GetInt(FileName, "HpLossPctMin");
        public static readonly int HpLossPctMax = BalanceData.GetInt(FileName, "HpLossPctMax");
    }
}
