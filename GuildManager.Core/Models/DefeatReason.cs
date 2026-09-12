namespace GuildManager.Core.Models
{
    /// <summary>敗北理由。仕様書 03 §8.3 参照。</summary>
    public enum DefeatReason
    {
        /// <summary>破産：所持金マイナスが4週連続で解消されない（4週間の猶予あり）。</summary>
        Bankruptcy,

        /// <summary>治安崩壊：脅威度が100%に到達した週の決算時点で猶予なく即時敗北。</summary>
        SecurityCollapse,
    }
}
