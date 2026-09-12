using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// パーティー編成の永続化（仕様書 03 §4.0.2）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class PartyFormationSystemTests
    {
        // ---------------- 作成・改名・削除 ----------------

        [Fact]
        public void CreateParty_AddsToGameStateAndReturnsIt()
        {
            var state = new GameState();
            var system = new PartyFormationSystem();

            var party = system.CreateParty(state, "第一遠征隊");

            Assert.Contains(party, state.SavedParties);
            Assert.Equal("第一遠征隊", party.Name);
            Assert.Empty(party.MemberIds);
        }

        [Fact]
        public void RenameParty_UpdatesName()
        {
            var state = new GameState();
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "旧名");

            system.RenameParty(party, "新名");

            Assert.Equal("新名", party.Name);
        }

        [Fact]
        public void DeleteParty_RemovesFromGameState()
        {
            var state = new GameState();
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "解散予定");

            system.DeleteParty(state, party);

            Assert.DoesNotContain(party, state.SavedParties);
        }

        // ---------------- メンバー編成・単一所属制約 ----------------

        [Fact]
        public void TryAssignMember_Succeeds_ForUnassignedAdventurer()
        {
            var adventurer = new Adventurer();
            var state = new GameState { Adventurers = { adventurer } };
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "A隊");

            bool result = system.TryAssignMember(state, party, adventurer.Id);

            Assert.True(result);
            Assert.Contains(adventurer.Id, party.MemberIds);
        }

        [Fact]
        public void TryAssignMember_IsIdempotent_WhenAlreadyInSameParty()
        {
            var adventurer = new Adventurer();
            var state = new GameState { Adventurers = { adventurer } };
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "A隊");
            system.TryAssignMember(state, party, adventurer.Id);

            bool result = system.TryAssignMember(state, party, adventurer.Id);

            Assert.True(result);
            Assert.Single(party.MemberIds);
        }

        [Fact]
        public void TryAssignMember_Fails_WhenPartyAlreadyHasFourMembers()
        {
            var state = new GameState();
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "満員隊");
            for (int i = 0; i < 4; i++)
            {
                var a = new Adventurer();
                state.Adventurers.Add(a);
                system.TryAssignMember(state, party, a.Id);
            }
            var fifthAdventurer = new Adventurer();
            state.Adventurers.Add(fifthAdventurer);

            bool result = system.TryAssignMember(state, party, fifthAdventurer.Id);

            Assert.False(result);
            Assert.Equal(4, party.MemberIds.Count);
        }

        [Fact]
        public void TryAssignMember_RemovesFromPreviousParty_WhenMovingToAnotherParty()
        {
            // 1人の冒険者は同時に1つのパーティーにしか所属できない。
            var adventurer = new Adventurer();
            var state = new GameState { Adventurers = { adventurer } };
            var system = new PartyFormationSystem();
            var partyA = system.CreateParty(state, "A隊");
            var partyB = system.CreateParty(state, "B隊");
            system.TryAssignMember(state, partyA, adventurer.Id);

            bool result = system.TryAssignMember(state, partyB, adventurer.Id);

            Assert.True(result);
            Assert.DoesNotContain(adventurer.Id, partyA.MemberIds);
            Assert.Contains(adventurer.Id, partyB.MemberIds);
        }

        [Fact]
        public void TryAssignMember_TreatsStaleMemberId_AsVacantSlot()
        {
            // 戦死・引退等で現役ロースターから除外された冒険者のIdがMemberIdsに
            // 残っていても、それは実質的な空き枠として扱われ、新規メンバーを
            // 追加できる（4枠すべてが現役ロースター上の実在者である必要はない）。
            var state = new GameState();
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "隊");
            var fallenId = System.Guid.NewGuid(); // 現役ロースターに存在しないId
            party.MemberIds.Add(fallenId);

            var newMember = new Adventurer();
            state.Adventurers.Add(newMember);

            bool result = system.TryAssignMember(state, party, newMember.Id);

            Assert.True(result);
            Assert.Contains(newMember.Id, party.MemberIds);
        }

        [Fact]
        public void RemoveMember_ReturnsAdventurerToUnassigned()
        {
            var adventurer = new Adventurer();
            var state = new GameState { Adventurers = { adventurer } };
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "隊");
            system.TryAssignMember(state, party, adventurer.Id);

            system.RemoveMember(party, adventurer.Id);

            Assert.DoesNotContain(adventurer.Id, party.MemberIds);
            Assert.Contains(adventurer, PartyFormationSystem.GetUnassignedAdventurers(state));
        }

        // ---------------- 未編成の導出 ----------------

        [Fact]
        public void GetUnassignedAdventurers_ExcludesMembersOfAnyParty()
        {
            var assigned = new Adventurer();
            var unassigned = new Adventurer();
            var state = new GameState { Adventurers = { assigned, unassigned } };
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "隊");
            system.TryAssignMember(state, party, assigned.Id);

            var result = PartyFormationSystem.GetUnassignedAdventurers(state).ToList();

            Assert.DoesNotContain(assigned, result);
            Assert.Contains(unassigned, result);
        }

        [Fact]
        public void GetUnassignedAdventurers_ReturnsAll_WhenNoPartiesExist()
        {
            var a = new Adventurer();
            var state = new GameState { Adventurers = { a } };

            var result = PartyFormationSystem.GetUnassignedAdventurers(state);

            Assert.Contains(a, result);
        }

        // ---------------- 出撃可能メンバーの解決（BuildDispatchParty） ----------------

        [Fact]
        public void BuildDispatchParty_IncludesOnlyAvailableMembers()
        {
            var available = new Adventurer();
            var injured = new Adventurer { Injury = InjurySeverity.Severe };
            var state = new GameState { Adventurers = { available, injured } };

            var party = PartyFormationSystem.BuildDispatchParty(state, new[] { available.Id, injured.Id });

            Assert.Single(party.Members);
            Assert.Contains(available, party.Members);
            Assert.DoesNotContain(injured, party.Members);
        }

        [Fact]
        public void BuildDispatchParty_ReturnsEmptyParty_WhenNoOneAvailable()
        {
            var injured1 = new Adventurer { Injury = InjurySeverity.Severe };
            var injured2 = new Adventurer { IsDispatched = true };
            var state = new GameState { Adventurers = { injured1, injured2 } };

            var party = PartyFormationSystem.BuildDispatchParty(state, new[] { injured1.Id, injured2.Id });

            Assert.True(party.IsEmpty);
        }

        [Fact]
        public void BuildDispatchParty_Succeeds_WithOneToThreeMembers()
        {
            // 4人揃っていなくても、出撃可能な人数（1〜3人）でPartyを構成できる
            // ことを確認する（→ 03 §4.0.1・QuestResolverはメンバー数に依存しない設計）。
            var a1 = new Adventurer();
            var a2 = new Adventurer();
            var a3 = new Adventurer();
            var unavailable = new Adventurer { IsDispatched = true };
            var state = new GameState { Adventurers = { a1, a2, a3, unavailable } };

            var party = PartyFormationSystem.BuildDispatchParty(state, new[] { a1.Id, a2.Id, a3.Id, unavailable.Id });

            Assert.Equal(3, party.Members.Count);
        }

        [Fact]
        public void BuildDispatchParty_SkipsUnknownIds_WithoutThrowing()
        {
            var state = new GameState();
            var party = PartyFormationSystem.BuildDispatchParty(state, new[] { System.Guid.NewGuid() });

            Assert.True(party.IsEmpty);
        }

        [Fact]
        public void BuildDispatchParty_DoesNotModify_SourceMemberIdsList()
        {
            // 一時的な入れ替え：呼び出し側が渡すId一覧を変更したり、SavedParty側の
            // データに書き込んだりしないことを確認する。
            var a = new Adventurer();
            var b = new Adventurer();
            var state = new GameState { Adventurers = { a, b } };
            var system = new PartyFormationSystem();
            var savedParty = system.CreateParty(state, "隊");
            system.TryAssignMember(state, savedParty, a.Id);

            // 一時的にbへ差し替えた出撃メンバー一覧（savedParty.MemberIdsのコピーではなく別の一覧）
            var temporaryLineup = new System.Collections.Generic.List<System.Guid> { b.Id };
            PartyFormationSystem.BuildDispatchParty(state, temporaryLineup);

            // savedParty自体はaのままで変化していない
            Assert.Contains(a.Id, savedParty.MemberIds);
            Assert.DoesNotContain(b.Id, savedParty.MemberIds);
        }

        // ---------------- 出撃不可メンバーの一覧（GetUnavailableMembers） ----------------

        [Fact]
        public void GetUnavailableMembers_ReturnsOnlyUnavailableOnes()
        {
            var ok = new Adventurer { Name = "健常" };
            var hurt = new Adventurer { Name = "重傷者", Injury = InjurySeverity.Severe };
            var state = new GameState { Adventurers = { ok, hurt } };

            var result = PartyFormationSystem.GetUnavailableMembers(state, new[] { ok.Id, hurt.Id });

            Assert.Single(result);
            Assert.Equal("重傷者", result[0].Name);
        }

        [Fact]
        public void GetUnavailableMembers_ReturnsEmpty_WhenAllAvailable()
        {
            var a = new Adventurer();
            var state = new GameState { Adventurers = { a } };

            var result = PartyFormationSystem.GetUnavailableMembers(state, new[] { a.Id });

            Assert.Empty(result);
        }

        // ---------------- 相性ペア表示（GetCompatibilityPairs） ----------------

        [Fact]
        public void GetCompatibilityPairs_ReturnsAllPairs_ForFourMembers()
        {
            var members = new[] { new Adventurer(), new Adventurer(), new Adventurer(), new Adventurer() };
            var state = new GameState { Adventurers = { members[0], members[1], members[2], members[3] } };
            var ids = members.Select(m => m.Id).ToList();

            var pairs = PartyFormationSystem.GetCompatibilityPairs(state, ids);

            // 4人の全2人組 = 4C2 = 6組
            Assert.Equal(6, pairs.Count);
        }

        [Fact]
        public void GetCompatibilityPairs_DefaultsToNeutralValue_ForUnregisteredPairs()
        {
            var a = new Adventurer();
            var b = new Adventurer();
            var state = new GameState { Adventurers = { a, b } };

            var pairs = PartyFormationSystem.GetCompatibilityPairs(state, new[] { a.Id, b.Id });

            var pair = Assert.Single(pairs);
            Assert.Equal(CompatibilityBalance.InitialValue, pair.Value);
            Assert.False(pair.IsHostile);
        }

        [Fact]
        public void GetCompatibilityPairs_FlagsHostilePair()
        {
            var a = new Adventurer();
            var b = new Adventurer();
            var state = new GameState { Adventurers = { a, b } };
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = CompatibilityBalance.HostileThreshold - 1;

            var pairs = PartyFormationSystem.GetCompatibilityPairs(state, new[] { a.Id, b.Id });

            var pair = Assert.Single(pairs);
            Assert.True(pair.IsHostile);
        }
    }
}
