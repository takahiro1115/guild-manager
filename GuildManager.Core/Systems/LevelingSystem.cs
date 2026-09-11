using System;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// レベルアップ制度。仕様書 03 §3.8 参照。
    ///
    /// AgingSystem（加齢による成長・衰微、§3）とは独立した、クエスト参加による
    /// もう一つの成長経路。年齢帯に関わらず（全盛期以降でも）機能する。
    ///
    /// - クエストに参加した冒険者は経験値を得る。ただし今回の遠征でダウンした者は無報酬
    ///   （→ ユーザー決定：「ダウンした場合は無報酬とする」）。
    /// - 経験値が閾値に達するとレベルが上がり、ランダムな1ステータスの実効値とPA（潜在能力）が
    ///   両方とも伸びる。PA自体を伸ばす（＝伸びしろそのものを増やす）ことで、成長期を過ぎた
    ///   冒険者でも経験を積めば強くなれる。
    /// - レベルには上限がある（→ ユーザー決定：「レベル上限を設ける」）。
    /// - レベルアップで伸びたPA・実効値も、AgingSystemの恒久低下の対象から特別扱いしない
    ///   （→ ユーザー決定：「減衰期のマイナスはレベルで上がった分も減衰する」）。
    ///   AgingSystemはPA_STR等を直接減算するだけで由来を区別しないため、これは追加実装なしで
    ///   自然にそうなる。
    ///
    /// 経験値の必要量・レベル上限・レベルアップ時の上昇量は本来 → BAL: レベル に集約する数値だが、
    /// 04_バランス表.xlsx はまだコードから読み込めない（Phase 4で外部化予定）ため、
    /// 他のSystemと同様に現状は仮値を定数として直書きする。
    /// </summary>
    public class LevelingSystem
    {
        private const int MaxLevel = 20; // → BAL: レベル/上限。現状は仮値
        private const int StatGainPerLevel = 2; // → BAL: レベル/上昇量。現状は仮値
        private const int MaxStatValue = 100; // 仕様書 03 §2.2「実効値 1〜100」「PA 各1〜100」

        /// <summary>UI表示用のレベル上限（→ MaxLevel）。</summary>
        public static int MaxLevelValue => MaxLevel;

        private readonly IRng _rng;

        public LevelingSystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// クエスト解決の直後に呼び出す。ダウンしなかった参加者にだけ経験値を与え、レベルアップを処理する。
        /// 出撃しなかった週（休養）では呼ばない想定（参加＝経験値の前提）。
        /// </summary>
        public void AwardExperience(Party party, Quest quest, WeekResolutionResult result)
        {
            foreach (var member in party.Members)
            {
                if (result.DownedAdventurerIds.Contains(member.Id))
                    continue; // ダウンした場合は無報酬

                // → BAL: レベル/経験値獲得量。現状は仮値（クエスト難易度をそのまま採用）
                member.Experience += quest.Difficulty;

                while (member.Level < MaxLevel && member.Experience >= ExperienceToNextLevel(member.Level))
                {
                    member.Experience -= ExperienceToNextLevel(member.Level);
                    member.Level++;
                    ApplyLevelUpGrowth(member);
                }

                // 上限到達後は経験値を貯める意味がないため切り捨てる。
                if (member.Level >= MaxLevel)
                    member.Experience = 0;
            }
        }

        /// <summary>レベルNからN+1に必要な経験値。→ BAL: レベル/必要経験値。現状は仮値（レベル×50）。</summary>
        private static int ExperienceToNextLevel(int level) => level * 50;

        /// <summary>UI表示用：次のレベルに必要な経験値。上限レベルに達している場合は0を返す。</summary>
        public static int GetExperienceRequiredForNextLevel(int level) =>
            level >= MaxLevel ? 0 : ExperienceToNextLevel(level);

        private void ApplyLevelUpGrowth(Adventurer adventurer)
        {
            string stat = AdventurerStatAccessor.AllStatNames[_rng.NextInt(0, AdventurerStatAccessor.AllStatNames.Length - 1)];

            int newActual = Math.Min(MaxStatValue, AdventurerStatAccessor.GetStat(adventurer, stat) + StatGainPerLevel);
            int newPa = Math.Min(MaxStatValue, AdventurerStatAccessor.GetPa(adventurer, stat) + StatGainPerLevel);
            AdventurerStatAccessor.SetStat(adventurer, stat, newActual);
            AdventurerStatAccessor.SetPa(adventurer, stat, newPa);
        }
    }
}
