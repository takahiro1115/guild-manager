namespace GuildManager.Core.Models
{
    /// <summary>
    /// 出撃前に提示する「勝算」の定性表現（→ コアシステム刷新仕様「成功率予測エンジン」）。
    ///
    /// 内部では成功率を0.0〜1.0の実数で算出しているが、本プロジェクトは情報公開の原則
    /// （→ コミットd7c7f39「Ratio・クエスト難易度/ランク・相性数値を非表示に」）に従い、
    /// 生の確率をプレイヤーへ提示しない。UIにはこの5段階の定性表現だけを出す。
    ///
    /// 表示文言（「楽勝そうだ」等）はGodot側で解決する（→ 05技術メモ：
    /// GuildManager.CoreはGodotに依存せず、表示文字列を持たない）。
    /// </summary>
    public enum SuccessConfidence
    {
        /// <summary>無謀（成功率が最も低い帯）。</summary>
        Reckless,

        /// <summary>かなり厳しい。</summary>
        Risky,

        /// <summary>五分五分。</summary>
        Even,

        /// <summary>勝算はある。</summary>
        Favorable,

        /// <summary>楽勝そうだ（成功率が最も高い帯）。</summary>
        Overwhelming,
    }
}
