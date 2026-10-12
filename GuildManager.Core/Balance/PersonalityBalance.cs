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
    }
}
