using System;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 冒険者データモデル。仕様書 03 §2 参照。
    /// MVPでは加齢・PA（潜在能力）・性格・相性はまだ実装しない
    /// （→ docs/06_タスクリスト.md Phase 3 で追加予定）。
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

        // ---- 動的・コンディション属性。仕様書 03 §2.3 ----

        /// <summary>最大HP = END×2 + 50（仕様書 03 §2.3）。</summary>
        public int MaxHP => END * 2 + 50;

        public int CurrentHP { get; set; }
        public int Fatigue { get; set; } = 0;
        public int Satisfaction { get; set; } = 70;
        public InjurySeverity Injury { get; set; } = InjurySeverity.None;

        /// <summary>負傷が治るまでの残り週数。0ならNoneに戻る（→ 03 §3.6 負傷回復処理）。</summary>
        public int InjuryWeeksRemaining { get; set; } = 0;

        public int WeeklyWage { get; set; }

        /// <summary>出撃可能かどうか（重傷または過労なら不可）。</summary>
        public bool IsAvailable => Injury != InjurySeverity.Severe && Fatigue < 100;
    }
}
