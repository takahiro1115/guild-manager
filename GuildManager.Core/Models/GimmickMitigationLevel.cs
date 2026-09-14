namespace GuildManager.Core.Models
{
    /// <summary>
    /// 環境ギミック1件に対する、パーティの対策達成度（3段階評価）。
    /// 能力値合算とアイテム補填の合計点数から判定する（→ Systems.GimmickEvaluator）。
    /// </summary>
    public enum GimmickMitigationLevel
    {
        /// <summary>未充足：0点。ペナルティがそのまま（100%）発動する。</summary>
        None,

        /// <summary>一部充足：1点。ペナルティが半減する。</summary>
        Partial,

        /// <summary>完全充足：2点以上。ペナルティが無効化される。</summary>
        Full,
    }
}
