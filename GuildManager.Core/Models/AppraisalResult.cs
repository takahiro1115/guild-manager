namespace GuildManager.Core.Models
{
    /// <summary>
    /// 鑑定結果の種別（→ AppraisalResult.Type、Balance.RelicBalance の武具率/素材率/換金率）。
    /// </summary>
    public enum AppraisalResultType
    {
        /// <summary>武具（→ GameState.Armory へ格納）。</summary>
        Equipment = 0,

        /// <summary>素材（→ GameState.AddMaterial で加算）。</summary>
        Material = 1,

        /// <summary>換金・古代硬貨（→ GameState.Gold へ加算）。</summary>
        Gold = 2,
    }

    /// <summary>
    /// 未鑑定遺物1個の鑑定結果（→ Systems.AppraisalSystem.Appraise、03 §4.7）。
    /// UI（InventoryPanel）はこれを読んで獲得アイテムとアルベールの鑑定コメントを表示する
    /// （Systems.GatheringResult・DungeonMissionResolution と同じ「結果DTO」の役割）。
    ///
    /// 3種別のうち実際に値が入るのは1組だけ（Equipment→ResultEquipment、
    /// Material→ResultMaterialId/Count、Gold→ResultGold）。
    /// </summary>
    public class AppraisalResult
    {
        /// <summary>鑑定後の名称（武具名・素材名・「古代硬貨」）。</summary>
        public string ItemName { get; set; } = "";

        /// <summary>鑑定元の遺物の希少度（結果の演出色・ログの強調に使う）。</summary>
        public ItemRarity Rarity { get; set; } = ItemRarity.Common;

        public AppraisalResultType Type { get; set; } = AppraisalResultType.Gold;

        /// <summary>武具だった場合の現物（→ GameState.Armory に格納済みの同一インスタンス）。それ以外はnull。</summary>
        public EquipmentItem? ResultEquipment { get; set; }

        /// <summary>素材だった場合の素材Id（→ Balance.MaterialBalance）。それ以外はnull。</summary>
        public string? ResultMaterialId { get; set; }

        /// <summary>素材だった場合の個数。それ以外は0。</summary>
        public int ResultMaterialCount { get; set; }

        /// <summary>換金だった場合の獲得ゴールド。それ以外は0。</summary>
        public int ResultGold { get; set; }

        /// <summary>アルベールの鑑定コメント（演出用のフレーバーテキスト）。</summary>
        public string FlavorText { get; set; } = "";

        /// <summary>鑑定に支払った費用（→ UnidentifiedItem.AppraisalCost。週報ログの収支表示用）。</summary>
        public int AppraisalCost { get; set; }
    }
}
