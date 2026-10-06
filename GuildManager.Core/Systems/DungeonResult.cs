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

        /// <summary>部隊の火力（ギミック補正・完全解析ボーナス適用後）。</summary>
        public double PartyPower { get; set; }

        /// <summary>ボスを削り切るのに必要だった火力。</summary>
        public double RequiredPower { get; set; }

        /// <summary>完全解析（→ IntelTier.Complete）による与ダメージ補正が乗ったか。</summary>
        public bool FullIntelBonusApplied { get; set; }

        /// <summary>ギミックごとの部隊の備え（0〜1、→ DungeonResolver.Readiness、§0.68）。</summary>
        public Dictionary<BossGimmickType, double> Readiness { get; set; } = new();

        /// <summary>そのギミックへの備えの不足（1−備え）。ボスがその種類を持たなければ0。</summary>
        public double ShortfallOf(BossGimmickType type) => Readiness.TryGetValue(type, out var r) ? 1.0 - r : 0;

        /// <summary>万全の備え（備え1）で臨めたギミック。</summary>
        public List<BossGimmickType> CounteredGimmicks { get; set; } = new();

        /// <summary>備えが足りなかったギミック（一部・無策。罰が不足に応じて効いた）。</summary>
        public List<BossGimmickType> UncounteredGimmicks { get; set; } = new();

        /// <summary>備えの不足による被ダメージ倍率（1.0＝加算なし）。</summary>
        public double DamageMultiplier { get; set; } = 1.0;

        /// <summary>魅了に備えが足りず、操られた隊員（部隊で一番火力の高い隊員、§0.68）。魅了が無い・万全なら null。</summary>
        public Guid? CharmedAdventurerId { get; set; }

        /// <summary>猛毒に備えが足りず毒状態になった隊員と、その週数（§0.68、週報の開示用）。</summary>
        public Dictionary<Guid, int> PoisonWeeksByAdventurer { get; set; } = new();

        /// <summary>神官の加護で下げた損耗率（%ポイント、0＝神官なし。§0.72、週報の開示用）。</summary>
        public double ClericBlessingPct { get; set; }

        /// <summary>この戦闘で付いた毒状態の全能力の低下率（0＝毒なし）。</summary>
        public double PoisonStatPenalty { get; set; }

        /// <summary>耐毒体質（→ TraitCatalog.ResistPoison）で未対策の猛毒の被ダメージ加算を軽減したか（週報の開示用）。</summary>
        public bool ResistPoisonApplied { get; set; }

        /// <summary>鷹の目（→ TraitCatalog.HawkEye、§0.55）で未対策の飛行の被ダメージ加算を軽減したか（週報の開示用）。</summary>
        public bool HawkEyeApplied { get; set; }

        /// <summary>未対策の即死級を危機察知（→ TraitCatalog.SixthSense、§0.55）でしのいだ冒険者ID（週報の開示用）。</summary>
        public List<Guid> SixthSenseAdventurerIds { get; set; } = new();

        /// <summary>重装甲ボスに対して巨獣狩り（→ TraitCatalog.GiantHunter）の上乗せが効いた冒険者ID（週報の開示用）。</summary>
        public List<Guid> GiantHunterAdventurerIds { get; set; } = new();

        /// <summary>
        /// この戦闘で後天的に付いた特性（週報の開示用）：撤退時の重傷生還による古傷（→ CriticalInjury.RollOldWound）と、
        /// 重装甲ボス撃破時の巨獣狩りの開眼（→ DungeonResolver.RollGiantHunterAwakening、§0.35）。
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
    }
}
