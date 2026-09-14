using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 環境ギミックの対策達成度判定（GimmickEvaluator）のテスト。環境ギミック刷新仕様参照。
    /// gimmick.csvのMiasma行：stat=MND, threshold_partial=30, threshold_full=60,
    /// phase=Attrition, none=2.0/partial=1.5/full=1.0, counter_item=Antidote。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class GimmickEvaluatorTests
    {
        private static readonly List<string> NoItems = new();

        [Fact]
        public void Gimmick_Miasma_FullMitigation_WhenSingleHighStat()
        {
            // 高MND神官1名（MND=70）：単独でThresholdFull(60)以上に達し、2点＝Full。
            var cleric = new Adventurer { JobClass = JobClass.Cleric, MND = 70 };
            var members = new List<Adventurer> { cleric };

            var level = GimmickEvaluator.Evaluate(members, EnvironmentTag.Miasma, NoItems);

            Assert.Equal(GimmickMitigationLevel.Full, level);
            Assert.Equal(2, GimmickEvaluator.GetScore(members, EnvironmentTag.Miasma, NoItems));
        }

        [Fact]
        public void Gimmick_Miasma_FullMitigation_WhenTwoNovicesCombined()
        {
            // 低MND神官2名（各MND=35。単独ではThresholdFull(60)未満）の合算(70)でFullに達する。
            var novice1 = new Adventurer { JobClass = JobClass.Cleric, MND = 35 };
            var novice2 = new Adventurer { JobClass = JobClass.Cleric, MND = 35 };
            var members = new List<Adventurer> { novice1, novice2 };

            var level = GimmickEvaluator.Evaluate(members, EnvironmentTag.Miasma, NoItems);

            Assert.Equal(GimmickMitigationLevel.Full, level);
        }

        [Fact]
        public void Gimmick_Miasma_PartialMitigation_WithItemOnly()
        {
            // MND合算(20)はThresholdPartial(30)未満＝能力値からの点数は0。
            // 携行アイテム（解毒薬）のみで+1点＝Partial（一部充足）となる。
            var member = new Adventurer { JobClass = JobClass.Warrior, MND = 20 };
            var members = new List<Adventurer> { member };
            var items = new List<string> { ConsumableCatalog.AntidoteId };

            var level = GimmickEvaluator.Evaluate(members, EnvironmentTag.Miasma, items);

            Assert.Equal(GimmickMitigationLevel.Partial, level);

            // Partial（一部充足）はペナルティ半減：損耗（HP消費%）倍率が1.5倍に緩和される
            // （未充足＝2.0倍のところ、一部充足で1.5倍に緩和。→ gimmick.csv）。
            Assert.Equal(1.5, GimmickBalance.GetMultiplier(EnvironmentTag.Miasma, level));
        }

        [Fact]
        public void Gimmick_Miasma_NoneMitigation_WhenNoStatAndNoItem()
        {
            var member = new Adventurer { JobClass = JobClass.Warrior, MND = 0 };
            var members = new List<Adventurer> { member };

            var level = GimmickEvaluator.Evaluate(members, EnvironmentTag.Miasma, NoItems);

            Assert.Equal(GimmickMitigationLevel.None, level);
            Assert.Equal(2.0, GimmickBalance.GetMultiplier(EnvironmentTag.Miasma, level));
        }
    }
}
