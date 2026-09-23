using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 加齢・衰微モデル（仕様書 03 §3）のテスト。「伸びる」側（成長トリガー）は
    /// GrowthSystem へ移動したため GrowthSystemTests でカバーする。
    /// 実行方法: このフォルダで `dotnet test`
    ///
    /// AlwaysMinRng で乱数を「常に範囲の下限」に固定し、衰微対象の抽選・
    /// 低下量ロールをすべて決定的にしてから境界を検証する。
    /// </summary>
    public class AgingSystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static GameState CreateState(Adventurer adventurer, int weekNumber) =>
            new GameState { WeekNumber = weekNumber, Adventurers = { adventurer } };

        // ---------------- AgeBand 境界（仕様書 03 §3.0） ----------------

        [Theory]
        [InlineData(15, AgeBand.Young)]   // 旧モデルの下限。18歳以下はすべて新鋭期へ丸める
        [InlineData(18, AgeBand.Young)]   // 8年稼働モデルの加入年齢（固定）
        [InlineData(19, AgeBand.Growing)]
        [InlineData(22, AgeBand.Growing)]
        [InlineData(23, AgeBand.Peak)]
        [InlineData(26, AgeBand.Peak)]    // 満期引退年齢
        [InlineData(27, AgeBand.Peak)]    // 満期を越えた例外値・旧データは全盛期へクランプ
        [InlineData(40, AgeBand.Peak)]
        public void AgeBand_MatchesDefinitionTable(int age, AgeBand expected)
        {
            var adventurer = new Adventurer { Age = age };
            Assert.Equal(expected, adventurer.AgeBand);
        }

        [Theory]
        [InlineData(18, 0.35)]
        [InlineData(20, 0.30)]
        [InlineData(25, 0.12)]
        [InlineData(99, 0.12)] // 範囲外も全盛期へクランプされ、必ず有効な確率が返る
        public void GetBaseProbability_ReturnsRateOfEachActiveAgeBand(int age, double expected)
        {
            // 8年稼働モデルで到達不能になった円熟期・限界期を撤去し、実働3区分へ純化した
            // （→ 03 §0.11・§3.0）。どの年齢でも0.0（＝成長しない）には落ちない。
            var adventurer = new Adventurer { Age = age };

            Assert.Equal(expected, GrowthBalance.GetBaseProbability(adventurer.AgeBand));
        }

        // ---------------- 成長期・全盛期はAgingSystem内では何もしない（成長はGrowthSystemへ移動） ----------------

        [Fact]
        public void ProcessWeeklyAging_YoungBand_DoesNothingToStats()
        {
            var adventurer = new Adventurer
            {
                Age = 18,
                STR = 10, PA_STR = 50,
                AGI = 20, PA_AGI = 20,
                VIT = 20, PA_VIT = 20,
            };
            var state = CreateState(adventurer, weekNumber: 5);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(10, adventurer.STR); // AgingSystemはもう成長を扱わない（→ GrowthSystem）
            Assert.Equal(20, adventurer.AGI);
            Assert.Equal(20, adventurer.VIT);
        }

        [Fact]
        public void ProcessWeeklyAging_PeakBand_NeitherGrowsNorDeclines()
        {
            var adventurer = new Adventurer
            {
                Age = 24,
                STR = 40, PA_STR = 80,
                AGI = 40, PA_AGI = 80,
                VIT = 40, PA_VIT = 80,
            };
            // 年度末（衰微が起きうる週）でも全盛期は対象外であることを確認する。
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(40, adventurer.STR);
            Assert.Equal(40, adventurer.AGI);
            Assert.Equal(40, adventurer.VIT);
        }

        // ---------------- 衰微の廃止（8年稼働・満期引退モデルへの改訂） ----------------
        // 旧モデルには円熟期(28〜34)・限界期(35〜40)のフィジカル衰微があったが、
        // 「冒険者は衰えない代わりに、短い稼働期間で必ずギルドを去る」という新モデルへ
        // 変更したため、加齢による能力低下は一切発生しない（→ AgingSystem クラスdocコメント）。

        [Theory]
        [InlineData(30)] // 旧・円熟期（年1回の衰微があった年齢帯）
        [InlineData(37)] // 旧・限界期（年2回・低下量拡大だった年齢帯）
        public void ProcessWeeklyAging_NeverDeclinesStats_RegardlessOfAge(int age)
        {
            var adventurer = new Adventurer
            {
                Age = age,
                STR = 40, PA_STR = 80, AGI = 40, PA_AGI = 80, VIT = 40, PA_VIT = 80,
                DEX = 40, PA_DEX = 80, MND = 40, PA_MND = 80, LDR = 40, PA_LDR = 80,
                INT = 40, PA_INT = 80,
            };
            var system = new AgingSystem(new AlwaysMinRng());

            // 旧モデルで衰微が発生していた週（限界期の24週・年度末の48週）を両方通す。
            system.ProcessWeeklyAging(CreateState(adventurer, weekNumber: 24));
            system.ProcessWeeklyAging(CreateState(adventurer, weekNumber: 48));

            Assert.Equal(40, adventurer.STR);
            Assert.Equal(40, adventurer.AGI);
            Assert.Equal(40, adventurer.VIT);
            Assert.Equal(40, adventurer.DEX);
            Assert.Equal(40, adventurer.MND);
            Assert.Equal(40, adventurer.LDR);
            Assert.Equal(40, adventurer.INT);
        }

        // ---------------- 稼働週数の記録（→ Adventurer.ActiveWeeks） ----------------

        [Fact]
        public void ProcessWeeklyAging_CountsActiveWeeks()
        {
            var adventurer = new Adventurer { Age = 20 };
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(CreateState(adventurer, weekNumber: 5));
            system.ProcessWeeklyAging(CreateState(adventurer, weekNumber: 6));
            system.ProcessWeeklyAging(CreateState(adventurer, weekNumber: 7));

            Assert.Equal(3, adventurer.ActiveWeeks);
        }

        [Fact]
        public void MaxActiveWeeks_IsEightYearsInWeeks()
        {
            // 8年稼働＝48週×8年。月単位ではなく週単位のまま運用する（ユーザー決定）。
            Assert.Equal(48 * 8, AgingSystem.MaxActiveWeeks);
        }

        // ---------------- 年齢の進行と満期引退（§3.7、26歳満期モデル） ----------------

        [Fact]
        public void ProcessWeeklyAging_AdvancesAgeAtYearEnd()
        {
            var adventurer = new Adventurer { Age = 20 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(21, adventurer.Age);
            Assert.False(adventurer.IsRetired);
        }

        [Fact]
        public void ProcessWeeklyAging_DoesNotRetireBeforeReachingMaturityAge()
        {
            // 24歳の年度末はまだ満期ではない（25歳になるだけ）。
            var adventurer = new Adventurer { Age = 24 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(25, adventurer.Age);
            Assert.False(adventurer.IsRetired);
        }

        [Fact]
        public void ProcessWeeklyAging_RetiresAtTheYearEndWhenTurningMaturityAge()
        {
            // 26歳になった年度末で満期引退する（→ 「全盛期のうちに引退させ、退職金を持たせて
            // 安全に自立させる」）。18歳加入ならこれがちょうど8年目の年度末にあたる。
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 50 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 5000;
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.True(adventurer.IsRetired);
            Assert.Equal(26, adventurer.Age); // 26歳になり、その時点で引退
            Assert.False(adventurer.IsAvailable);
        }

        [Fact]
        public void ProcessWeeklyAging_DoesNotAdvanceAgeMidYear()
        {
            var adventurer = new Adventurer { Age = 20 };
            var state = CreateState(adventurer, weekNumber: 25);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(20, adventurer.Age);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_MovesFromActiveToRetiredRoster()
        {
            var adventurer = new Adventurer { Age = 25 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.DoesNotContain(adventurer, state.Adventurers);
            Assert.Contains(adventurer, state.RetiredAdventurers);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_PaysBaseSeveranceOfTwelveWeeksWage()
        {
            // 功績が無い場合は基礎部分（週給×12週）のみ。
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 50, TotalContributionScore = 0 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 1000;
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(1000 - 50 * 12, state.Gold);
            Assert.True(adventurer.SeverancePaid);
        }

        // ---------------- 功績に応じた退職金（→ 8年稼働・満期引退モデル） ----------------

        [Fact]
        public void CalculateSeverancePay_AddsContributionBonusOnTopOfBaseWage()
        {
            // 「危険な仕事をさせた分だけ手厚く送り出す」：累積功績×係数が基礎額に上乗せされる。
            var plain = new Adventurer { WeeklyWage = 50, TotalContributionScore = 0 };
            var veteran = new Adventurer { WeeklyWage = 50, TotalContributionScore = 100 };

            int plainPay = AgingSystem.CalculateSeverancePay(plain);
            int veteranPay = AgingSystem.CalculateSeverancePay(veteran);

            Assert.Equal(50 * EconomyBalance.SeveranceWeeks, plainPay);
            Assert.Equal(
                plainPay + (int)System.Math.Round(100 * EconomyBalance.SeveranceContributionCoefficient),
                veteranPay);
            Assert.True(veteranPay > plainPay);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_PaysContributionBonus()
        {
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 50, TotalContributionScore = 40 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 100_000;
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(100_000 - AgingSystem.CalculateSeverancePay(adventurer), state.Gold);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_StillSucceedsButDamagesReputation_WhenGoldIsShort()
        {
            // 退職金を払いきれなくても引退自体は成立させる（冒険者を人質に取らない）が、
            // 「約束した退職金を用意できないギルド」として名声が下がる。
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 50 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 10; // 明らかに足りない
            state.Reputation = 100;
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.True(adventurer.IsRetired);
            Assert.Contains(adventurer, state.RetiredAdventurers);
            Assert.Equal(100 - EconomyBalance.SeveranceShortfallReputationPenalty, state.Reputation);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_DoesNotTouchReputation_WhenGoldIsSufficient()
        {
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 50 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 100_000;
            state.Reputation = 100;
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(100, state.Reputation);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_RecordsAgeAndWeek()
        {
            var adventurer = new Adventurer { Age = 25 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(26, adventurer.RetiredAtAge);
            Assert.Equal(48, adventurer.RetiredAtWeek);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_FreesTrainingSlot()
        {
            var adventurer = new Adventurer { Age = 25 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.DoesNotContain(adventurer.Id, state.TrainingAssignments.Keys);
        }

        // ---------------- 早期引退（→ 03 §7「引退の経路」） ----------------

        [Fact]
        public void RetireVoluntarily_MovesAdventurerToRetiredList_EvenBeforeMaturityAge()
        {
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 40 };
            var state = new GameState { Gold = 1000, Adventurers = { adventurer } };
            var system = new AgingSystem(new AlwaysMinRng());

            system.RetireVoluntarily(state, adventurer);

            Assert.True(adventurer.IsRetired);
            Assert.DoesNotContain(adventurer, state.Adventurers);
            Assert.Contains(adventurer, state.RetiredAdventurers);
        }

        [Fact]
        public void RetireVoluntarily_PaysSameSeveranceAsForcedRetirement()
        {
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 40 };
            var state = new GameState { Gold = 1000, Adventurers = { adventurer } };
            var system = new AgingSystem(new AlwaysMinRng());

            system.RetireVoluntarily(state, adventurer);

            Assert.Equal(1000 - 40 * 12, state.Gold); // 退職金＝週給×12週（40歳強制引退と共通）
            Assert.True(adventurer.SeverancePaid);
        }

        [Fact]
        public void RetireVoluntarily_RecordsCurrentAgeAndWeek_NotNecessarilyForty()
        {
            var adventurer = new Adventurer { Age = 25 };
            var state = new GameState { WeekNumber = 30, Adventurers = { adventurer } };
            var system = new AgingSystem(new AlwaysMinRng());

            system.RetireVoluntarily(state, adventurer);

            Assert.Equal(25, adventurer.RetiredAtAge); // 40歳ちょうどではない、その時点の年齢を記録
            Assert.Equal(30, adventurer.RetiredAtWeek);
        }

        [Fact]
        public void RetireVoluntarily_DoesNothing_WhenAlreadyRetired()
        {
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 40, IsRetired = true };
            var state = new GameState { Gold = 1000, RetiredAdventurers = { adventurer } };
            var system = new AgingSystem(new AlwaysMinRng());

            system.RetireVoluntarily(state, adventurer);

            Assert.Equal(1000, state.Gold); // 退職金が二重に支給されない
            Assert.Single(state.RetiredAdventurers); // 重複追加されない
        }

        // ---------------- 形見装備の自動回収（→ 03 §4.2.2、2026年9月新設） ----------------

        [Fact]
        public void Retirement_CollectsEquipments_ToArmory()
        {
            // 満期引退：装備したままRetiredAdventurersへ移ると武具に二度と手が届かなくなるため、
            // ロースターから外す直前にギルド保管庫へ回収される（→ EquipmentSystem.UnequipAllToArmory）。
            var adventurer = new Adventurer { Name = "満期", Age = 25, WeeklyWage = 40, JobClass = JobClass.Warrior };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 10000;
            var equipment = new EquipmentSystem();
            var weapon = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var armor = EquipmentItem.FromCatalog(ItemCatalog.LeatherArmor);
            state.Armory.AddRange(new[] { weapon, armor });
            Assert.True(equipment.TryEquip(state, adventurer, EquipmentSlot.Weapon, weapon));
            Assert.True(equipment.TryEquip(state, adventurer, EquipmentSlot.Armor, armor));
            Assert.Empty(state.Armory);

            new AgingSystem(new AlwaysMinRng()).ProcessWeeklyAging(state);

            Assert.True(adventurer.IsRetired);
            Assert.Contains(adventurer, state.RetiredAdventurers);
            Assert.Null(adventurer.EquippedWeapon);
            Assert.Null(adventurer.EquippedArmor);
            Assert.Equal(2, state.Armory.Count);
            Assert.Contains(weapon, state.Armory);
            Assert.Contains(armor, state.Armory);
            // 引退者は健在なので「形見」ではなく返還として記録する。
            Assert.Equal("満期（引退）から返還", weapon.AcquiredFrom);
            Assert.Equal(48, weapon.AcquiredAtWeek);
        }

        [Fact]
        public void RetireVoluntarily_CollectsEquipments_ToArmory_AndReturnsThem()
        {
            var adventurer = new Adventurer { Name = "早期", Age = 24, WeeklyWage = 40, JobClass = JobClass.Warrior };
            var state = new GameState { WeekNumber = 30, Gold = 10000, Adventurers = { adventurer } };
            var accessory = EquipmentItem.FromCatalog(ItemCatalog.PowerRing);
            state.Armory.Add(accessory);
            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Accessory1, accessory));

            var recovered = new AgingSystem(new AlwaysMinRng()).RetireVoluntarily(state, adventurer);

            // 回収された装備は戻り値としても受け取れる（UI側の引退メッセージ表示用）。
            Assert.Same(accessory, Assert.Single(recovered));
            Assert.Null(adventurer.EquippedAccessory1);
            Assert.Same(accessory, Assert.Single(state.Armory));
            Assert.Equal("早期（引退）から返還", accessory.AcquiredFrom);
        }

        [Fact]
        public void RetireVoluntarily_ReturnsEmpty_WhenAlreadyRetired()
        {
            var adventurer = new Adventurer { Age = 25, IsRetired = true };
            var state = new GameState { RetiredAdventurers = { adventurer } };

            Assert.Empty(new AgingSystem(new AlwaysMinRng()).RetireVoluntarily(state, adventurer));
        }

        [Fact]
        public void Retirement_WithNoEquipment_LeavesArmoryUntouched()
        {
            var adventurer = new Adventurer { Age = 25, WeeklyWage = 40 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 10000;

            new AgingSystem(new AlwaysMinRng()).ProcessWeeklyAging(state);

            Assert.True(adventurer.IsRetired);
            Assert.Empty(state.Armory);
        }

        [Fact]
        public void ProcessWeeklyAging_SkipsRetiredAdventurersEntirely()
        {
            var adventurer = new Adventurer { Age = 40, IsRetired = true, STR = 30, PA_STR = 60 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(30, adventurer.STR); // 衰微処理も年齢処理も走らない
            Assert.Equal(40, adventurer.Age);
        }

        [Fact]
        public void ProcessWeeklyAging_OneRetirement_DoesNotSkipProcessingOtherAdventurers()
        {
            // state.Adventurersを直接foreachすると、途中でRemoveしたときに
            // コレクション変更例外や後続要素のスキップが起こりうる。スナップショットで
            // 回避できていることを、複数人・年度末処理で確認する。
            var retiring = new Adventurer { Age = 40 };
            var stayingYoung = new Adventurer { Age = 20 };
            var state = new GameState
            {
                WeekNumber = 48,
                Adventurers = { retiring, stayingYoung },
            };
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Contains(retiring, state.RetiredAdventurers);
            Assert.Contains(stayingYoung, state.Adventurers);
            Assert.Equal(21, stayingYoung.Age); // 年度末の加齢が正しく適用されている
        }
    }
}
