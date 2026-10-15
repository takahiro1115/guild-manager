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

        // ---- オルテンシア（廃墟）の玉座への回廊（§0.130） ----
        public static readonly int OrtensiaMaxSquads = BalanceData.GetInt(FileName, "OrtensiaMaxSquads");
        public static readonly int OrtensiaThresholdOutpost = BalanceData.GetInt(FileName, "OrtensiaThresholdOutpost");
        public static readonly int OrtensiaThresholdFinal = BalanceData.GetInt(FileName, "OrtensiaThresholdFinal");
        public static readonly int OrtensiaFullSquadSize = BalanceData.GetInt(FileName, "OrtensiaFullSquadSize");
        public static readonly double OrtensiaBreakRate = BalanceData.GetDouble(FileName, "OrtensiaBreakRate");
        public static readonly double OrtensiaIllusionRate = BalanceData.GetDouble(FileName, "OrtensiaIllusionRate");
        public static readonly int OrtensiaSealMax = BalanceData.GetInt(FileName, "OrtensiaSealMax");
        public static readonly int OrtensiaSealDamageBase = BalanceData.GetInt(FileName, "OrtensiaSealDamageBase");
        public static readonly int OrtensiaResealMonths = BalanceData.GetInt(FileName, "OrtensiaResealMonths");
        public static readonly double OrtensiaGuardRate = BalanceData.GetDouble(FileName, "OrtensiaGuardRate");
        public static readonly double OrtensiaAweRate = BalanceData.GetDouble(FileName, "OrtensiaAweRate");
        public static readonly int OrtensiaAweFamePerG1 = BalanceData.GetInt(FileName, "OrtensiaAweFamePerG1");
        public static readonly int OrtensiaAweFameMax = BalanceData.GetInt(FileName, "OrtensiaAweFameMax");
        public static readonly int OrtensiaThroneDamageBase = BalanceData.GetInt(FileName, "OrtensiaThroneDamageBase");
        public static readonly int OrtensiaThroneDamageMin = BalanceData.GetInt(FileName, "OrtensiaThroneDamageMin");
        public static readonly int OrtensiaThroneDamageMax = BalanceData.GetInt(FileName, "OrtensiaThroneDamageMax");
        public static readonly int OrtensiaWinHpLossPctMin = BalanceData.GetInt(FileName, "OrtensiaWinHpLossPctMin");
        public static readonly int OrtensiaWinHpLossPctMax = BalanceData.GetInt(FileName, "OrtensiaWinHpLossPctMax");
        public static readonly int OrtensiaLossHpLossPctMin = BalanceData.GetInt(FileName, "OrtensiaLossHpLossPctMin");
        public static readonly int OrtensiaLossHpLossPctMax = BalanceData.GetInt(FileName, "OrtensiaLossHpLossPctMax");
        public static readonly double OrtensiaIntelResealReveal = BalanceData.GetDouble(FileName, "OrtensiaIntelResealReveal");
        public static readonly int NobleDaughterPaBonus = BalanceData.GetInt(FileName, "NobleDaughterPaBonus");

        // ---- イグニア（峡谷）の灼熱の底（§0.131） ----
        public static readonly int IgniaMaxSquads = BalanceData.GetInt(FileName, "IgniaMaxSquads");
        public static readonly int IgniaRegenPerMonth = BalanceData.GetInt(FileName, "IgniaRegenPerMonth");
        public static readonly int IgniaThresholdOutpost = BalanceData.GetInt(FileName, "IgniaThresholdOutpost");
        public static readonly int IgniaThresholdFinal = BalanceData.GetInt(FileName, "IgniaThresholdFinal");
        public static readonly int IgniaFullSquadSize = BalanceData.GetInt(FileName, "IgniaFullSquadSize");
        public static readonly double IgniaBreakRate = BalanceData.GetDouble(FileName, "IgniaBreakRate");
        public static readonly int IgniaDamageBase = BalanceData.GetInt(FileName, "IgniaDamageBase");
        public static readonly int IgniaDamageMin = BalanceData.GetInt(FileName, "IgniaDamageMin");
        public static readonly int IgniaDamageMax = BalanceData.GetInt(FileName, "IgniaDamageMax");
        public static readonly int IgniaHeatPct = BalanceData.GetInt(FileName, "IgniaHeatPct");
        public static readonly int IgniaHeatPctMin = BalanceData.GetInt(FileName, "IgniaHeatPctMin");
        public static readonly int IgniaHeatPctMax = BalanceData.GetInt(FileName, "IgniaHeatPctMax");
        public static readonly int IgniaFailExtraPct = BalanceData.GetInt(FileName, "IgniaFailExtraPct");
        public static readonly double IgniaLeaveHpRate = BalanceData.GetDouble(FileName, "IgniaLeaveHpRate");
        public static readonly int IgniaMinFighters = BalanceData.GetInt(FileName, "IgniaMinFighters");
        public static readonly double IgniaPushFitRate = BalanceData.GetDouble(FileName, "IgniaPushFitRate");
        public static readonly double IgniaPushHeatRate = BalanceData.GetDouble(FileName, "IgniaPushHeatRate");
        public static readonly int IgniaPushFatalRate = BalanceData.GetInt(FileName, "IgniaPushFatalRate");
        public static readonly int IgniaForgeScaleCost = BalanceData.GetInt(FileName, "IgniaForgeScaleCost");
        public static readonly int IgniaForgeGoldCost = BalanceData.GetInt(FileName, "IgniaForgeGoldCost");

        // ---- ネフェリス（深淵）の心臓の間（§0.132） ----
        public static readonly int NepherisMaxSquads = BalanceData.GetInt(FileName, "NepherisMaxSquads");
        public static readonly int NepherisThresholdOutpost = BalanceData.GetInt(FileName, "NepherisThresholdOutpost");
        public static readonly int NepherisThresholdFinal = BalanceData.GetInt(FileName, "NepherisThresholdFinal");
        public static readonly double NepherisBreakRate = BalanceData.GetDouble(FileName, "NepherisBreakRate");
        public static readonly double NepherisCharmRate = BalanceData.GetDouble(FileName, "NepherisCharmRate");
        public static readonly int NepherisDamageBase = BalanceData.GetInt(FileName, "NepherisDamageBase");
        public static readonly int NepherisDamageMin = BalanceData.GetInt(FileName, "NepherisDamageMin");
        public static readonly int NepherisDamageMax = BalanceData.GetInt(FileName, "NepherisDamageMax");
        public static readonly double NepherisIntelSongReveal = BalanceData.GetDouble(FileName, "NepherisIntelSongReveal");

        // ---- 介入「名を呼ぶ」 ----
        public static readonly int CallNameBaseChance = BalanceData.GetInt(FileName, "CallNameBaseChance");
        public static readonly int CallNameCompatibilityDivisor = BalanceData.GetInt(FileName, "CallNameCompatibilityDivisor");
        public static readonly int CallNameMoraleDivisor = BalanceData.GetInt(FileName, "CallNameMoraleDivisor");
        public static readonly int CallNameMaxChance = BalanceData.GetInt(FileName, "CallNameMaxChance");
    }
}
