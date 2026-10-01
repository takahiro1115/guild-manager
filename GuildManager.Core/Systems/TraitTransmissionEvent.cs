using System.Collections.Generic;
using System.Linq;
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

        /// <summary>この伝授で克服した生まれつきの欠点のId（§0.56、→ Adventurer.FlawsOvercomeBy）。無ければ空。</summary>
        public IReadOnlyList<string> OvercomeTraitIds { get; }

        public TraitTransmissionEvent(Adventurer student, Adventurer trainer, string traitId, IReadOnlyList<string>? overcomeTraitIds = null)
        {
            Student = student;
            Trainer = trainer;
            TraitId = traitId;
            OvercomeTraitIds = overcomeTraitIds ?? new List<string>();
        }

        /// <summary>
        /// 週報の1行（BBCodeなし、→ 03 §7.1・§0.19 完全開示、2026年9月・§0.34）。
        /// 例：「【奥義継承】教官クラウディアの指導により、リナは特性『豪胆』を会得した！」
        /// 欠点を克服したとき（§0.56）：「…『豪胆』を会得し、『臆病』を克服した！」
        /// </summary>
        public string ToLogText()
        {
            string head = $"【奥義継承】教官{Trainer.Name}の指導により、{Student.Name}は特性『{Name(TraitId)}』を会得";
            return OvercomeTraitIds.Count == 0
                ? $"{head}した！"
                : $"{head}し、{string.Join("", OvercomeTraitIds.Select(id => $"『{Name(id)}』"))}を克服した！";
        }

        private static string Name(string traitId) => TraitCatalog.FindById(traitId)?.DisplayName ?? traitId;
    }
}
