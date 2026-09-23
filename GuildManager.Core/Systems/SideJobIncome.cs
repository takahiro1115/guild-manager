namespace GuildManager.Core.Systems
{
    /// <summary>
    /// アルベールの市販薬・内職売上1回分（→ EconomySystem.ProcessWeeklySideJobIncome）。週報の計算内訳の開示用。
    /// FinalGold＝round(BaseGold×Multiplier)。
    /// </summary>
    public record SideJobIncome(int BaseGold, int Mood, MasterMoodTier Tier, double Multiplier, int FinalGold);
}
