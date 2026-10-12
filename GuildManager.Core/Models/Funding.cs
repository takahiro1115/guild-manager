using System;

namespace GuildManager.Core.Models
{
    /// <summary>融資の貸し手（段4e、§0.104）。</summary>
    public enum LoanLender
    {
        /// <summary>商人ギルド：いつでも借りられるが、手数料が高く、返し終わるまでマスターの機嫌が下がる。</summary>
        Merchant,
        /// <summary>イザベラ（白百合の杖）：来訪のあと、年に1回。手数料が安い。</summary>
        Isabella,
    }

    /// <summary>
    /// 借りているお金1口（→ GameState.Loans、Systems.FundingSystem）。月の最後の週に MonthlyPayment ずつ返す
    /// （払えなければ所持金がマイナスになり、破産の数えに入る）。返し終わったら一覧から外す。
    /// </summary>
    public class GuildLoan
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public LoanLender Lender { get; set; }
        /// <summary>借りた額。</summary>
        public int Principal { get; set; }
        /// <summary>返す額の合計（借りた額＋手数料）。</summary>
        public int TotalDue { get; set; }
        /// <summary>まだ返していない額。</summary>
        public int Remaining { get; set; }
        /// <summary>月々の返済額（最後の月は残りだけ）。</summary>
        public int MonthlyPayment { get; set; }
        /// <summary>借りた週。</summary>
        public int TakenWeek { get; set; }
    }

    /// <summary>スポンサー契約のノルマの種類（§0.104）。</summary>
    public enum SponsorQuota
    {
        /// <summary>まだ倒していないボスを倒す（どのフィールドでもよい）。</summary>
        Bosses,
        /// <summary>大会で入賞する（ベスト4以上）。</summary>
        Placings,
        /// <summary>依頼を達成する。</summary>
        Commissions,
    }

    /// <summary>
    /// スポンサー契約1件（→ GameState.SponsorContracts、Systems.FundingSystem、§0.104）。
    /// 春と秋のはじめに届き（Signed=false）、結ぶと前金が入る（Signed=true）。ノルマに届けば成功報酬、期限までに届かなければ違約金。
    /// 終わった契約・結ばずに流れた話は一覧から外す。
    /// </summary>
    public class SponsorContract
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string SponsorName { get; set; } = "";
        public SponsorQuota Quota { get; set; }
        /// <summary>強気な話（前金もノルマも大きい）。</summary>
        public bool Bold { get; set; }
        public int Target { get; set; }
        /// <summary>結んだときの数え（ノルマの進み＝今の数え−これ）。</summary>
        public int Baseline { get; set; }
        public int Advance { get; set; }
        public int Bonus { get; set; }
        public int Penalty { get; set; }
        public int OfferedWeek { get; set; }
        /// <summary>期限の週（この週の決算までにノルマに届けばよい）。</summary>
        public int DeadlineWeek { get; set; }
        public bool Signed { get; set; }
    }
}
