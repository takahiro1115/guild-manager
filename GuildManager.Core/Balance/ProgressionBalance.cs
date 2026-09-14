namespace GuildManager.Core.Balance
{
    /// <summary>
    /// ギルド進行（同時出撃枠の段階開放・ランク昇格試験）に関するバランス値。
    /// → コアシステム刷新仕様「4. 進行管理（ランク昇格と第2パーティ開放）」参照。
    /// 値は docs/04_バランス表/progression.csv から読み込む（→ 03 §10.1）。
    ///
    /// 設計意図：プレイ開始40〜60分（4〜5回目の出撃）で第2部隊枠に到達させるため、
    /// 昇格試験の提示条件を「累計出撃3回以上 かつ 資金100G以上」という
    /// 早期に自然と満たせる水準に置いている（→ GuildProgressionSystem）。
    /// </summary>
    public static class ProgressionBalance
    {
        private const string FileName = "progression.csv";

        // ---- 同時出撃枠（→ GameState.UnlockedSquadSlots） ----

        /// <summary>ゲーム開始時の同時出撃枠。1枠の中で1〜4名の分割編成は自由（フリーアサイン）。</summary>
        public static readonly int InitialSquadSlots = BalanceData.GetInt(FileName, "InitialSquadSlots");

        /// <summary>昇格試験突破後の同時出撃枠。</summary>
        public static readonly int SquadSlotsAfterPromotionExam = BalanceData.GetInt(FileName, "SquadSlotsAfterPromotionExam");

        // ---- 昇格試験の提示条件（→ Phase 2） ----

        public static readonly int PromotionExamMinDispatchCount = BalanceData.GetInt(FileName, "PromotionExamMinDispatchCount");
        public static readonly int PromotionExamMinGold = BalanceData.GetInt(FileName, "PromotionExamMinGold");

        // ---- 昇格試験突破時の報酬（→ Phase 4） ----

        /// <summary>昇格報奨金（クエスト報酬とは別枠。新人を雇用できる十分な額）。</summary>
        public static readonly int PromotionExamRewardGold = BalanceData.GetInt(FileName, "PromotionExamRewardGold");

        /// <summary>昇格時に酒場へ補充される新規冒険者の人数。</summary>
        public static readonly int PromotionExamNewHireCount = BalanceData.GetInt(FileName, "PromotionExamNewHireCount");

        // ---- 昇格試験クエストそのものの定義（→ GuildProgressionSystem.CreatePromotionExamQuest） ----

        public static readonly string PromotionExamQuestName = BalanceData.GetString(FileName, "PromotionExamQuestName");
        public static readonly int PromotionExamDifficulty = BalanceData.GetInt(FileName, "PromotionExamDifficulty");
        public static readonly int PromotionExamScoutRequirement = BalanceData.GetInt(FileName, "PromotionExamScoutRequirement");
        public static readonly int PromotionExamRewardQuestGold = BalanceData.GetInt(FileName, "PromotionExamRewardQuestGold");
        public static readonly int PromotionExamDeadlineWeeks = BalanceData.GetInt(FileName, "PromotionExamDeadlineWeeks");
        public static readonly int PromotionExamRecommendedMembers = BalanceData.GetInt(FileName, "PromotionExamRecommendedMembers");
    }
}
