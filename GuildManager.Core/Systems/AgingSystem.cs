using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 加齢・稼働期間・満期引退モデル。仕様書 03 §3 参照。
    ///
    /// 毎週の決算処理で ProcessWeeklyAging を1回呼び出す想定
    /// （InjuryRecoverySystem.ProcessWeeklyRecovery と同様の使い方）。
    ///
    /// ■ 8年稼働・26歳満期引退モデルへの改訂（ユーザー承認済み）
    /// 旧モデル（15〜40歳・円熟期/限界期のフィジカル衰微あり・40歳強制引退）から、
    /// 以下へ変更した。世界観上の根拠はギルドマスター・アルベールの信念
    /// （「危険な冒険者は若く美しい全盛期のうちに引退させ、十分な退職金を持たせて
    /// 安全に自立させるべき」→ 01_コンセプト.md）。
    ///
    /// - **加齢による能力低下（衰微）は一切行わない。** 旧 ApplyDecline とそのバランス値
    ///   （MatureDecline*/LimitDecline*）は削除した。冒険者は衰えない代わりに、
    ///   短い稼働期間で必ずギルドを去る。
    /// - **26歳の年度末（48週目）で満期引退。** 退職金を支給して現役ロースターから外す。
    /// - **稼働週数（ActiveWeeks）を毎週記録する。** 退職金の功績加算とUI表示に使う。
    ///
    /// 時間の刻みは従来どおり**週単位**（48週＝1年）を維持する（月単位化はしない）。
    /// 「伸びる」側（実効値の成長）は GrowthSystem が引き続き担当する（→ 03 §3.1〜3.4）。
    /// </summary>
    public class AgingSystem
    {
        private const int WeeksPerYear = 48; // 仕様書 03 §1.2（構造値）

        private static readonly int RetirementAge = BalanceData.GetInt("aging.csv", "RetirementAge"); // 仕様書 03 §3.7

        /// <summary>満期稼働週数（8年＝384週）。退職金の算定・UI表示に使う（引退判定は年齢で行う）。</summary>
        public static readonly int MaxActiveWeeks = BalanceData.GetInt("aging.csv", "MaxActiveWeeks");

        private static readonly int SeveranceWeeks = EconomyBalance.SeveranceWeeks; // 退職金の基礎部分（仕様書 03 §7）

        private readonly IRng _rng;

        /// <summary>
        /// 乱数は現在このクラスでは使用しない（衰微の抽選が無くなったため）。
        /// 既存の呼び出し側（MainDashboard・各テスト）のシグネチャを壊さないために引き続き受け取る。
        /// 将来「引退勧告の受諾判定」（→ 03 §3.7）を実装する際の差し込み口でもある。
        /// </summary>
        public AgingSystem(IRng rng)
        {
            _rng = rng;
        }

        public void ProcessWeeklyAging(GameState state)
        {
            int weekOfYear = ((state.WeekNumber - 1) % WeeksPerYear) + 1;
            bool isYearEnd = weekOfYear == WeeksPerYear;

            // ToList()でスナップショットを取る：年度末の引退処理でstate.Adventurersから
            // 要素を取り除く（RetiredAdventurersへ移す）ため、foreach対象の生のリストを
            // 直接回すとコレクション変更例外になる。
            foreach (var adventurer in state.Adventurers.ToList())
            {
                if (adventurer.IsRetired)
                    continue;

                adventurer.ActiveWeeks++;

                if (isYearEnd)
                    AdvanceYear(state, adventurer);
            }
        }

        /// <summary>
        /// 年度末の加齢と満期判定。**先に加齢し、その結果が満期年齢に達していれば引退**させる。
        ///
        /// この順序が重要：18歳で加入した冒険者は年度末を8回迎えて26歳になるため、
        /// 「26歳になった年度末で引退」とすることで稼働期間がちょうど8年（384週）になる。
        /// 逆に「26歳の年度末で引退」（加齢を先に止める形）にすると、26歳のまま1年間
        /// 在籍することになり9年稼働になってしまう。
        /// </summary>
        private void AdvanceYear(GameState state, Adventurer adventurer)
        {
            adventurer.Age++;

            if (adventurer.Age >= RetirementAge)
                Retire(state, adventurer);
        }

        /// <summary>
        /// 早期引退（仕様書 03 §7「引退の経路」）。プレイヤーが任意のタイミングで
        /// 満期前の現役冒険者を引退させ、顧問候補にする。退職金支給等の処理は
        /// 満期引退（Retire）と共通のものを使う。既に引退済みなら何もしない。
        /// </summary>
        public void RetireVoluntarily(GameState state, Adventurer adventurer)
        {
            if (adventurer.IsRetired)
                return;

            Retire(state, adventurer);
        }

        /// <summary>
        /// 退職金の総額＝週給×基礎週数 ＋ 累積功績×功績係数（→ EconomyBalance）。
        /// 「危険な仕事をさせた分だけ手厚く送り出す」という方針の実装。
        /// public static にしてあるのは、UI側で引退前に支給予定額を提示できるようにするため。
        /// </summary>
        public static int CalculateSeverancePay(Adventurer adventurer) =>
            adventurer.WeeklyWage * SeveranceWeeks
            + (int)Math.Round(adventurer.TotalContributionScore * EconomyBalance.SeveranceContributionCoefficient);

        /// <summary>
        /// 引退処理の共通部分（仕様書 03 §3.7・§7）。退職金を支給し、現役ロースターから
        /// 引退済み一覧へ移す。訓練場に配置中だった場合はその枠も解放する。
        /// 満期引退（AdvanceYear経由）と早期引退（RetireVoluntarily）の両方から呼ばれる。
        ///
        /// 退職金を払いきれない場合も引退自体は成立させる（冒険者を人質に取らない）が、
        /// 「約束した退職金を用意できないギルド」として名声が下がる
        /// （→ EconomyBalance.SeveranceShortfallReputationPenalty）。
        /// </summary>
        private void Retire(GameState state, Adventurer adventurer)
        {
            adventurer.IsRetired = true;
            adventurer.RetiredAtAge = adventurer.Age;
            adventurer.RetiredAtWeek = state.WeekNumber;

            int severance = CalculateSeverancePay(adventurer);
            bool canAfford = state.Gold >= severance;

            state.Gold -= severance;
            adventurer.SeverancePaid = true;

            if (!canAfford)
                state.Reputation = Math.Max(0, state.Reputation - EconomyBalance.SeveranceShortfallReputationPenalty);

            state.TrainingAssignments.Remove(adventurer.Id); // 訓練場配置からも外れる（枠を解放）
            state.Adventurers.Remove(adventurer);
            state.RetiredAdventurers.Add(adventurer);
        }
    }
}
