namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 階層ボス討伐のバランス値（→ Systems.DungeonResolver）。
    /// 値は docs/04_バランス表/dungeon.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 設計の骨子：「未調査での突撃は壊滅する」。未対策のギミック1件ごとに被ダメージ倍率が
    /// 積み上がり、即死級を未対策で踏むとHPを全損する。逆に対策が揃っていれば、
    /// 損害は通常の討伐クエスト並みに収まる。
    /// </summary>
    public static class DungeonBalance
    {
        private const string FileName = "dungeon.csv";

        /// <summary>ボスを削り切るのに必要な部隊火力の基準＝階層×この値。</summary>
        public static readonly double PartyPowerRequirementPerFloor = BalanceData.GetDouble(FileName, "PartyPowerRequirementPerFloor");

        /// <summary>完全解析（→ IntelTier.Complete）到達時の与ダメージ補正。</summary>
        public static readonly double FullIntelDamageBonus = BalanceData.GetDouble(FileName, "FullIntelDamageBonus");

        /// <summary>未対策ギミック1件（危険度1あたり）につき被ダメージへ加算される倍率。</summary>
        public static readonly double UncounteredDamageMultiplierPerDangerLevel =
            BalanceData.GetDouble(FileName, "UncounteredDamageMultiplierPerDangerLevel");

        public static readonly int BaseHpLossPctMin = BalanceData.GetInt(FileName, "BaseHpLossPctMin");
        public static readonly int BaseHpLossPctMax = BalanceData.GetInt(FileName, "BaseHpLossPctMax");

        public static readonly int RetreatHpLossPctMin = BalanceData.GetInt(FileName, "RetreatHpLossPctMin");
        public static readonly int RetreatHpLossPctMax = BalanceData.GetInt(FileName, "RetreatHpLossPctMax");

        /// <summary>即死級ギミックを未対策で踏んだ場合のHP消費率（対策の有無が生死を分ける）。</summary>
        public static readonly int InstantKillUncounteredHpLossPct = BalanceData.GetInt(FileName, "InstantKillUncounteredHpLossPct");
    }
}
