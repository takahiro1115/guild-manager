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

        /// <summary>40歳年度末で強制引退したか（仕様書 03 §3.7）。→ AgingSystem が設定する。</summary>
        public bool IsRetired { get; set; } = false;

        // ---- レベルアップ制度（仕様書 03 §3.8）。加齢による成長・衰微（AgingSystem）とは独立した、
        // クエスト参加による成長経路。→ LevelingSystem が更新する。 ----
        public int Level { get; set; } = 1;
        public int Experience { get; set; } = 0;

        // ---- 動的・コンディション属性。仕様書 03 §2.3 ----

        /// <summary>最大HP = END×2 + 50（仕様書 03 §2.3）。</summary>
        public int MaxHP => END * 2 + 50;

        public int CurrentHP { get; set; }
        public int Satisfaction { get; set; } = 70;
        public InjurySeverity Injury { get; set; } = InjurySeverity.None;

        /// <summary>負傷が治るまでの残り週数。0ならNoneに戻る（→ 03 §3.6 負傷回復処理）。</summary>
        public int InjuryWeeksRemaining { get; set; } = 0;

        public int WeeklyWage { get; set; }

        /// <summary>出撃可能かどうか（重傷・引退済みなら不可）。疲労（Fatigue）は廃止済み（→ 03 §3.5改）。</summary>
        public bool IsAvailable => Injury != InjurySeverity.Severe && !IsRetired;
    }
}
