namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 大迷宮の支配者と支配者戦（2026年10月・§0.127、→ Systems.RulerSystem、docs/検討中_大迷宮の支配者.md §3）のバランス値。
    /// 値は docs/04_バランス表/rulers.csv から読み込む（フォールバックは持たない）。
    /// </summary>
    public static class RulerBalance
    {
        private const string FileName = "rulers.csv";

        // ---- 支配者に共通 ----
        public static readonly int MaxHp = BalanceData.GetInt(FileName, "MaxHp");
        public static readonly int RegenPerMonth = BalanceData.GetInt(FileName, "RegenPerMonth");
        public static readonly int OutpostRetreatHp = BalanceData.GetInt(FileName, "OutpostRetreatHp");
        public static readonly int InterventionsBase = BalanceData.GetInt(FileName, "InterventionsBase");

        // ---- ヴェスペラ（森）の一騎打ち ----
        public static readonly int VesperaMaxRepresentatives = BalanceData.GetInt(FileName, "VesperaMaxRepresentatives");
        public static readonly int VesperaMaxBouts = BalanceData.GetInt(FileName, "VesperaMaxBouts");
        public static readonly double VesperaStrengthOutpost = BalanceData.GetDouble(FileName, "VesperaStrengthOutpost");
        public static readonly double VesperaStrengthFinal = BalanceData.GetDouble(FileName, "VesperaStrengthFinal");
        public static readonly double VesperaFatiguePerBout = BalanceData.GetDouble(FileName, "VesperaFatiguePerBout");
        public static readonly int VesperaCharmThresholdOutpost = BalanceData.GetInt(FileName, "VesperaCharmThresholdOutpost");
        public static readonly int VesperaCharmThresholdFinal = BalanceData.GetInt(FileName, "VesperaCharmThresholdFinal");
        public static readonly int VesperaLonelyThresholdBonus = BalanceData.GetInt(FileName, "VesperaLonelyThresholdBonus");
        public static readonly double VesperaWaverRate = BalanceData.GetDouble(FileName, "VesperaWaverRate");
        public static readonly double VesperaWaverStrengthFactor = BalanceData.GetDouble(FileName, "VesperaWaverStrengthFactor");
        public static readonly int VesperaWinDamageBase = BalanceData.GetInt(FileName, "VesperaWinDamageBase");
        public static readonly int VesperaWinDamageMin = BalanceData.GetInt(FileName, "VesperaWinDamageMin");
        public static readonly int VesperaWinDamageMax = BalanceData.GetInt(FileName, "VesperaWinDamageMax");
        public static readonly int VesperaLossDamage = BalanceData.GetInt(FileName, "VesperaLossDamage");
        public static readonly int VesperaWinHpLossPctMin = BalanceData.GetInt(FileName, "VesperaWinHpLossPctMin");
        public static readonly int VesperaWinHpLossPctMax = BalanceData.GetInt(FileName, "VesperaWinHpLossPctMax");
        public static readonly int VesperaLossHpLossPctMin = BalanceData.GetInt(FileName, "VesperaLossHpLossPctMin");
        public static readonly int VesperaLossHpLossPctMax = BalanceData.GetInt(FileName, "VesperaLossHpLossPctMax");
        public static readonly double VesperaIntelCharmReveal = BalanceData.GetDouble(FileName, "VesperaIntelCharmReveal");
        public static readonly double VesperaIntelWeaponReveal = BalanceData.GetDouble(FileName, "VesperaIntelWeaponReveal");
        public static readonly double VesperaIntelStrengthReveal = BalanceData.GetDouble(FileName, "VesperaIntelStrengthReveal");

        // ---- メルジーネ（洞窟）の調べの読み合い（§0.129） ----
        public static readonly int MelusineMaxSquads = BalanceData.GetInt(FileName, "MelusineMaxSquads");
        public static readonly int MelusineThresholdOutpost = BalanceData.GetInt(FileName, "MelusineThresholdOutpost");
        public static readonly int MelusineThresholdFinal = BalanceData.GetInt(FileName, "MelusineThresholdFinal");
        public static readonly double MelusineSwallowRate = BalanceData.GetDouble(FileName, "MelusineSwallowRate");
        public static readonly double MelusineSweetCharmRate = BalanceData.GetDouble(FileName, "MelusineSweetCharmRate");
        public static readonly int MelusineDamageBase = BalanceData.GetInt(FileName, "MelusineDamageBase");
        public static readonly int MelusineDamageMin = BalanceData.GetInt(FileName, "MelusineDamageMin");
        public static readonly int MelusineDamageMax = BalanceData.GetInt(FileName, "MelusineDamageMax");
        public static readonly int MelusineWinHpLossPctMin = BalanceData.GetInt(FileName, "MelusineWinHpLossPctMin");
        public static readonly int MelusineWinHpLossPctMax = BalanceData.GetInt(FileName, "MelusineWinHpLossPctMax");
        public static readonly int MelusineLossHpLossPctMin = BalanceData.GetInt(FileName, "MelusineLossHpLossPctMin");
        public static readonly int MelusineLossHpLossPctMax = BalanceData.GetInt(FileName, "MelusineLossHpLossPctMax");
        public static readonly double MelusineIntelRevealOne = BalanceData.GetDouble(FileName, "MelusineIntelRevealOne");
        public static readonly double MelusineIntelRevealTwo = BalanceData.GetDouble(FileName, "MelusineIntelRevealTwo");
        public static readonly double MelusineIntelRevealAll = BalanceData.GetDouble(FileName, "MelusineIntelRevealAll");

        // ---- 介入「名を呼ぶ」 ----
        public static readonly int CallNameBaseChance = BalanceData.GetInt(FileName, "CallNameBaseChance");
        public static readonly int CallNameCompatibilityDivisor = BalanceData.GetInt(FileName, "CallNameCompatibilityDivisor");
        public static readonly int CallNameMoraleDivisor = BalanceData.GetInt(FileName, "CallNameMoraleDivisor");
        public static readonly int CallNameMaxChance = BalanceData.GetInt(FileName, "CallNameMaxChance");
    }
}
