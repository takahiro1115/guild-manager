using System;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// アイテムの希少度（→ 未鑑定遺物と鑑定システム、03 §4.7）。未鑑定遺物の鑑定費用・
    /// 鑑定結果の比率・抽選プールはすべてこの4段階で決まる（→ Balance.RelicBalance）。
    /// 表示上の通称は銅・銀・金・虹（→ RelicBalance.GetRarityLabel）。
    /// </summary>
    public enum ItemRarity
    {
        /// <summary>銅。最も出やすく、素材・小銭に化けやすい。</summary>
        Common = 0,

        /// <summary>銀。</summary>
        Rare = 1,

        /// <summary>金。</summary>
        Epic = 2,

        /// <summary>虹。最深部・高階層ボスからしか実質出ない当たり枠。</summary>
        Legendary = 3,
    }

    /// <summary>
    /// 未鑑定の古代遺物（レリック。→ 03 §4.7）。探索（採取）任務の副産物、および階層ボス撃破の
    /// 確定ドロップとしてギルドへ持ち帰られ、<see cref="GameState.UnidentifiedItems"/> に積まれる。
    ///
    /// 中身は鑑定するまで分からない（＝このクラスは「何が出るか」の情報を一切持たない）。
    /// 実際の中身は鑑定を実行した瞬間に抽選される（→ Systems.AppraisalSystem.Appraise）。
    /// 出土地（<see cref="OriginFieldId"/>）と出土階層（<see cref="OriginFloor"/>）だけは
    /// 判明しており、素材が出た場合の抽選テーブル（→ Balance.MaterialBalance）に使われる。
    ///
    /// セーブは本クラスをそのままJSON化する（プリミティブと列挙のみで構成されている）。
    /// Idを Guid ではなく string にしてあるのは、UI（ItemListの行→遺物）の突き合わせと
    /// セーブデータの可読性のため（→ Systems.AppraisalSystem.CanAppraise の引数）。
    /// </summary>
    public class UnidentifiedItem
    {
        /// <summary>遺物1個体ごとの識別子（Guidの文字列表現）。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>未鑑定時の仮称（例：「？？？ 苔むした封印箱」）。→ RelicNames.Roll。</summary>
        public string Name { get; set; } = "";

        public ItemRarity Rarity { get; set; } = ItemRarity.Common;

        /// <summary>出土したフィールドのId（"forest" / "cave" 等。→ Models.DungeonField.Id）。</summary>
        public string OriginFieldId { get; set; } = "";

        /// <summary>出土した階層。深いほど良い物が出る（→ RelicBalance の希少度ロール）。</summary>
        public int OriginFloor { get; set; }

        /// <summary>鑑定費用（希少度から決まる。→ RelicBalance.GetAppraisalCost）。</summary>
        public int AppraisalCost { get; set; }
    }

    /// <summary>
    /// 未鑑定遺物の仮称プール（→ UnidentifiedItem.Name）。中身が分からないことを示す「？？？」を
    /// 頭に付けた、外見だけの呼び名を希少度ごとに用意する。
    ///
    /// バランス値ではなく演出用のフレーバーテキストのため、CSV（relic.csv）ではなくコード側に置く
    /// （→ 03 §10.1「CSVに出すのはバランス値だけ」。Systems.AppraisalSystem の鑑定コメントと同じ扱い）。
    /// </summary>
    public static class RelicNames
    {
        private static readonly string[] CommonNames =
        {
            "？？？ 苔むした封印箱", "？？？ 錆びついた留め具", "？？？ 泥にまみれた小袋", "？？？ 欠けた石板",
        };

        private static readonly string[] RareNames =
        {
            "？？？ 歪んだ古代装具", "？？？ 銀糸で綴じた革筒", "？？？ 印章付きの木箱", "？？？ 曇った硝子瓶",
        };

        private static readonly string[] EpicNames =
        {
            "？？？ 金環の施された遺物", "？？？ 脈打つ結晶核", "？？？ 意匠を凝らした宝函", "？？？ 文様の浮く祭器",
        };

        private static readonly string[] LegendaryNames =
        {
            "？？？ 虹色に燻る封印筐", "？？？ 銘の読めぬ古代兵装", "？？？ 時を止めた聖遺物", "？？？ 光を吸う黒匣",
        };

        /// <summary>希少度に応じた仮称を1つ選ぶ。indexは呼び出し側が引いた乱数（負値・範囲外も安全に丸める）。</summary>
        public static string Roll(ItemRarity rarity, int index)
        {
            var pool = rarity switch
            {
                ItemRarity.Legendary => LegendaryNames,
                ItemRarity.Epic => EpicNames,
                ItemRarity.Rare => RareNames,
                _ => CommonNames,
            };

            return pool[Math.Abs(index) % pool.Length];
        }
    }
}
