namespace GuildManager.Core.Models
{
    /// <summary>
    /// 単一の建設キューの中身。仕様書 03 §6.1「同時着工は1件のみ」参照。
    /// GameState.UnderConstruction が null かどうかで「工事中か」を表す
    /// （null許容参照型により、同時に2件以上を持てない設計を型で保証する）。
    /// </summary>
    public class FacilityConstruction
    {
        public FacilityType Type { get; set; }

        /// <summary>完成時に到達するLv（着工時点の現在Lv+1）。</summary>
        public int TargetLevel { get; set; }

        /// <summary>完成までの残り週数。0に到達した週に完成する。</summary>
        public int WeeksRemaining { get; set; }
    }
}
