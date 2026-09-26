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

        /// <summary>
        /// 基礎進軍階層数（＝移動予算。→ DungeonTraversalResolver.CalculateBaseFloors：max(1, floor(Ratio×FloorsPerRatio))、
        /// 上限なし。2026年9月、リニア進軍モデル）。実際に進んだ階層数は区間の解析倍率とストッパーで決まる（→ FloorAfter−FloorBefore）。
        /// </summary>
        public int BaseFloors { get; set; }

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
        /// 進軍全体の実効平均走破倍率（1.0〜3.0、→ DungeonTraversalResolver.IntelSpeedMultiplier）。
        /// 歩いた各階層の区間倍率を階層数で重み付けした平均（区間をまたいだ場合は各区間の倍率が混ざる）。
        /// 1階層も進めなかった場合は出発区間の倍率。週報・UIの表示用。
        /// </summary>
        public double IntelSpeedMultiplier { get; set; } = 1.0;

        /// <summary>
        /// 今回の進軍に未踏破階層（進軍前のフィールドの最高到達階層より深い階層）が含まれていたか。
        /// trueなら未踏破の階層ぶんだけ、HP消費の基礎が未踏破の重損耗（→ DungeonBalance.UnexploredHpLossPct*）になる。
        /// </summary>
        public bool EnteredUnexplored { get; set; }

        /// <summary>
        /// 夜目（→ TraitCatalog.NightVision）で未踏破階層の損耗が軽減されたか（未踏破に踏み込み、かつ部隊に保有者がいた）。
        /// 週報の開示用（→ 03 §4.5.3）。
        /// </summary>
        public bool NightVisionApplied { get; set; }

        /// <summary>
        /// 今回の進軍で踏み入れた未踏破階層の数（＝新階層開拓。→ MasterMoodSystem の機嫌上昇、2026年9月新設）。
        /// </summary>
        public int UnexploredFloorsAdvanced { get; set; }

        /// <summary>
        /// 歩いた階層の被ダメージ倍率の平均（完全解析区間のみなら0.3）。表示用の参考値であり、
        /// HP損耗の計算には使わない（損耗は階層ごとに積み上げる、→ Segments・EffectiveLossPct）。
        /// </summary>
        public double DamageTakenMultiplier { get; set; } = 1.0;

        /// <summary>走破力スコア（→ DungeonTraversalResolver.CalculateTraversalScore）。判定内訳の開示用。</summary>
        public double TraversalScore { get; set; }

        /// <summary>出発階層の要求値（→ DungeonTraversalResolver.FloorRequirement）。</summary>
        public double Requirement { get; set; }

        /// <summary>走破力÷要求値（→ 進軍ランクの判定に使ったRatio）。</summary>
        public double Ratio { get; set; }

        /// <summary>既踏階層の基礎損耗率（進軍ランクの率、部隊平均の%）。既踏階層を歩かなかった場合は0。</summary>
        public double RankLossPct { get; set; }

        /// <summary>未踏破階層の基礎損耗率（重損耗の率、部隊平均の%）。未踏破階層を歩かなかった場合は0。</summary>
        public double UnexploredLossPct { get; set; }

        /// <summary>
        /// 実効損耗率（部隊平均の%）＝Σ(区間の基礎損耗率×区間の被ダメージ倍率×区間の階層数)÷歩いた階層数。
        /// </summary>
        public double EffectiveLossPct { get; set; }

        /// <summary>冒険者IDごとの実効損耗率（%）。失ったHP＝floor(最大HP×この値÷100)。</summary>
        public Dictionary<Guid, double> EffectiveLossPctByAdventurer { get; set; } = new();

        /// <summary>
        /// 区間ごとの内訳（区間担当ボスと未踏破かどうかが変わるごとに1区間）。週報の損耗内訳表示用。
        /// </summary>
        public List<TraversalSegmentDetail> Segments { get; set; } = new();

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
