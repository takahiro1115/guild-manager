using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 教官からの週次特性伝授（TrainingSystem.ProcessWeeklyTraitTransmission）で
    /// 実際に伝授が成立した1件の記録。UI側（Godot）はこれを見て週報ログに表示する想定
    /// （GrowthEventと同じパターン）。
    /// </summary>
    public class TraitTransmissionEvent
    {
        public Adventurer Student { get; }
        public Adventurer Trainer { get; }
        public string TraitId { get; }

        public TraitTransmissionEvent(Adventurer student, Adventurer trainer, string traitId)
        {
            Student = student;
            Trainer = trainer;
            TraitId = traitId;
        }

        /// <summary>
        /// 週報の1行（BBCodeなし、→ 03 §7.1・§0.19 完全開示、2026年9月・§0.34）。
        /// 例：「【奥義継承】教官クラウディアの指導により、リナは特性『豪胆』を会得した！」
        /// </summary>
        public string ToLogText() =>
            $"【奥義継承】教官{Trainer.Name}の指導により、{Student.Name}は特性『{TraitCatalog.FindById(TraitId)?.DisplayName ?? TraitId}』を会得した！";
    }
}
