using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 部隊の方針（自動出撃、→ Systems.SquadOrderSystem、03 §4.0.3・§0.63）と扉前の構え（§0.68）のバランス値。
    /// 値は docs/04_バランス表/squad_orders.csv から読む（→ 03 §10.1。フォールバックは持たない）。
    /// </summary>
    public static class SquadOrderBalance
    {
        private const string FileName = "squad_orders.csv";

        /// <summary>自動出撃する条件：部隊の全員のHPがこの割合（%）以上。満たさなければその週は静養して待つ。</summary>
        public static readonly int AutoDispatchMinHpPercent = BalanceData.GetInt(FileName, "AutoDispatchMinHpPercent");

        /// <summary>扉前の構え1つ分の、挑む条件（→ SquadOrderSystem.JudgeEngage）。</summary>
        public sealed record StanceRule(double PowerMargin, int MinHpPercent, double MinReadiness, double MinInstantKillReadiness);

        private static StanceRule Load(string prefix) => new(
            BalanceData.GetDouble(FileName, $"{prefix}PowerMargin"),
            BalanceData.GetInt(FileName, $"{prefix}MinHpPercent"),
            BalanceData.GetDouble(FileName, $"{prefix}MinReadiness"),
            BalanceData.GetDouble(FileName, $"{prefix}MinInstantKillReadiness"));

        public static readonly StanceRule Cautious = Load("Cautious");
        public static readonly StanceRule Standard = Load("Standard");
        public static readonly StanceRule Bold = Load("Bold");

        /// <summary>構えの条件。</summary>
        public static StanceRule For(DoorStance stance) => stance switch
        {
            DoorStance.Cautious => Cautious,
            DoorStance.Bold => Bold,
            _ => Standard,
        };
    }
}
