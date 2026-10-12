namespace GuildManager.Core.Models
{
    /// <summary>
    /// 研究室で進めている研究（2026年10月・§0.114、→ GameState.ResearchInProgress）。同時に1件だけ。
    /// 始めたときに研究費と素材を払い、残り週が0になった週の決算で完了する（→ Systems.ResearchSystem.ProcessWeek）。
    /// プリミティブだけで構成され、セーブへそのまま JSON 化できる。
    /// </summary>
    public class ResearchProgress
    {
        /// <summary>研究Id（→ research.csv）。</summary>
        public string ResearchId { get; set; } = "";

        /// <summary>完了までの残り週。</summary>
        public int WeeksRemaining { get; set; }

        /// <summary>研究の手伝いで縮めた週（→ ResearchSystem.MaxShortenWeeks まで）。</summary>
        public int WeeksShortened { get; set; }
    }
}
