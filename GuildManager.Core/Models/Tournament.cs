using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>大会の種類（2026年10月・§0.82、→ docs/検討中_大会と育成の栄光.md）。</summary>
    public enum TournamentKind
    {
        /// <summary>新人戦（毎年 春1の月。その年に加わった者だけ）。</summary>
        Rookie,
        /// <summary>地方大会（G3）。毎年の暦に置く（月と週は年ごとに変わる）。</summary>
        Local,
        /// <summary>王都大会（G2）。その部門のG1より前の月に置く。</summary>
        Royal,
        /// <summary>定例のG1（毎年同じ月・週）。</summary>
        Classic,
        /// <summary>王都最強決定戦（定例のG1。その年の成績上位だけ）。</summary>
        Final,
        /// <summary>招待・特別大会（条件を満たすと2か月後に置く）。</summary>
        Invite,
    }

    /// <summary>大会の部門。</summary>
    public enum TournamentDiscipline
    {
        /// <summary>剣：STR×1.0＋VIT×0.6＋AGI×0.4。</summary>
        Sword,
        /// <summary>魔：INT×1.0＋MND×0.8＋LDR×0.2。</summary>
        Magic,
        /// <summary>技：DEX×1.0＋AGI×0.8＋STR×0.2。</summary>
        Skill,
        /// <summary>部隊戦（迷宮踏破杯）：部隊の走破力。</summary>
        Party,
        /// <summary>出場者の得意な部門（新人戦・王都最強決定戦・御前試合）。</summary>
        Best,
        /// <summary>置くときに剣・魔・技から抽選する（招待大会）。</summary>
        Random,
    }

    /// <summary>大会の格。</summary>
    public enum TournamentGrade
    {
        G3,
        G2,
        G1,
        /// <summary>招待・特別大会（施設のご褒美ではG2として数える）。</summary>
        Special,
    }

    /// <summary>大会の月の過ごし方（ユーザー判断：休養か追い込みを選ぶ）。</summary>
    public enum TournamentPrep
    {
        /// <summary>休養：HPの回復が静養の RestRecoveryMultiplier 倍。</summary>
        Rest,
        /// <summary>追い込み：部門の能力に訓練所と同じ成長の判定。HPを毎週 PushHpCost 使う。</summary>
        Push,
    }

    /// <summary>大会の定義1件（→ docs/04_バランス表/tournaments.csv、Balance.TournamentBalance）。</summary>
    public sealed class TournamentDefinition
    {
        public string Id { get; init; } = "";
        /// <summary>名前（{地方}・{フィールド} は置くときに埋める）。</summary>
        public string Name { get; init; } = "";
        public TournamentKind Kind { get; init; }
        public TournamentDiscipline Discipline { get; init; }
        public TournamentGrade Grade { get; init; }
        /// <summary>定例のとき：年の中の月（1〜12）。0＝暦に置くときに決める。</summary>
        public int Month { get; init; }
        /// <summary>月の中の週（1〜4）。0＝暦に置くときに決める。</summary>
        public int Week { get; init; }
        public double MinStrength { get; init; }
        public double MaxStrength { get; init; }
        public int Prize1 { get; init; }
        public int Prize2 { get; init; }
        public int Prize4 { get; init; }
        public int Mood1 { get; init; }
        public int Mood2 { get; init; }
        public int Mood4 { get; init; }
    }

    /// <summary>暦に置いた大会1つ（その年の1回分）。結果が出たら Result に入る。</summary>
    public sealed class TournamentEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string DefinitionId { get; set; } = "";
        public string Name { get; set; } = "";
        public TournamentKind Kind { get; set; }
        /// <summary>この回の部門（Random は置くときに決める。Best は出場者ごとに決まる）。</summary>
        public TournamentDiscipline Discipline { get; set; }
        public TournamentGrade Grade { get; set; }
        public int Year { get; set; }
        /// <summary>年の中の月（1〜12）。</summary>
        public int Month { get; set; }
        /// <summary>月の中の週（1〜4）。この週の決算で行う。</summary>
        public int Week { get; set; }
        /// <summary>招待大会のとき、招待された冒険者（null＝ギルドの誰でも）。</summary>
        public Guid? InvitedAdventurerId { get; set; }
        /// <summary>招待大会のご褒美の種類（"unique"＝固有武具、"halfcost"＝次の改築費が半額、""＝なし）。</summary>
        public string SpecialReward { get; set; } = "";
        /// <summary>結果（まだなら null）。</summary>
        public TournamentResult? Result { get; set; }
    }

    /// <summary>今月の出場1件（個人なら AdventurerId、部隊戦なら PartyId）。</summary>
    public sealed class TournamentEntry
    {
        public Guid EventId { get; set; }
        public Guid? AdventurerId { get; set; }
        public Guid? PartyId { get; set; }
        public TournamentPrep Prep { get; set; } = TournamentPrep.Rest;
    }

    /// <summary>トーナメントの1試合。</summary>
    public sealed class TournamentMatch
    {
        /// <summary>回戦（1＝1回戦。8人なら3が決勝）。</summary>
        public int Round { get; set; }
        public string NameA { get; set; } = "";
        public string NameB { get; set; } = "";
        public bool OursA { get; set; }
        public bool OursB { get; set; }
        public double StrengthA { get; set; }
        public double StrengthB { get; set; }
        public bool AWon { get; set; }
    }

    /// <summary>大会の結果1件：順位（ギルドの出場者分）と試合の一覧。</summary>
    public sealed class TournamentResult
    {
        public string WinnerName { get; set; } = "";
        public bool WinnerIsOurs { get; set; }
        public List<TournamentMatch> Matches { get; set; } = new();
        /// <summary>ギルドの出場者の順位（名前・順位：1＝優勝、2＝準優勝、4＝ベスト4、8＝ベスト8）。</summary>
        public List<TournamentPlacing> Placings { get; set; } = new();
    }

    /// <summary>ギルドの出場者1人（1部隊）の順位とご褒美。</summary>
    public sealed class TournamentPlacing
    {
        public Guid? AdventurerId { get; set; }
        public Guid? PartyId { get; set; }
        public string Name { get; set; } = "";
        public TournamentDiscipline Discipline { get; set; }
        public int Placing { get; set; }
        public int Prize { get; set; }
        public int Mood { get; set; }
    }

    /// <summary>冒険者に残る大会の記録1件（勝ち鞍＝Placing が1、入賞＝4以内）。</summary>
    public sealed class TournamentRecord
    {
        public int Year { get; set; }
        public string DefinitionId { get; set; } = "";
        public string Name { get; set; } = "";
        public TournamentKind Kind { get; set; }
        public TournamentGrade Grade { get; set; }
        public TournamentDiscipline Discipline { get; set; }
        public int Placing { get; set; }
    }

    /// <summary>施設が開いたときの知らせ（演出の型と台詞つき）。</summary>
    public sealed class FacilityUnlockNotice
    {
        public FacilityType Facility { get; set; }
        public int Level { get; set; }
        /// <summary>"Albert"（アルベールのひらめき）・"Adjutant"（副官の提案）・"Royal"（王都からの褒賞）。</summary>
        public string Style { get; set; } = "";
        public string Line { get; set; } = "";
    }
}
