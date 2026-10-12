namespace GuildManager.Core.Balance
{
    /// <summary>
    /// ギルドの方針・引退後の進路・育成への投資（段4f、§0.105）の値。
    /// 値は docs/04_バランス表/policy.csv から読み込む（→ Systems.GuildPolicySystem）。
    /// </summary>
    public static class PolicyBalance
    {
        private const string FileName = "policy.csv";

        public static readonly double TrainingHardGrowthMultiplier = BalanceData.GetDouble(FileName, "TrainingHardGrowthMultiplier");
        public static readonly double TrainingHardInjuryChance = BalanceData.GetDouble(FileName, "TrainingHardInjuryChance");
        public static readonly int TrainingHardSatisfaction = BalanceData.GetInt(FileName, "TrainingHardSatisfaction");
        public static readonly double TrainingGentleGrowthMultiplier = BalanceData.GetDouble(FileName, "TrainingGentleGrowthMultiplier");
        public static readonly int TrainingGentleSatisfaction = BalanceData.GetInt(FileName, "TrainingGentleSatisfaction");

        public static readonly double PrizeShareRate = BalanceData.GetDouble(FileName, "PrizeShareRate");
        public static readonly int PrizeShareSatisfaction = BalanceData.GetInt(FileName, "PrizeShareSatisfaction");
        public static readonly int PrizeShareDriveBonus = BalanceData.GetInt(FileName, "PrizeShareDriveBonus");

        public static readonly int RetireeRoleGraceWeeks = BalanceData.GetInt(FileName, "RetireeRoleGraceWeeks");

        public static readonly int CoachingCost = BalanceData.GetInt(FileName, "CoachingCost");
        public static readonly int CoachingWeeks = BalanceData.GetInt(FileName, "CoachingWeeks");
        public static readonly int CoachingSlots = BalanceData.GetInt(FileName, "CoachingSlots");
        public static readonly int CoachingRollsPerWeek = BalanceData.GetInt(FileName, "CoachingRollsPerWeek");
        public static readonly double CoachingGrowthMultiplier = BalanceData.GetDouble(FileName, "CoachingGrowthMultiplier");

        public static readonly int ScoutBudgetCost = BalanceData.GetInt(FileName, "ScoutBudgetCost");
        public static readonly int ScoutBudgetExtraCandidates = BalanceData.GetInt(FileName, "ScoutBudgetExtraCandidates");
        public static readonly double ScoutBudgetHighPotentialBonus = BalanceData.GetDouble(FileName, "ScoutBudgetHighPotentialBonus");
    }
}
