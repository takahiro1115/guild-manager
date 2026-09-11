using System;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 冒険者データモデル。仕様書 03 §2 参照。
    /// 性格・相性はまだ実装しない（→ docs/06_タスクリスト.md Phase 3 で追加予定）。
    /// </summary>
    public class Adventurer
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "名無し";
        public int Age { get; set; } = 18;
        public JobClass JobClass { get; set; }

        /// <summary>
        /// 配置（前衛/後衛）。仕様書 03 §4.2 参照。冒険者個人に紐づく永続状態で、
        /// クエストをまたいで保持される。生成時に PlacementRules.GetDefault(JobClass) で
        /// 職業に応じた初期値を設定する想定（→ SampleData・RecruitmentSystem）。
        /// 配置は職業で固定されない。どの職業でも自由に前衛/後衛を選べる
        /// （ユーザー決定：「魔法使いや僧侶も前衛になることができる。職業で固定になることはない」）。
        /// 役割から外れた配置のペナルティは個人CP補正（→ Balance/PlacementBalance）で表現する。
        /// </summary>
        public Placement Placement { get; set; } = Placement.Front;

        /// <summary>
        /// 配置を変更する。職業による制約は無いため常に成功するが、Party.TryAdd等の
        /// 既存の「Try」系メソッドと同じ形（bool戻り値）に揃えてある。
        /// </summary>
        public bool TrySetPlacement(Placement placement)
        {
            Placement = placement;
            return true;
        }

        // ---- 能力値（実効値 1〜100）。仕様書 03 §2.2 ----
        public int STR { get; set; }
        public int AGI { get; set; }
        public int END { get; set; }
        public int MAG { get; set; }
        public int SCT { get; set; }
        public int LDR { get; set; }

        // ---- 潜在能力 PA（各ステータスの成長上限。1〜100）。仕様書 03 §2.2 ----
        // 実効値はデフォルトでは0から始まるため、PAのデフォルトは範囲の最大値にしておき、
        // 明示的に潜在能力を絞らない限り既存の挙動（成長上限なし相当）を壊さないようにする。
        public int PA_STR { get; set; } = 100;
        public int PA_AGI { get; set; } = 100;
        public int PA_END { get; set; } = 100;
        public int PA_MAG { get; set; } = 100;
        public int PA_SCT { get; set; } = 100;
        public int PA_LDR { get; set; } = 100;

        /// <summary>総合PA＝6つのPAの平均（採用試験・スカウト評価用。仕様書 03 §2.2）。</summary>
        public double TotalPA => (PA_STR + PA_AGI + PA_END + PA_MAG + PA_SCT + PA_LDR) / 6.0;

        /// <summary>年齢帯（仕様書 03 §3.0 の定義表）。表の範囲外は近い側の帯に丸める。</summary>
        public AgeBand AgeBand =>
            Age <= 21 ? AgeBand.GrowthPeriod :
            Age <= 27 ? AgeBand.PrimePeriod :
            Age <= 34 ? AgeBand.MaturePeriod :
            AgeBand.LimitPeriod;

        /// <summary>40歳年度末で強制引退したか（仕様書 03 §3.7）。→ AgingSystem が設定する。
        /// 引退した冒険者は GameState.Adventurers から GameState.RetiredAdventurers へ移される。
        /// このフラグ自体は移動後も参照できるよう残す（防御的なガード・監査用）。</summary>
        public bool IsRetired { get; set; } = false;

        // ---- 引退記録（仕様書 03 §3.7つづき）。§7顧問制度が未実装の間の「引退済み・顧問候補」データ。 ----

        /// <summary>引退時点の年齢。強制引退は常に40だが、将来の任意引退等に備えて記録する。</summary>
        public int? RetiredAtAge { get; set; }

        /// <summary>引退した週番号（GameState.WeekNumber）。§7実装時の参考情報として保持。</summary>
        public int? RetiredAtWeek { get; set; }

        /// <summary>退職金（週給×12週。仕様書 03 §7）が支給済みか。</summary>
        public bool SeverancePaid { get; set; } = false;

        // ---- 動的・コンディション属性。仕様書 03 §2.3 ----

        /// <summary>最大HP = END×2 + 50（仕様書 03 §2.3）。</summary>
        public int MaxHP => END * 2 + 50;

        public int CurrentHP { get; set; }
        public int Satisfaction { get; set; } = 70;
        public InjurySeverity Injury { get; set; } = InjurySeverity.None;

        // ---- 満足度・契約交渉（仕様書 03 §5.1・§5.2）。→ SatisfactionSystem が更新する。 ----

        /// <summary>直近出撃してからの連続週数（出撃した週に0へリセット）。出場機会ペナルティ判定に使う（§5.1）。</summary>
        public int WeeksSinceLastDeployment { get; set; } = 0;

        /// <summary>満足度20未満で立つ交渉警告フラグ（§5.2「昇給要求」「移籍検討」）。</summary>
        public bool NeedsNegotiation { get; set; } = false;

        /// <summary>交渉警告が立ってから経過した週数。2週を超えて未対応だと契約解除される。</summary>
        public int NegotiationWeeksElapsed { get; set; } = 0;

        /// <summary>負傷が治るまでの残り週数。0ならNoneに戻る（→ 03 §3.6 負傷回復処理）。</summary>
        public int InjuryWeeksRemaining { get; set; } = 0;

        public int WeeklyWage { get; set; }

        /// <summary>出撃可能かどうか（重傷・引退済みなら不可）。疲労（Fatigue）は廃止済み（→ 03 §3.5改）。</summary>
        public bool IsAvailable => Injury != InjurySeverity.Severe && !IsRetired;
    }
}
