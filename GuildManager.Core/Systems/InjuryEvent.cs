using System;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大迷宮の任務で冒険者が負傷した記録（週報の開示用、→ CriticalInjury.TryInflictSevere・TryInflictLight、03 §4.3・§0.53）。
    /// </summary>
    public record InjuryEvent(Guid AdventurerId, string Name, InjurySeverity Severity, int Weeks);
}
