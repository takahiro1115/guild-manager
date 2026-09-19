namespace GuildManager.Core.Balance
{
    /// <summary>
    /// ギルド進行（同時出撃枠）に関するバランス値。
    /// 値は docs/04_バランス表/progression.csv から読み込む（→ 03 §10.1）。
    ///
    /// 出撃枠の拡張は大迷宮の節目ボス撃破（森10F/20F、→ DungeonExpeditionSystem.ApplyFieldProgression）が
    /// 唯一のトリガー。旧通常クエストの「ランクE昇格試験」（GuildProgressionSystem）とその提示条件・
    /// 報酬の値は、旧クエストの撤去（2026年9月）に伴い削除した。
    /// </summary>
    public static class ProgressionBalance
    {
        private const string FileName = "progression.csv";

        // ---- 同時出撃枠（→ GameState.UnlockedSquadSlots） ----

        /// <summary>ゲーム開始時の同時出撃枠。1枠の中で1〜4名の分割編成は自由（フリーアサイン）。</summary>
        public static readonly int InitialSquadSlots = BalanceData.GetInt(FileName, "InitialSquadSlots");
    }
}
