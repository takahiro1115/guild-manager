using System;
using System.Collections.Generic;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 採用（新春採用試験）システム。仕様書 03 §2.4 参照。
    ///
    /// 年1回・新年（各年度の第1週）にのみ応募者が提示される。途中採用なし。
    ///
    /// 他システムに依存する部分は、そのシステムが未実装のため固定値／TODOフックで
    /// スタブしてある（ユーザー決定：「他システム依存部分は固定値/TODOフックでスタブして先に進む」）。
    ///  - 雇用枠の宿舎Lv連動（→ §6 施設・インフラ拡張、未実装）→ 固定8枠で代用
    ///  - 応募の質の格付け連動（→ §8.1 ギルド格付け、未実装）→ 格付けに依らない固定の生成分布
    ///  - スカウト顧問+25%（→ §7 顧問制度、未実装）→ 未反映
    ///
    /// 数値（応募者数・PA生成レンジ・契約金係数等）は本来 → BAL: 採用 に集約する値だが、
    /// 04_バランス表.xlsx はまだコードから読み込めない（Phase 4で外部化予定）ため、
    /// 他のSystemと同様に現状は仮値を定数として直書きする。
    /// </summary>
    public class RecruitmentSystem
    {
        private const int WeeksPerYear = 48; // 仕様書 03 §1.2

        // TODO(→ 03 §6 施設・インフラ拡張): 宿舎Lvに連動させる。現状は宿舎Lv1相当（8枠）で固定。
        private const int DefaultActiveSlotCap = 8;

        private const int CandidateCount = 5; // → BAL: 採用/応募者数。現状は仮値

        private const int MinCandidateAge = 15;
        private const int MaxCandidateAge = 18;

        // → BAL: 採用/年齢別PA補正。現状は仮値（15歳で+10、18歳で+0へ線形補間）。
        private const int YoungestAgePaBonus = 10;

        // → BAL: 採用（PA自体の生成レンジ）。現状は仮値。
        private const int MinGeneratedPa = 40;
        private const int MaxGeneratedPa = 80;

        // 実効値/PA の比率。15歳ほどPAから遠く（伸びしろ最大）、18歳ほどPAに近い（即戦力）
        // （→ 03 §2.4）。→ BAL: 採用。現状は仮値（15歳:30%実現 〜 18歳:70%実現）。
        private const double YoungestGrowthRatio = 0.3;
        private const double OldestGrowthRatio = 0.7;

        private const double SigningBonusCoefficient = 3.0; // → BAL: 採用/契約金。現状は仮値
        private const double WeeklyWageCoefficient = 0.6;   // → BAL: 経済/週給基準。現状は仮値

        private readonly IRng _rng;

        public RecruitmentSystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 新春採用試験の週（2年目以降・各年度の第1週）かどうか。
        /// 1年目（ゲーム開始時点の第1週）は初期メンバーが既に配備されているため対象外
        /// （→ 03 §2.4「初期メンバーは採用システム対象外」）。
        /// </summary>
        public bool IsRecruitmentWeek(int weekNumber)
        {
            if (weekNumber <= WeeksPerYear) return false; // 1年目は対象外
            return ((weekNumber - 1) % WeeksPerYear) + 1 == 1;
        }

        /// <summary>現役枠の上限（→ 03 §2.4「雇用枠」。施設Lv未実装のため固定値）。</summary>
        public int GetActiveSlotCap(GameState state) => DefaultActiveSlotCap;

        /// <summary>現役枠の空き数。引退済み（顧問化予定）の者は占有しない（→ 03 §2.4・§7）。</summary>
        public int GetOpenSlotCount(GameState state)
        {
            int activeCount = 0;
            foreach (var a in state.Adventurers)
                if (!a.IsRetired) activeCount++;

            return Math.Max(0, GetActiveSlotCap(state) - activeCount);
        }

        /// <summary>
        /// 今年の応募者一覧を生成する。応募者数自体は空き枠の有無に関わらず一定
        /// （空き枠の範囲でプレイヤーが選んで採用する。→ 03 §2.4「選抜」）。
        /// </summary>
        public List<RecruitmentOffer> GenerateCandidates()
        {
            var offers = new List<RecruitmentOffer>();
            for (int i = 0; i < CandidateCount; i++)
                offers.Add(GenerateOne(i));
            return offers;
        }

        /// <summary>
        /// 応募を採用する。空き枠が無い、または契約金が足りない場合は何もせず false を返す。
        /// </summary>
        public bool TryHire(GameState state, RecruitmentOffer offer)
        {
            if (GetOpenSlotCount(state) <= 0) return false;
            if (state.Gold < offer.SigningBonus) return false;

            state.Gold -= offer.SigningBonus;
            state.Adventurers.Add(offer.Candidate);
            return true;
        }

        private RecruitmentOffer GenerateOne(int index)
        {
            int age = _rng.NextInt(MinCandidateAge, MaxCandidateAge);

            // ageT: 0(15歳) 〜 1(18歳)
            double ageT = (double)(age - MinCandidateAge) / (MaxCandidateAge - MinCandidateAge);
            int ageBonus = (int)Math.Round(YoungestAgePaBonus * (1 - ageT));
            double growthRatio = YoungestGrowthRatio + (OldestGrowthRatio - YoungestGrowthRatio) * ageT;

            var candidate = new Adventurer
            {
                // 氏名ジェネレータは別タスク（→ docs/06_タスクリスト.md Phase 4）。ここでは仮の識別名。
                Name = $"新人応募者{(char)('A' + index)}",
                Age = age,
                JobClass = (JobClass)_rng.NextInt(0, 3),
            };

            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int pa = Math.Min(100, _rng.NextInt(MinGeneratedPa, MaxGeneratedPa) + ageBonus);
                int actual = Math.Max(1, (int)(pa * growthRatio));
                AdventurerStatAccessor.SetPa(candidate, stat, pa);
                AdventurerStatAccessor.SetStat(candidate, stat, actual);
            }

            candidate.CurrentHP = candidate.MaxHP;
            candidate.WeeklyWage = Math.Max(1, (int)(candidate.TotalPA * WeeklyWageCoefficient));

            int signingBonus = (int)(candidate.TotalPA * candidate.Age * SigningBonusCoefficient);

            return new RecruitmentOffer(candidate, signingBonus);
        }
    }
}
