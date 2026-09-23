namespace GuildManager.Core.Systems
{
    /// <summary>
    /// アルベールの市販薬・内職売上1回分（→ EconomySystem.ProcessWeeklySideJobIncome）。週報の計算内訳の開示用。
    /// BaseGold＝InitialBaseGold（economy.csv SideJobBaseAmount）＋ResearchBonus（内職強化研究の合計、
    /// → ResearchEffectType.SideBusinessGoldBonus）。FinalGold＝round(BaseGold×Multiplier)。
    /// </summary>
    public record SideJobIncome(int InitialBaseGold, int ResearchBonus, int Mood, MasterMoodTier Tier, double Multiplier, int FinalGold)
    {
        /// <summary>倍率を掛ける前の基本額（初期基本額＋研究による加算）。</summary>
        public int BaseGold => InitialBaseGold + ResearchBonus;
    }
}
