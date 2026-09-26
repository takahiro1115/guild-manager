using System;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>障害特性が付いた原因（→ TraitGrantEvent）。週報の文面を分ける。</summary>
    public enum TraitGrantCause
    {
        /// <summary>重傷（HP1）での生還による後遺症（古傷。→ CriticalInjury.RollOldWound）。</summary>
        CriticalInjury,

        /// <summary>同じ部隊の仲間の強制除籍を目の当たりにした衝撃（トラウマ。→ CompatibilitySystem.ApplyDeathAftermath）。</summary>
        ComradeRetired,
    }

    /// <summary>
    /// 障害特性（古傷・トラウマ）が付いた出来事1件（→ 03 §5.3.2「週報での開示」、2026年9月・§0.33）。
    /// 5枠満杯で通常特性を侵食した場合は ErodedTraitId に消えた特性のIdが入る（→ Adventurer.TryAddCurseTrait）。
    /// 週報の文面（→ ToLogText）は Core 側で組み立て、テストで検証できるようにしている。
    /// </summary>
    public record TraitGrantEvent(Guid AdventurerId, string AdventurerName, string TraitId, string? ErodedTraitId, TraitGrantCause Cause)
    {
        /// <summary>
        /// 週報の1行（BBCodeなし）。例：
        /// 「【不可逆障害】リナ は重傷の後遺症により『古傷』を負った（特性枠満杯のため『勤勉』を忘却）」
        /// </summary>
        public string ToLogText()
        {
            string traitName = DisplayName(TraitId);
            string text = Cause switch
            {
                TraitGrantCause.CriticalInjury => $"【不可逆障害】{AdventurerName} は重傷の後遺症により『{traitName}』を負った",
                _ => $"【精神的打撃】{AdventurerName} は仲間除籍の衝撃により『{traitName}』を負った",
            };
            return ErodedTraitId == null ? text : $"{text}（特性枠満杯のため『{DisplayName(ErodedTraitId)}』を忘却）";
        }

        private static string DisplayName(string traitId) => TraitCatalog.FindById(traitId)?.DisplayName ?? traitId;
    }
}
