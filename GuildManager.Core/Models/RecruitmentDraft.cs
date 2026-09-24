using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 第1週（ゲーム開始週）の「新春ドラフト」1回分（→ Systems.RecruitmentSystem.StartInitialDraft、
    /// 03 §2.4、2026年9月）。候補の契約金はすべて0G、採用は HiresRemaining 名まで。
    /// 残り0名になった時点でドラフトは終了（IsComplete）し、以後の採用は通常の新春採用試験
    /// （2年目以降の年度第1週、→ RecruitmentSystem.TryHire）だけになる。
    ///
    /// 開始直後のモーダルの中だけで完結する一時的な状態のため、セーブデータには載せない
    /// （ドラフトが終わるまでモーダルが閉じず、セーブ操作もできない）。
    /// </summary>
    public class RecruitmentDraft
    {
        /// <summary>まだ採用されていない候補（採用すると取り除かれる）。</summary>
        public List<RecruitmentOffer> Offers { get; }

        /// <summary>あと何名採用するか。</summary>
        public int HiresRemaining { get; internal set; }

        /// <summary>規定人数の採用を終えた（または候補が尽きた）か。</summary>
        public bool IsComplete => HiresRemaining <= 0 || Offers.Count == 0;

        public RecruitmentDraft(List<RecruitmentOffer> offers, int hiresRemaining)
        {
            Offers = offers;
            HiresRemaining = hiresRemaining;
        }
    }
}
