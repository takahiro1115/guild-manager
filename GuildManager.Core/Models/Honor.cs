using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    // 戦績・称号・殿堂・引退式と観測日誌（2026年10月・大会と育成の栄光 段2、→ docs/検討中_大会と育成の栄光.md §4.1）。

    /// <summary>ボス撃破の記録1件（撃破した部隊の生還者に残す）。</summary>
    public sealed class BossKillRecord
    {
        /// <summary>撃破した週（GameState.WeekNumber）。</summary>
        public int Week { get; set; }
        public Guid BossId { get; set; }
        public string FieldId { get; set; } = "";
        public string FieldName { get; set; } = "";
        public int Floor { get; set; }
        public string BossName { get; set; } = "";
        /// <summary>主役（部隊で討伐火力 DungeonPowerCalculator.MemberPower が最も高かった者）か。</summary>
        public bool IsMvp { get; set; }
    }

    /// <summary>観測日誌（年表）の種類。重みは HonorSystem.Weight（引退式に載せる出来事を選ぶのに使う）。</summary>
    public enum JournalKind
    {
        Joined,
        FirstPlacing,
        FirstWin,
        BigWin,
        Title,
        SevereInjury,
        Recovered,
        BossMvp,
        DaughterBorn,
        MotherDaughterSquad,
        HallOfFame,
        Retired,
    }

    /// <summary>観測日誌の1行（アルベールの記録）。</summary>
    public sealed class JournalEntry
    {
        public int Week { get; set; }
        public JournalKind Kind { get; set; }
        public string Text { get; set; } = "";
        /// <summary>関わった相手（娘・母など。無ければ null）。</summary>
        public Guid? RelatedId { get; set; }
    }

    /// <summary>称号の格（高いほど上）。</summary>
    public enum TitleRank
    {
        /// <summary>並。</summary>
        Plain,
        /// <summary>誉。</summary>
        Honor,
        /// <summary>名。</summary>
        Name,
        /// <summary>伝説。</summary>
        Legend,
    }

    /// <summary>称号の定義（→ docs/04_バランス表/titles.csv）。名前の {G1} は大会の名前で埋める。</summary>
    public sealed class TitleDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public TitleRank Rank { get; init; }
        /// <summary>条件の数値（回数など。使わない称号は0）。</summary>
        public int Param { get; init; }
    }

    /// <summary>冒険者が得ている称号1つ（記録から毎回計算する。セーブには持たない）。</summary>
    public sealed class EarnedTitle
    {
        /// <summary>称号の Id（大会ごとに分かれるものは「Id:大会Id」）。</summary>
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public TitleRank Rank { get; init; }
    }

    /// <summary>関係の深い相手1人（表示だけ。効果は無い）。</summary>
    public sealed class RelationInfo
    {
        public Guid OtherId { get; init; }
        public string OtherName { get; init; } = "";
        public int Compatibility { get; init; }
        /// <summary>関係タグ（戦友・名コンビ・母娘・師弟）。</summary>
        public List<string> Tags { get; init; } = new();
    }

    /// <summary>週の決算で起きた栄誉の知らせ（月報の主な出来事に出す）。</summary>
    public sealed class HonorNotice
    {
        public Guid AdventurerId { get; init; }
        public JournalKind Kind { get; init; }
        public string Text { get; init; } = "";
    }

    /// <summary>引退式の1枚（→ HonorSystem.BuildCeremony）。</summary>
    public sealed class RetirementCeremony
    {
        public Guid AdventurerId { get; init; }
        public string Name { get; init; } = "";
        public string? PortraitId { get; init; }
        public string Title { get; init; } = "";
        public int YearsActive { get; init; }
        /// <summary>加入時の見立て（例「B?」。記録が無ければ空）。</summary>
        public string JoinEstimate { get; init; } = "";
        /// <summary>本当の素質の段階（引退式で明かす）。</summary>
        public string TrueRank { get; init; } = "";
        /// <summary>ピークの能力（能力名→値）。</summary>
        public Dictionary<string, int> PeakStats { get; init; } = new();
        /// <summary>8年の歩み（年表から大きな出来事を選んで週の順に）。</summary>
        public List<JournalEntry> Highlights { get; init; } = new();
        /// <summary>勝ち鞍（優勝した大会の「年目 大会名」）。</summary>
        public List<string> Wins { get; init; } = new();
        public bool HallOfFame { get; init; }
        public string AlbertLine { get; init; } = "";
        /// <summary>記録が少ない者の短い版か。</summary>
        public bool IsShort { get; init; }
    }
}
