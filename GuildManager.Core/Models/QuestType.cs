namespace GuildManager.Core.Models
{
    /// <summary>
    /// クエスト種別。仕様書 03 §4.0 参照。
    /// 脅威度（§4.4）への影響は討伐(Subjugation)のみが対象。調査・探索(Exploration)・
    /// 護衛(Escort)の固有ギミックは今後実装（post-MVP、→ §11）。
    /// </summary>
    public enum QuestType
    {
        Subjugation,
        Exploration,
        Escort,
    }
}
