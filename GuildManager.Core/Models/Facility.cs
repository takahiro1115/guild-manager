namespace GuildManager.Core.Models
{
    /// <summary>1つの施設の状態。仕様書 03 §6 参照。</summary>
    public class Facility
    {
        public FacilityType Type { get; set; }

        /// <summary>現在のLv（1〜5）。着工しただけでは上がらず、工事完了時にのみ+1される（→ §6.1）。</summary>
        public int CurrentLevel { get; set; } = 1;
    }
}
