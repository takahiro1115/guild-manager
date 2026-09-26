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

        /// <summary>対策できていたギミック。</summary>
        public List<BossGimmickType> CounteredGimmicks { get; set; } = new();

        /// <summary>対策できずに踏んだギミック（被害が跳ね上がる原因）。</summary>
        public List<BossGimmickType> UncounteredGimmicks { get; set; } = new();

        /// <summary>未対策ギミックによる被ダメージ倍率（1.0＝すべて対策済み）。</summary>
        public double DamageMultiplier { get; set; } = 1.0;

        /// <summary>耐毒体質（→ TraitCatalog.ResistPoison）で未対策の猛毒の被ダメージ加算を軽減したか（週報の開示用）。</summary>
        public bool ResistPoisonApplied { get; set; }

        /// <summary>重装甲ボスに対して巨獣狩り（→ TraitCatalog.GiantHunter）の上乗せが効いた冒険者ID（週報の開示用）。</summary>
        public List<Guid> GiantHunterAdventurerIds { get; set; } = new();

        /// <summary>冒険者IDごとの、今回の戦闘で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();

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
