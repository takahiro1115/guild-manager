namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 冒険者の個性（志向・気質・相性の素地と見立て、2026年10月・§0.100、→ Systems.PersonalitySystem）のバランス値。
    /// 値は docs/04_バランス表/personality.csv から読み込む（フォールバックは持たない）。
    /// </summary>
    public static class PersonalityBalance
    {
        private const string FileName = "personality.csv";

        /// <summary>副志向を持つ確率（%）。</summary>
        public static readonly int SubDriveChancePercent = BalanceData.GetInt(FileName, "SubDriveChancePercent");
        /// <summary>苦手を持つ確率（%）。苦手は仕事の5つからだけ選ぶ。</summary>
        public static readonly int DislikeChancePercent = BalanceData.GetInt(FileName, "DislikeChancePercent");
        /// <summary>特性に合う志向の選ばれやすさ（ほかの志向の重みは1）。</summary>
        public static readonly int TraitPullWeight = BalanceData.GetInt(FileName, "TraitPullWeight");

        /// <summary>気質の選ばれやすさ（とても慎重・慎重・中庸・大胆・とても大胆の順）。</summary>
        public static readonly int[] TemperamentWeights =
        {
            BalanceData.GetInt(FileName, "TemperamentWeight_VeryCautious"),
            BalanceData.GetInt(FileName, "TemperamentWeight_Cautious"),
            BalanceData.GetInt(FileName, "TemperamentWeight_Balanced"),
            BalanceData.GetInt(FileName, "TemperamentWeight_Bold"),
            BalanceData.GetInt(FileName, "TemperamentWeight_VeryBold"),
        };

        /// <summary>秘薬の娘の相性の素地：両親の円の上の中点±この値。</summary>
        public static readonly int AffinityInheritSpread = BalanceData.GetInt(FileName, "AffinityInheritSpread");

        public static readonly int SatisfactionMain = BalanceData.GetInt(FileName, "SatisfactionMain");
        public static readonly int SatisfactionSub = BalanceData.GetInt(FileName, "SatisfactionSub");
        public static readonly int SatisfactionDisliked = BalanceData.GetInt(FileName, "SatisfactionDisliked");
        public static readonly int MotiveNegativeMain = BalanceData.GetInt(FileName, "MotiveNegativeMain");
        public static readonly int MotiveNegativeSub = BalanceData.GetInt(FileName, "MotiveNegativeSub");

        /// <summary>仲間（動機）：同じ部隊にこの相性以上の相手がいれば喜ぶ。</summary>
        public static readonly int ComradesFriendThreshold = BalanceData.GetInt(FileName, "ComradesFriendThreshold");
        /// <summary>名声（動機）：最後の手柄からこの週数を過ぎると焦る（以後12週ごと）。</summary>
        public static readonly int FameDroughtWeeks = BalanceData.GetInt(FileName, "FameDroughtWeeks");

        public static readonly int TemperamentMatch = BalanceData.GetInt(FileName, "TemperamentMatch");
        public static readonly int TemperamentMismatch = BalanceData.GetInt(FileName, "TemperamentMismatch");
        public static readonly int TemperamentBoldInjury = BalanceData.GetInt(FileName, "TemperamentBoldInjury");

        /// <summary>見立て：きっかけをこの回数見ると「〜らしい」。</summary>
        public static readonly int RevealLikely = BalanceData.GetInt(FileName, "RevealLikely");
        /// <summary>見立て：この回数で確か。</summary>
        public static readonly int RevealSure = BalanceData.GetInt(FileName, "RevealSure");
        /// <summary>採用の一言が主志向を言い当てる確率（%）。</summary>
        public static readonly int HintTruthPercent = BalanceData.GetInt(FileName, "HintTruthPercent");

        // ==================== 段4b（§0.101）：満足度と相性の段階 ====================

        public static readonly int MoraleCheerfulEnter = BalanceData.GetInt(FileName, "MoraleCheerfulEnter");
        public static readonly int MoraleCheerfulExit = BalanceData.GetInt(FileName, "MoraleCheerfulExit");
        public static readonly int MoraleDiscontentEnter = BalanceData.GetInt(FileName, "MoraleDiscontentEnter");
        public static readonly int MoraleDiscontentExit = BalanceData.GetInt(FileName, "MoraleDiscontentExit");
        public static readonly int MoraleBreakingEnter = BalanceData.GetInt(FileName, "MoraleBreakingEnter");
        public static readonly int MoraleBreakingExit = BalanceData.GetInt(FileName, "MoraleBreakingExit");
        public static readonly double MoraleMultiplierCheerful = BalanceData.GetDouble(FileName, "MoraleMultiplier_Cheerful");
        public static readonly double MoraleMultiplierDiscontent = BalanceData.GetDouble(FileName, "MoraleMultiplier_Discontent");
        public static readonly double MoraleMultiplierBreaking = BalanceData.GetDouble(FileName, "MoraleMultiplier_Breaking");
        public static readonly double MoraleCheerfulGrowthBonus = BalanceData.GetDouble(FileName, "MoraleCheerfulGrowthBonus");
        public static readonly double MoraleCheerfulHelpCreditMultiplier = BalanceData.GetDouble(FileName, "MoraleCheerfulHelpCreditMultiplier");
        public static readonly double MoraleLowTraversalHpLossMultiplier = BalanceData.GetDouble(FileName, "MoraleLowTraversalHpLossMultiplier");
        public static readonly int MoraleBreakingContagion = BalanceData.GetInt(FileName, "MoraleBreakingContagion");
        public static readonly int SlackChanceDiscontent = BalanceData.GetInt(FileName, "SlackChance_Discontent");
        public static readonly int SlackChanceBreaking = BalanceData.GetInt(FileName, "SlackChance_Breaking");
        public static readonly int SlackTraitModifier = BalanceData.GetInt(FileName, "SlackTraitModifier");

        public static readonly double DrivePowerMain = BalanceData.GetDouble(FileName, "DrivePower_Main");
        public static readonly double DrivePowerSub = BalanceData.GetDouble(FileName, "DrivePower_Sub");
        public static readonly double DrivePowerDisliked = BalanceData.GetDouble(FileName, "DrivePower_Disliked");
        public static readonly double DriveGrowthMain = BalanceData.GetDouble(FileName, "DriveGrowth_Main");
        public static readonly double DriveGrowthSub = BalanceData.GetDouble(FileName, "DriveGrowth_Sub");
        public static readonly double DriveGrowthDisliked = BalanceData.GetDouble(FileName, "DriveGrowth_Disliked");

        public static readonly int BuddyThreshold = BalanceData.GetInt(FileName, "BuddyThreshold");
        public static readonly double BuddyPowerBonus = BalanceData.GetDouble(FileName, "BuddyPowerBonus");
        public static readonly double HostilePowerPenalty = BalanceData.GetDouble(FileName, "HostilePowerPenalty");
        public static readonly double HostileTraversalHpLossMultiplier = BalanceData.GetDouble(FileName, "HostileTraversalHpLossMultiplier");
        public static readonly int GuardChancePercent = BalanceData.GetInt(FileName, "GuardChancePercent");
        public static readonly double GuardShareRate = BalanceData.GetDouble(FileName, "GuardShareRate");

        /// <summary>相性の素地の帯の上限（共鳴・調和・ふつう。これより遠いと不協和）。</summary>
        public static readonly int[] AffinityBandMax =
        {
            BalanceData.GetInt(FileName, "AffinityBand_ResonantMax"),
            BalanceData.GetInt(FileName, "AffinityBand_HarmoniousMax"),
            BalanceData.GetInt(FileName, "AffinityBand_NeutralMax"),
        };

        /// <summary>帯ごとの相性の上がり方（共鳴・調和・ふつう・不協和の順）。</summary>
        public static readonly double[] AffinityGain =
        {
            BalanceData.GetDouble(FileName, "AffinityGain_Resonant"), BalanceData.GetDouble(FileName, "AffinityGain_Harmonious"),
            BalanceData.GetDouble(FileName, "AffinityGain_Neutral"), BalanceData.GetDouble(FileName, "AffinityGain_Dissonant"),
        };

        /// <summary>帯ごとの相性の下がり方。</summary>
        public static readonly double[] AffinityLoss =
        {
            BalanceData.GetDouble(FileName, "AffinityLoss_Resonant"), BalanceData.GetDouble(FileName, "AffinityLoss_Harmonious"),
            BalanceData.GetDouble(FileName, "AffinityLoss_Neutral"), BalanceData.GetDouble(FileName, "AffinityLoss_Dissonant"),
        };

        /// <summary>帯ごとの、はじめて組んだときの相性。</summary>
        public static readonly int[] AffinityInitial =
        {
            BalanceData.GetInt(FileName, "AffinityInitial_Resonant"), BalanceData.GetInt(FileName, "AffinityInitial_Harmonious"),
            BalanceData.GetInt(FileName, "AffinityInitial_Neutral"), BalanceData.GetInt(FileName, "AffinityInitial_Dissonant"),
        };

        // ==================== 段4c（§0.102）：出来事と事故 ====================

        public static readonly int EventChanceCheerful = BalanceData.GetInt(FileName, "EventChance_Cheerful");
        public static readonly int EventChanceNormal = BalanceData.GetInt(FileName, "EventChance_Normal");
        public static readonly int EventChanceDiscontent = BalanceData.GetInt(FileName, "EventChance_Discontent");
        public static readonly int EventChanceBreaking = BalanceData.GetInt(FileName, "EventChance_Breaking");
        public static readonly int EventWeightBase = BalanceData.GetInt(FileName, "EventWeightBase");
        public static readonly int EventWeightPull = BalanceData.GetInt(FileName, "EventWeightPull");
        public static readonly int QuarrelCompatLoss = BalanceData.GetInt(FileName, "QuarrelCompatLoss");
        public static readonly int QuarrelSatisfaction = BalanceData.GetInt(FileName, "QuarrelSatisfaction");
        public static readonly int GloomWeeksMin = BalanceData.GetInt(FileName, "GloomWeeksMin");
        public static readonly int GloomWeeksMax = BalanceData.GetInt(FileName, "GloomWeeksMax");
        public static readonly int WasteWageWeeks = BalanceData.GetInt(FileName, "WasteWageWeeks");
        public static readonly int FretBossHpLossPct = BalanceData.GetInt(FileName, "FretBossHpLossPct");
        public static readonly int MentorCompatGain = BalanceData.GetInt(FileName, "MentorCompatGain");
        public static readonly int MentorSatisfaction = BalanceData.GetInt(FileName, "MentorSatisfaction");
        public static readonly int TreatSatisfaction = BalanceData.GetInt(FileName, "TreatSatisfaction");
        public static readonly int BondChancePercent = BalanceData.GetInt(FileName, "BondChancePercent");
        public static readonly int BondCompatGain = BalanceData.GetInt(FileName, "BondCompatGain");
        public static readonly int BondSatisfaction = BalanceData.GetInt(FileName, "BondSatisfaction");
        public static readonly int BrawlChancePercent = BalanceData.GetInt(FileName, "BrawlChancePercent");
        public static readonly int BrawlCompatLoss = BalanceData.GetInt(FileName, "BrawlCompatLoss");
        public static readonly int PoachDeadlineWeeks = BalanceData.GetInt(FileName, "PoachDeadlineWeeks");
        public static readonly int PoachRaiseSatisfaction = BalanceData.GetInt(FileName, "PoachRaiseSatisfaction");
        public static readonly int PoachBonusSatisfaction = BalanceData.GetInt(FileName, "PoachBonusSatisfaction");
        public static readonly int BoycottChanceDiscontent = BalanceData.GetInt(FileName, "BoycottChance_Discontent");
        public static readonly int BoycottChanceBreaking = BalanceData.GetInt(FileName, "BoycottChance_Breaking");
        public static readonly int BoycottDriveModifier = BalanceData.GetInt(FileName, "BoycottDriveModifier");
        public static readonly int RefuseChanceHostile = BalanceData.GetInt(FileName, "RefuseChanceHostile");
        public static readonly int FeudThreshold = BalanceData.GetInt(FileName, "FeudThreshold");
        public static readonly int DisobeyChanceDiscontent = BalanceData.GetInt(FileName, "DisobeyChance_Discontent");
        public static readonly int DisobeyChanceBreaking = BalanceData.GetInt(FileName, "DisobeyChance_Breaking");
        public static readonly int DisobeyVeryModifier = BalanceData.GetInt(FileName, "DisobeyVeryModifier");
        public static readonly int DisobeyLeaderLdrDivisor = BalanceData.GetInt(FileName, "DisobeyLeaderLdrDivisor");
        public static readonly int DisobeyFleeSatisfaction = BalanceData.GetInt(FileName, "DisobeyFleeSatisfaction");
        public static readonly int DisobeyFleeCompatLoss = BalanceData.GetInt(FileName, "DisobeyFleeCompatLoss");
        public static readonly int ShowAccidentWarnings = BalanceData.GetInt(FileName, "ShowAccidentWarnings");
        public static readonly double PumpedPowerBonus = BalanceData.GetDouble(FileName, "PumpedPowerBonus");
    }
}
