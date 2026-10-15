using System;
using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>階層ボス討伐の結末（→ DungeonResolver）。</summary>
    public enum DungeonOutcome
    {
        /// <summary>撃破：ボスのHPを削り切った。</summary>
        Victory,

        /// <summary>撤退：火力が足りず削り切れなかった（ボスのHPは回復し、次回は仕切り直し）。</summary>
        Retreat,
    }

    /// <summary>
    /// 階層ボス討伐1回分の結果（→ DungeonResolver.Resolve）。
    /// UI側（Godot）はこれを読んで週報ログ・決戦ログのステップ再生に使う。
    /// </summary>
    public class DungeonResult
    {
        public DungeonOutcome Outcome { get; set; }

        /// <summary>部隊の火力（完全解析ボーナス適用後）。</summary>
        public double PartyPower { get; set; }

        /// <summary>ボスを削り切るのに必要だった火力。</summary>
        public double RequiredPower { get; set; }

        /// <summary>決戦の勝率（§0.98、→ DungeonResolver.WinChance）。勝ち負けはこの勝率で抽選する。</summary>
        public double WinChance { get; set; } = 1.0;

        /// <summary>完全解析（→ IntelTier.Complete）による与ダメージ補正が乗ったか。</summary>
        public bool FullIntelBonusApplied { get; set; }

        /// <summary>被ダメージ倍率（1.0＝加算なし。迷宮の異変「瘴気」、§0.64。§0.138 でギミックの加算は無くした）。</summary>
        public double DamageMultiplier { get; set; } = 1.0;

        /// <summary>神官の加護で下げた損耗率（%ポイント、0＝神官なし。§0.72、週報の開示用）。</summary>
        public double ClericBlessingPct { get; set; }

        /// <summary>HP0から一命を取り留めた危機察知（→ TraitCatalog.SixthSense、§0.138：強制除籍の確率が半分）の冒険者ID（週報の開示用）。</summary>
        public List<Guid> SixthSenseAdventurerIds { get; set; } = new();

        /// <summary>巨獣狩り（→ TraitCatalog.GiantHunter）の上乗せが効いた冒険者ID（週報の開示用。§0.138 でどの番人にも）。</summary>
        public List<Guid> GiantHunterAdventurerIds { get; set; } = new();

        /// <summary>
        /// この戦闘で後天的に付いた特性（週報の開示用）：撤退時の重傷生還による古傷（→ CriticalInjury.RollOldWound）と、
        /// 番人の撃破時の巨獣狩りの開眼（→ DungeonResolver.RollGiantHunterAwakening、§0.35）。
        /// </summary>
        public List<TraitGrantEvent> TraitGrantEvents { get; set; } = new();

        /// <summary>冒険者IDごとの、今回の戦闘で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();

        /// <summary>この任務で負傷した隊員（→ CriticalInjury、週報の開示用。§0.53）。</summary>
        public List<InjuryEvent> InjuryEvents { get; set; } = new();

        /// <summary>
        /// HPが0になり、ギルド登録を強制抹消された冒険者ID。
        ///
        /// 世界観上の扱い（→ 01_コンセプト.md）：アルベールの秘薬により一命は取り留めるが、
        /// 「危ないじゃないか！」と激怒したマスターによって即座に登録を抹消され、
        /// 二度と冒険者として戻らない。**システム上は恒久的なロスト**であり、
        /// 現役ロースターから外れる点は戦死と同じ（呼び出し側で除籍処理を行う）。
        /// </summary>
        public HashSet<Guid> ForceRetiredAdventurerIds { get; set; } = new();

        /// <summary>
        /// HPが0になったが、強制除籍の判定（→ CombatBalance.FatalChancePercent、§0.125）を免れて HP1 で一命を取り留めた冒険者ID。
        /// 重傷になり、古傷のロールもある（週報・月報の開示用）。
        /// </summary>
        public List<Guid> NearDeathSurvivorIds { get; set; } = new();

        /// <summary>相棒のかばい合い（§0.101、→ DungeonResolver.ApplyHpLoss）。</summary>
        public List<GuardEvent> GuardEvents { get; set; } = new();
    }
}

namespace GuildManager.Core.Systems
{
    /// <summary>決戦で相棒が損耗を分け合ってかばった1件（§0.101）。Share は相棒が引き受けたHP。</summary>
    public sealed record GuardEvent(System.Guid GuardianId, string GuardianName, System.Guid ProtectedId, string ProtectedName, int Share);
}
