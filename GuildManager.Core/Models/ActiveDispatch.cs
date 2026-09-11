namespace GuildManager.Core.Models
{
    /// <summary>
    /// 進行中（派遣中）の複数週クエスト1件。仕様書 03 §4.0.1 参照。
    /// Party はオブジェクト参照なので、派遣中にメンバーのHP・実効値等が変化しても
    /// （本来は§4.0.1により移動中は何も変化しないはずだが）満了週の解決時には
    /// 常に最新の状態が参照される。
    /// </summary>
    public class ActiveDispatch
    {
        public Party Party { get; set; } = new();
        public Quest Quest { get; set; } = new();

        /// <summary>満了までの残り週数。0に到達した週に解決する。</summary>
        public int WeeksRemaining { get; set; }
    }
}
