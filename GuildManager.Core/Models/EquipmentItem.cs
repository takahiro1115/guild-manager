using System;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ギルド保管庫（<see cref="GameState.Armory"/>）に積まれている装備品の「個体」。
    ///
    /// 既存の <see cref="Item"/> は「カタログ定義」（Id・効果・価格が固定の静的データ、→ ItemCatalog）
    /// であり、同じ鉄の剣を2本持っている状態を表現できない。鑑定（→ Systems.AppraisalSystem）で
    /// 武具が出るようになったことで、ギルドが「まだ誰にも装備させていない現物」を複数抱える状態が
    /// 生まれるため、カタログIdを指す個体としてこのクラスを新設した。
    ///
    /// 冒険者側は従来どおりカタログIdの文字列だけを持つ（→ Adventurer.EquippedWeaponId 等）ため、
    /// 本クラスは「保管庫の在庫」専用であり、装備状態そのものは表現しない。
    /// セーブはプリミティブのみで構成されているためそのままJSON化できる（→ SaveData.Armory）。
    /// </summary>
    public class EquipmentItem
    {
        /// <summary>個体ごとの識別子（保管庫内で一意）。同じカタログIdの武具を複数持てるようにするため。</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>カタログ上のアイテムId（→ ItemCatalog.FindById）。</summary>
        public string ItemId { get; set; } = "";

        /// <summary>
        /// 表示名。カタログ由来のスナップショットを保持する（カタログから引けなくなった
        /// 旧セーブの装備でも、名前だけは一覧に出せるようにするための防御的な二重持ち）。
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 入手した週（→ GameState.WeekNumber）。0＝不明（旧セーブ）。保管庫一覧の並べ替え・
        /// 「いつ掘り出した物か」の表示に使う。
        /// </summary>
        public int AcquiredAtWeek { get; set; }

        /// <summary>入手経路の短い説明（例：「森 第12層の遺物を鑑定」）。演出・履歴表示用。</summary>
        public string AcquiredFrom { get; set; } = "";

        /// <summary>
        /// カタログ定義を引く。未知のId（カタログから削除された等）ならnull。
        /// プロパティではなくメソッドにしてあるのは、System.Text.Jsonが読み取り専用プロパティを
        /// セーブデータへ書き出してしまうのを避けるため（→ SaveData）。
        /// </summary>
        public Item? GetDefinition() => ItemCatalog.FindById(ItemId);

        /// <summary>カタログ定義から保管庫の個体を作る。</summary>
        public static EquipmentItem FromCatalog(Item item, int acquiredAtWeek = 0, string acquiredFrom = "") => new()
        {
            ItemId = item.Id,
            Name = item.Name,
            AcquiredAtWeek = acquiredAtWeek,
            AcquiredFrom = acquiredFrom,
        };
    }
}
