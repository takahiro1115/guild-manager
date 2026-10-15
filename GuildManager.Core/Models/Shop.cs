namespace GuildManager.Core.Models
{
    /// <summary>ギルドの店の種類（§0.134、→ Systems.GuildShopSystem）。</summary>
    public enum ShopKind
    {
        /// <summary>道具屋：DEX・INT（目利き・調合）。倉庫の素材の在庫が多いほど売れる。</summary>
        ItemShop,
        /// <summary>酒場：LDR・MND（客あしらい）。二つ名・殿堂入りの子が立つと客が集まる。</summary>
        Tavern,
    }
}
