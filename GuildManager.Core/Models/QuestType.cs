namespace GuildManager.Core.Models
{
    /// <summary>
    /// クエスト種別。仕様書 03 §4.0 参照。
    /// 脅威度（§4.4）への影響は討伐(Subjugation)のみが対象。
    /// クエスト適性ボーナス（→ 03 §4.2.1、v1.7改訂）で種別ごとの重視ステータスに接続済み
    /// （護衛=MND・VIT、調査・探索=AGI・DEX・INT）。敵種別によるさらなる細分化はpost-MVP（→ §11）。
    /// </summary>
    public enum QuestType
    {
        Subjugation,
        Exploration,
        Escort,
    }
}
