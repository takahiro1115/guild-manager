namespace GuildManager.Core.Models
{
    /// <summary>
    /// 階層ボスが持つギミック1件（→ ダンジョン攻略システム）。
    ///
    /// 対策の判定は「いずれかを満たせば対策済み」というOR条件にする
    /// （→ Systems.DungeonResolver）。3つの対策口（ロール・ステータス・アイテム）を
    /// すべて必須にすると、編成の自由度が失われて「唯一解のパズル」になってしまうため。
    ///
    /// どの対策口を使うかはギミック種別ごとにボス定義側で決める（未設定の対策口は
    /// 判定に参加しない）。
    /// </summary>
    public class BossGimmick
    {
        public BossGimmickType Type { get; set; }

        /// <summary>対策になる職業。null＝職業による対策口は無い。</summary>
        public JobClass? RequiredCounterRole { get; set; }

        /// <summary>
        /// 対策になるステータス名（"MND"等。→ AdventurerStatAccessor）。null＝ステータスによる対策口は無い。
        /// パーティ合算値が RequiredCounterStatThreshold 以上なら対策成立。
        /// </summary>
        public string? RequiredCounterStat { get; set; }

        /// <summary>RequiredCounterStat の必要合算値。</summary>
        public double RequiredCounterStatThreshold { get; set; }

        /// <summary>対策になる携行アイテムのId（→ ConsumableCatalog）。null＝アイテムによる対策口は無い。</summary>
        public string? RequiredItemId { get; set; }

        /// <summary>
        /// 未対策時の危険度（1〜）。被ダメージ倍率の算出に使う
        /// （→ DungeonBalance.UncounteredDamageMultiplierPerDangerLevel）。
        /// </summary>
        public int DangerLevel { get; set; } = 1;
    }
}
