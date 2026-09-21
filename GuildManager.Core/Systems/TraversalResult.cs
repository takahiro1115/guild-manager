using System;
using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 道中進軍1回分の結果（→ DungeonTraversalResolver.Resolve）。調査任務の分岐A
    /// （→ 03 §4.5.2、大迷宮5フィールド拡張仕様）。UI側（Godot）はこれを読んで週報ログへ表示する
    /// （ScoutingResult・DungeonResultと同じ役割）。
    /// </summary>
    public class TraversalResult
    {
        /// <summary>今回の進軍ランク（走破力Ratioから決まる）。</summary>
        public TraversalRank Rank { get; set; }

        /// <summary>進軍前の到達階層。</summary>
        public int FloorBefore { get; set; }

        /// <summary>進軍後の到達階層（ストッパーでクランプされた場合はその階層）。</summary>
        public int FloorAfter { get; set; }

        /// <summary>
        /// ストッパーが発動したか（＝走破力が十分でも、次の未撃破ボス階層を超えて
        /// 進めなかった）。trueの場合、TargetBossに発見したボスが入る。
        /// </summary>
        public bool StopperTriggered { get; set; }

        /// <summary>ストッパー発動時に発見した（まだ倒していない）ボス。それ以外はnull。</summary>
        public FloorBoss? TargetBoss { get; set; }

        /// <summary>冒険者IDごとの、今回の進軍で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();

        /// <summary>
        /// 進軍開始時点の区間担当ボスの解析率から求めた走破倍率（1.0〜3.0、→ DungeonTraversalResolver.IntelSpeedMultiplier）。
        /// 週報・UIの表示用。実際の進軍は1階層ごとに区間の倍率を引き直す。
        /// </summary>
        public double IntelSpeedMultiplier { get; set; } = 1.0;

        /// <summary>
        /// 今回の進軍に未踏破階層（進軍前のフィールドの最高到達階層より深い階層）が含まれていたか。
        /// trueならHP消費の基礎が未踏破の重損耗（→ DungeonBalance.UnexploredHpLossPct*）になる。
        /// </summary>
        public bool EnteredUnexplored { get; set; }

        /// <summary>今回の進軍に適用した被ダメージ倍率（歩いた階層の平均。完全解析区間のみなら0.3）。</summary>
        public double DamageTakenMultiplier { get; set; } = 1.0;

        /// <summary>
        /// 参謀のルート指導による走破力スコアへの加算値（→ AdvisorSystem.GetAdvisorTraversalPowerBonus）。
        /// 未任命なら0。走破力（→ DungeonTraversalResolver.CalculateTraversalScore）には既に含まれている。
        /// </summary>
        public double AdvisorTraversalBonus { get; set; }

        /// <summary>支援した参謀の名前（週報表示用）。未任命・ボーナス0ならnull。</summary>
        public string? AdvisorName { get; set; }

        /// <summary>今回の進軍で拾ったゴールド（部隊が持ち歩き、帰還時にギルドへ格納される）。</summary>
        public int LootGold { get; set; }

        /// <summary>今回の進軍で拾った素材（素材Id→個数）。帰還時にギルドへ格納される。</summary>
        public Dictionary<string, int> LootMaterials { get; set; } = new();
    }
}
