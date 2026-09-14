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
    }
}
