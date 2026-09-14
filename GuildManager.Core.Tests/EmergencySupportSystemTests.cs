using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// ギルドマスターの後方支援（緊急撤退・緊急回復）のテスト。
    /// → コアシステム刷新仕様「(3) ギルドマスター後方支援機能の実装」。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class EmergencySupportSystemTests
    {
        private static Adventurer MakeAdventurer(int currentHpPercent = 100)
        {
            var a = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50, INT = 50 };
            a.CurrentHP = a.MaxHP * currentHpPercent / 100;
            return a;
        }

        /// <summary>派遣中の状態（ActiveDispatch登録済み・メンバーがIsDispatched）を作る。</summary>
        private static (GameState State, ActiveDispatch Dispatch) MakeDispatchedState(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members)
            {
                party.TryAdd(m);
                m.IsDispatched = true;
            }

            var dispatch = new ActiveDispatch
            {
                Party = party,
                Quest = new Quest { Name = "test", Difficulty = 30 },
                WeeksRemaining = 2,
            };

            var state = new GameState { Gold = 1000 };
            foreach (var m in members)
                state.Adventurers.Add(m);
            state.ActiveDispatches.Add(dispatch);

            return (state, dispatch);
        }

        // ---------------- 緊急撤退 ----------------

        [Fact]
        public void TryEmergencyRetreat_RemovesDispatchAndReleasesMembers()
        {
            var member = MakeAdventurer(40);
            var (state, dispatch) = MakeDispatchedState(member);
            var system = new EmergencySupportSystem();

            bool result = system.TryEmergencyRetreat(state, dispatch);

            Assert.True(result);
            Assert.Empty(state.ActiveDispatches);
            Assert.False(member.IsDispatched); // 再編成・訓練配置が可能な状態へ戻る
        }

        [Fact]
        public void TryEmergencyRetreat_DoesNotInjureOrKillAnyone()
        {
            // 撤退＝判定自体を行わない。報酬は得られないが、負傷も発生しない。
            var member = MakeAdventurer(15);
            int hpBefore = member.CurrentHP;
            var (state, dispatch) = MakeDispatchedState(member);

            new EmergencySupportSystem().TryEmergencyRetreat(state, dispatch);

            Assert.Equal(hpBefore, member.CurrentHP);
            Assert.Equal(InjurySeverity.None, member.Injury);
            Assert.Equal(1000, state.Gold); // 報酬も加算されない
        }

        [Fact]
        public void TryEmergencyRetreat_KeepsConsumables_BecauseQuestWasNeverResolved()
        {
            // 解決していない＝携行アイテムは使っていないので持ち帰る。
            var member = MakeAdventurer();
            var (state, dispatch) = MakeDispatchedState(member);
            dispatch.Party.TryAddConsumable(ConsumableCatalog.AntidoteId);

            new EmergencySupportSystem().TryEmergencyRetreat(state, dispatch);

            Assert.Contains(ConsumableCatalog.AntidoteId, dispatch.Party.ConsumableItemIds);
        }

        [Fact]
        public void TryEmergencyRetreat_ReturnsFalse_WhenDispatchIsNotActive()
        {
            var (state, dispatch) = MakeDispatchedState(MakeAdventurer());
            var system = new EmergencySupportSystem();
            system.TryEmergencyRetreat(state, dispatch); // 1回目で取り除かれる

            bool second = system.TryEmergencyRetreat(state, dispatch);

            Assert.False(second);
        }

        // ---------------- 緊急回復 ----------------

        [Fact]
        public void TryEmergencyHeal_HealsConfiguredRatioAndChargesCost()
        {
            var member = MakeAdventurer(20);
            int hpBefore = member.CurrentHP;
            var (state, dispatch) = MakeDispatchedState(member);

            bool result = new EmergencySupportSystem().TryEmergencyHeal(state, dispatch);

            Assert.True(result);
            Assert.Equal(1000 - EmergencyBalance.EmergencyHealCostGold, state.Gold);

            int expectedHeal = (int)System.Math.Round(member.MaxHP * EmergencyBalance.EmergencyHealRatio);
            Assert.Equal(hpBefore + expectedHeal, member.CurrentHP);
        }

        [Fact]
        public void TryEmergencyHeal_AppliesToEveryMemberOfTheSquad()
        {
            var a = MakeAdventurer(20);
            var b = MakeAdventurer(30);
            int aBefore = a.CurrentHP;
            int bBefore = b.CurrentHP;
            var (state, dispatch) = MakeDispatchedState(a, b);

            new EmergencySupportSystem().TryEmergencyHeal(state, dispatch);

            Assert.True(a.CurrentHP > aBefore);
            Assert.True(b.CurrentHP > bBefore);
        }

        [Fact]
        public void TryEmergencyHeal_DoesNotExceedMaxHp()
        {
            var nearlyFull = MakeAdventurer(95);
            var (state, dispatch) = MakeDispatchedState(nearlyFull);

            new EmergencySupportSystem().TryEmergencyHeal(state, dispatch);

            Assert.Equal(nearlyFull.MaxHP, nearlyFull.CurrentHP);
        }

        [Fact]
        public void TryEmergencyHeal_CanOnlyBeUsedOncePerDispatch()
        {
            var member = MakeAdventurer(20);
            var (state, dispatch) = MakeDispatchedState(member);
            var system = new EmergencySupportSystem();

            Assert.True(system.TryEmergencyHeal(state, dispatch));
            int goldAfterFirst = state.Gold;
            int hpAfterFirst = member.CurrentHP;

            bool second = system.TryEmergencyHeal(state, dispatch);

            Assert.False(second);
            Assert.Equal(goldAfterFirst, state.Gold);   // 2回目は課金されない
            Assert.Equal(hpAfterFirst, member.CurrentHP); // HPも動かない
        }

        [Fact]
        public void TryEmergencyHeal_Fails_WhenGoldInsufficient()
        {
            var member = MakeAdventurer(20);
            int hpBefore = member.CurrentHP;
            var (state, dispatch) = MakeDispatchedState(member);
            state.Gold = EmergencyBalance.EmergencyHealCostGold - 1;

            bool result = new EmergencySupportSystem().TryEmergencyHeal(state, dispatch);

            Assert.False(result);
            Assert.Equal(hpBefore, member.CurrentHP);
            Assert.False(dispatch.EmergencyHealUsed); // 使用済みフラグも立たない（再挑戦できる）
        }

        [Fact]
        public void TryEmergencyHeal_Fails_WhenDispatchIsNotActive()
        {
            var (state, dispatch) = MakeDispatchedState(MakeAdventurer(20));
            state.ActiveDispatches.Clear();

            Assert.False(new EmergencySupportSystem().TryEmergencyHeal(state, dispatch));
        }

        [Fact]
        public void EmergencyHealUsedFlag_SurvivesSaveAndLoad()
        {
            // セーブ・ロードを挟むだけで「1出撃1回」の制限を回避できないこと。
            var member = MakeAdventurer(20);
            var (state, dispatch) = MakeDispatchedState(member);
            new EmergencySupportSystem().TryEmergencyHeal(state, dispatch);

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.True(restored.ActiveDispatches[0].EmergencyHealUsed);
        }
    }
}
