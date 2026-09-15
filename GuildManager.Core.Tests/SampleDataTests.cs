using GuildManager.Core.Data;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// MVP動作確認用の固定データ（SampleData）のテスト。
    /// 初期編成改訂仕様：固定初期メンバーは3名（前衛の重戦士・斥候、後衛の神官）のみとし、
    /// 残り3名は第1週のチュートリアル採用試験（→ RecruitmentSystem.
    /// IsTutorialRecruitmentWeek・RecruitmentBalance.TutorialCandidateCount）で
    /// プレイヤー自身が選抜契約することで、計6名体制になる。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class SampleDataTests
    {
        [Fact]
        public void SampleData_ShouldContainExactlyThreeAdventurers()
        {
            var adventurers = SampleData.CreateStarterAdventurers();

            Assert.Equal(3, adventurers.Count);
        }

        [Fact]
        public void SampleData_AllStarterAdventurers_AreFemale()
        {
            // 世界観設定（女性限定ギルド仕様）：初期ロースターは全員Gender.Female。
            var adventurers = SampleData.CreateStarterAdventurers();

            Assert.All(adventurers, a => Assert.Equal(Gender.Female, a.Gender));
        }
    }
}
