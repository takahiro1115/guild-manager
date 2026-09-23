using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// マスター（アルベール）の機嫌（→ GameState.MasterMood、03 §8.1・§8.1.1）。
    /// 旧・名声（Reputation）とギルド格付け（GuildRankSystem）の後継（2026年9月）。
    ///
    /// 上昇要因は大迷宮での成果（→ ProcessWeeklyMood）と未鑑定遺物の鑑定（→ ApplyAppraisal）。
    /// 成果が1件もない週は退屈して機嫌を損ねる（退屈減衰）。機嫌は内職売上の倍率を決め
    /// （→ GetSideJobMultiplier、EconomySystem.ProcessWeeklySideJobIncome）、0に達すると
    /// 副官解雇＝敗北になる（→ DefeatSystem）。値は常に 0〜100 にクランプする。
    /// </summary>
    public class MasterMoodSystem
    {
        /// <summary>
        /// 機嫌を delta だけ動かし（0〜100にクランプ）、実際に動いた量を返す。
        /// 週次決算以外の経路（鑑定・退職金不足）からも使うため static にしてある。
        /// </summary>
        public static int Adjust(GameState state, int delta)
        {
            int before = state.MasterMood;
            state.MasterMood = Math.Clamp(before + delta, MasterMoodBalance.Min, MasterMoodBalance.Max);
            return state.MasterMood - before;
        }

        /// <summary>機嫌の段階（上機嫌／平常／不機嫌／危機、→ master_mood.csv TierThreshold_*）。</summary>
        public static MasterMoodTier GetTier(int mood)
        {
            if (mood >= MasterMoodBalance.TierThresholdCheerful) return MasterMoodTier.Cheerful;
            if (mood >= MasterMoodBalance.TierThresholdNormal) return MasterMoodTier.Normal;
            if (mood >= MasterMoodBalance.TierThresholdGrumpy) return MasterMoodTier.Grumpy;
            return MasterMoodTier.Crisis;
        }

        /// <summary>機嫌の段階ごとの内職売上倍率（上機嫌1.5／平常1.0／不機嫌0.5／危機0.0）。</summary>
        public static double GetSideJobMultiplier(MasterMoodTier tier) => tier switch
        {
            MasterMoodTier.Cheerful => MasterMoodBalance.SideJobMultiplierCheerful,
            MasterMoodTier.Normal => MasterMoodBalance.SideJobMultiplierNormal,
            MasterMoodTier.Grumpy => MasterMoodBalance.SideJobMultiplierGrumpy,
            _ => MasterMoodBalance.SideJobMultiplierCrisis,
        };

        /// <summary>現在の機嫌に対する内職売上倍率。</summary>
        public static double GetSideJobMultiplier(int mood) => GetSideJobMultiplier(GetTier(mood));

        /// <summary>
        /// 未鑑定遺物1個の鑑定完了による機嫌上昇（→ AppraisalSystem.Appraise、週次決算を待たず即時）。
        /// 実際に動いた量を返す。鑑定は「大迷宮での成果」には数えない（退屈減衰は防がない）。
        /// </summary>
        public static int ApplyAppraisal(GameState state) => Adjust(state, MasterMoodBalance.AppraisalMoodGain);

        /// <summary>
        /// 強制除籍（ボス討伐等でHP0→不死薬による現場からの永久離脱）へのアルベールの激怒
        /// （→ DungeonExpeditionSystem、2026年9月新設）。除籍1名につき ForcedRetirementMoodLoss だけ
        /// 機嫌を下げ（下限0）、実際に動いた量（0以下）を返す。0に達すれば週次決算で副官解雇になる（→ DefeatSystem）。
        /// 週次決算の機嫌集計（→ ProcessWeeklyMood）が、解決結果（DungeonMissionResolution.MasterFuryMoodApplied）から
        /// 週報の内訳に載せる。
        /// </summary>
        public static int ApplyForcedRetirementFury(GameState state, int retiredCount) =>
            retiredCount <= 0 ? 0 : Adjust(state, -MasterMoodBalance.ForcedRetirementMoodLoss * retiredCount);

        /// <summary>強制除籍時のアルベールの激怒の台詞（週報の【アルベールの激怒】行）。</summary>
        public const string FuryLine = "「うちの子に何て無茶をさせたの！ あなた、自分が何をしたか分かっているの!?」";

        /// <summary>機嫌の段階ごとのアルベールの一言（週報の【マスターの機嫌】行の末尾に付ける）。</summary>
        public static string GetAlbertLine(MasterMoodTier tier) => tier switch
        {
            MasterMoodTier.Cheerful => "「ふふ、いいデータが届いたわ。今夜は気分がいいから調合も捗るわね」",
            MasterMoodTier.Normal => "「順調ね。次も期待しているわよ、副官」",
            MasterMoodTier.Grumpy => "「……はあ。退屈ね。薬の注文なんて放っておいて頂戴、気分じゃないの」",
            _ => "「ねえ副官、あなた本当に私の役に立っているのかしら？ 次はないと思いなさい」",
        };

        /// <summary>
        /// 週次決算での機嫌の変動（→ WeekProcessingSystem.ProcessWeek、大迷宮の解決直後に1回だけ呼ぶ）。
        ///
        /// 大迷宮での成果ごとに機嫌を動かす：
        ///  - 階層ボス撃破：+BossDefeatMoodGain（1体ごと）
        ///  - 道中潜行の新階層開拓：未踏破階層を進んだ数×PioneerMoodPerFloor
        ///  - 迷宮調査の帰還：護衛判定「余裕」「十分」で+SurveySuccessMoodGain、「不足」（潰走）で−SurveyRoutedMoodLoss
        ///    （「充足」は増減なし。解析は進むため成果には数える）
        ///  - 探索採取：素材を1個以上獲得して帰還で+GatheringMoodGain
        ///  - 扉前待機中の偵察：増減なし。護衛判定が「不足」でなければ成果に数える
        /// 成果（上記のうち潰走以外）が1件もなければ WeeksSinceLastGuildActivity を+1し、
        /// 機嫌を BoredomMoodDecay だけ下げる（退屈減衰）。成果が1件でもあれば0にリセットする。
        /// </summary>
        public MasterMoodReport ProcessWeeklyMood(GameState state, IEnumerable<DungeonMissionResolution> resolutions)
        {
            var list = resolutions.ToList();

            // 強制除籍への激怒（→ ApplyForcedRetirementFury）は大迷宮の解決中に既に反映済みのため、
            // 決算開始時点の機嫌へ戻して内訳に載せる（ここでは機嫌を二重に動かさない）。
            int furyApplied = list.Sum(r => r.MasterFuryMoodApplied);
            var report = new MasterMoodReport { MoodBefore = state.MasterMood - furyApplied };
            foreach (var r in list.Where(r => r.MasterFuryRetiredCount > 0))
            {
                report.Entries.Add(new MasterMoodEntry(
                    $"アルベールの激怒（除籍{r.MasterFuryRetiredCount}名×{MasterMoodBalance.ForcedRetirementMoodLoss}）",
                    -MasterMoodBalance.ForcedRetirementMoodLoss * r.MasterFuryRetiredCount, r.MasterFuryMoodApplied));
            }

            bool hadActivity = false;

            foreach (var r in list)
            {
                if (r.DungeonResult != null && r.DungeonResult.Outcome == DungeonOutcome.Victory)
                {
                    hadActivity = true;
                    Record(state, report, $"階層ボス撃破「{r.Boss?.Name}」", MasterMoodBalance.BossDefeatMoodGain);
                }

                if (r.TraversalResult != null && r.TraversalResult.UnexploredFloorsAdvanced > 0)
                {
                    hadActivity = true;
                    int floors = r.TraversalResult.UnexploredFloorsAdvanced;
                    Record(state, report, $"新階層開拓（{r.Field.Name} {floors}階層×{MasterMoodBalance.PioneerMoodPerFloor}）",
                        floors * MasterMoodBalance.PioneerMoodPerFloor);
                }

                if (r.ScoutingResult != null)
                {
                    var tier = r.ScoutingResult.GuardTier;
                    if (r.MissionType == DungeonMissionType.Survey)
                    {
                        if (tier is GuardTier.Abundant or GuardTier.Sufficient)
                        {
                            hadActivity = true;
                            Record(state, report, "迷宮調査の帰還", MasterMoodBalance.SurveySuccessMoodGain);
                        }
                        else if (tier == GuardTier.Marginal)
                        {
                            hadActivity = true;
                            Record(state, report, "迷宮調査の帰還（護衛充足）", 0);
                        }
                        else
                        {
                            Record(state, report, "迷宮調査の潰走", -MasterMoodBalance.SurveyRoutedMoodLoss);
                        }
                    }
                    else if (tier != GuardTier.Deficient)
                    {
                        hadActivity = true; // 扉前での偵察（増減なし）
                    }
                }

                if (r.GatheringResult != null && !string.IsNullOrEmpty(r.GatheringResult.MaterialId) && r.GatheringResult.MaterialCount >= 1)
                {
                    hadActivity = true;
                    Record(state, report, "探索採取の成功", MasterMoodBalance.GatheringMoodGain);
                }
            }

            if (hadActivity)
            {
                state.WeeksSinceLastGuildActivity = 0;
            }
            else
            {
                state.WeeksSinceLastGuildActivity++;
                report.Bored = true;
                Record(state, report, "大迷宮での成果なし（退屈減衰）", -MasterMoodBalance.BoredomMoodDecay);
            }

            report.WeeksSinceLastGuildActivity = state.WeeksSinceLastGuildActivity;
            report.MoodAfter = state.MasterMood;
            return report;
        }

        private static void Record(GameState state, MasterMoodReport report, string reason, int delta) =>
            report.Entries.Add(new MasterMoodEntry(reason, delta, Adjust(state, delta)));
    }

    /// <summary>機嫌の段階（→ MasterMoodSystem.GetTier）。</summary>
    public enum MasterMoodTier
    {
        /// <summary>危機（1〜19、0で副官解雇）：内職売上×0.0。</summary>
        Crisis,

        /// <summary>不機嫌（20〜49）：内職売上×0.5。</summary>
        Grumpy,

        /// <summary>平常（50〜79）：内職売上×1.0。</summary>
        Normal,

        /// <summary>上機嫌（80〜100）：内職売上×1.5。</summary>
        Cheerful,
    }

    /// <summary>機嫌の変動1件（Delta＝規定の変動量、Applied＝0〜100のクランプ後に実際に動いた量）。</summary>
    public record MasterMoodEntry(string Reason, int Delta, int Applied);

    /// <summary>1週分の機嫌の変動（→ MasterMoodSystem.ProcessWeeklyMood）。週報の開示用。</summary>
    public class MasterMoodReport
    {
        /// <summary>週次決算の開始時点の機嫌。</summary>
        public int MoodBefore { get; set; }

        /// <summary>変動後の機嫌（退職金不足の低下も含め、WeekProcessingSystem が決算の最後に更新する）。</summary>
        public int MoodAfter { get; set; }

        /// <summary>今週の変動の内訳。</summary>
        public List<MasterMoodEntry> Entries { get; } = new();

        /// <summary>成果ゼロで退屈減衰が発生したか。</summary>
        public bool Bored { get; set; }

        /// <summary>決算後の「最終活動成果からの経過週数」。</summary>
        public int WeeksSinceLastGuildActivity { get; set; }

        /// <summary>今週の変動量の合計。</summary>
        public int Delta => MoodAfter - MoodBefore;
    }
}
