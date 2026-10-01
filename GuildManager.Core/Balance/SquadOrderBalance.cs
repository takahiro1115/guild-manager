namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 部隊の方針（自動出撃、→ Systems.SquadOrderSystem、03 §4.0.3・§0.63）のバランス値。
    /// 値は docs/04_バランス表/squad_orders.csv から読む（→ 03 §10.1。フォールバックは持たない）。
    /// </summary>
    public static class SquadOrderBalance
    {
        private const string FileName = "squad_orders.csv";

        /// <summary>自動出撃する条件：部隊の全員のHPがこの割合（%）以上。満たさなければその週は静養して待つ。</summary>
        public static readonly int AutoDispatchMinHpPercent = BalanceData.GetInt(FileName, "AutoDispatchMinHpPercent");

        /// <summary>扉前で自動で挑む条件：部隊の全員のHPがこの割合（%）以上。</summary>
        public static readonly int AutoEngageMinHpPercent = BalanceData.GetInt(FileName, "AutoEngageMinHpPercent");

        /// <summary>扉前で自動で挑む条件：討伐火力が要求火力のこの倍率以上（1.0＝見立てで「撃破見込み」）。</summary>
        public static readonly double AutoEngagePowerMargin = BalanceData.GetDouble(FileName, "AutoEngagePowerMargin");
    }
}
