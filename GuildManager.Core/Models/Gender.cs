namespace GuildManager.Core.Models
{
    /// <summary>
    /// 性別。仕様書 03 §2.4 参照（v1.8改訂で新設）。
    /// 現時点ではステータス・成長・戦闘のいずれにも影響を与えない。氏名生成
    /// （NameGenerator）と、将来の立ち絵システム連携（次フェーズ、post-MVP）の
    /// ための土台として先行して追加したフィールドである。
    /// </summary>
    public enum Gender
    {
        Male,
        Female,
    }
}
