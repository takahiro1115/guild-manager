namespace GuildManager.Core.Models
{
    /// <summary>
    /// 階層ボスのギミック種別（→ ダンジョン攻略システム、03 §4.5.4）。
    ///
    /// 2026年10月・§0.68で、対策を「できた／できない」の離散判定から、部隊の**備え**（0〜1、→ DungeonResolver.Readiness）で
    /// 段階的に効く形へ改め、携行アイテムの対策口を撤去した。あわせて3種（再生・群れ・魅了）を足し、ギミックごとに
    /// 「何で負けるか」を変えた：
    ///  - 損耗が増える：猛毒（＋戦闘後の毒状態）・飛行
    ///  - 戦死の恐れ：即死級
    ///  - 火力が下がる：重装甲（§0.68で損耗型から変更）・群れ（後衛だけ）・魅了（一番火力の高い隊員が戦わない）
    ///  - 要求火力が上がる：再生
    ///
    /// 設計メモ：旧・通常クエストの環境ギミック（`EnvironmentTag`）とは独立した仕組み（旧体系は撤去済み、→ 03 §0.12・§0.13）。
    /// </summary>
    public enum BossGimmickType
    {
        /// <summary>猛毒：損耗が増え、備えが足りないと戦闘後に毒状態（全能力の割合低下）が残る。神官・学者／MND。</summary>
        Poison,

        /// <summary>重装甲：攻撃が通りにくく、部隊火力が下がる。魔導士・重戦士／STR。</summary>
        HeavyArmor,

        /// <summary>飛行：地上からの攻撃が届きにくく、損耗が増える。斥候・盗賊／DEX。</summary>
        Flying,

        /// <summary>即死級攻撃：備えが無ければ一撃で戦闘不能域まで追い込まれる。騎士・斥候／LDR。</summary>
        InstantKill,

        /// <summary>再生：削った端から傷が塞がり、要求火力が上がる。盗賊・魔導士／AGI（§0.68）。</summary>
        Regeneration,

        /// <summary>群れ（取り巻き）：前衛を抜けて後衛が襲われ、後衛の火力が下がり損耗も少し増える。重戦士・騎士／VIT（§0.68）。</summary>
        Swarm,

        /// <summary>魅了：部隊で一番火力の高い隊員が操られて戦わず、その隊員の損耗が大きい。神官・学者／INT（§0.68）。</summary>
        Charm,
    }

    /// <summary>ギミック種別の表示名など、Core と UI が共通で使う情報（→ BossGimmickType、§0.68）。</summary>
    public static class BossGimmickInfo
    {
        /// <summary>ギミックの短い名前（週報・見立て・伝説級の装備の説明に使う）。</summary>
        public static string Label(BossGimmickType type) => type switch
        {
            BossGimmickType.Poison => "猛毒",
            BossGimmickType.HeavyArmor => "重装甲",
            BossGimmickType.Flying => "飛行",
            BossGimmickType.InstantKill => "即死級",
            BossGimmickType.Regeneration => "再生",
            BossGimmickType.Swarm => "群れ",
            BossGimmickType.Charm => "魅了",
            _ => type.ToString(),
        };

        /// <summary>
        /// 対策の職業の候補（ギミックごとに2つ）。ボスごとにどちらが効くか（両方の場合もある）は
        /// BossGimmick.CounterRoles に入る（→ SampleData.CreateBossGimmicks）。7職がそれぞれ2回ずつ出番を持つ。
        /// </summary>
        public static JobClass[] RoleCandidates(BossGimmickType type) => type switch
        {
            BossGimmickType.Poison => new[] { JobClass.Cleric, JobClass.Scholar },
            BossGimmickType.HeavyArmor => new[] { JobClass.Mage, JobClass.Warrior },
            BossGimmickType.Flying => new[] { JobClass.Ranger, JobClass.Thief },
            BossGimmickType.InstantKill => new[] { JobClass.Knight, JobClass.Ranger },
            BossGimmickType.Regeneration => new[] { JobClass.Thief, JobClass.Mage },
            BossGimmickType.Swarm => new[] { JobClass.Warrior, JobClass.Knight },
            _ => new[] { JobClass.Cleric, JobClass.Scholar }, // Charm
        };

        /// <summary>対策の能力（部隊の合計で測る、ギミックごとに固定）。7種で7能力を1つずつ使う。</summary>
        public static string CounterStat(BossGimmickType type) => type switch
        {
            BossGimmickType.Poison => "MND",
            BossGimmickType.HeavyArmor => "STR",
            BossGimmickType.Flying => "DEX",
            BossGimmickType.InstantKill => "LDR",
            BossGimmickType.Regeneration => "AGI",
            BossGimmickType.Swarm => "VIT",
            _ => "INT", // Charm
        };
    }
}
