namespace GuildManager.Core.Models
{
    /// <summary>
    /// 新春採用試験（仕様書 03 §2.4）で提示される応募1件。
    /// 契約金（SigningBonus）は採用が確定するまでの一時的な値のため、
    /// 雇用後は不要になる Adventurer 本体ではなくここで保持する。
    /// </summary>
    public class RecruitmentOffer
    {
        public Adventurer Candidate { get; }

        /// <summary>契約時の一時金。→ BAL: 採用/契約金。</summary>
        public int SigningBonus { get; }

        /// <summary>古王の寝所（§0.130）から来た、名のある家の娘（有望新人）。</summary>
        public bool IsNobleDaughter { get; init; }

        public RecruitmentOffer(Adventurer candidate, int signingBonus)
        {
            Candidate = candidate;
            SigningBonus = signingBonus;
        }
    }
}
