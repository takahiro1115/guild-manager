namespace GuildManager.Core.Models
{
    /// <summary>
    /// 配置（前衛/後衛）。仕様書 03 §4.2 参照。
    /// パーティのスロット位置ではなく、冒険者個人に紐づく永続状態
    /// （クエストをまたいで保持され、毎週・毎クエスト選び直す必要はない）。
    /// </summary>
    public enum Placement
    {
        Front,
        Back
    }
}
