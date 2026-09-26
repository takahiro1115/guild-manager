using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 編成画面の改善（→ 03 §0.42）のCore部分：リーダー選出（PartyFormationSystem.TryPromoteToLeader）、
    /// 仮の編成での指標（PreviewMetrics）、任務別の指標一式（SquadMetrics）と隊員1名ごとの貢献値のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~PartyLeaderAndPreview`
    /// </summary>
    public class PartyLeaderAndPreviewTests
    {
        private static Adventurer Make(string name, JobClass job = JobClass.Scholar, int stat = 30, int ldr = 10)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = job,
                STR = stat, VIT = stat, AGI = stat, DEX = stat, INT = stat, MND = stat, LDR = ldr,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static (GameState State, SavedParty Party, Adventurer[] Members) MakeParty(int count)
        {
            var members = Enumerable.Range(0, count).Select(i => Make($"隊員{i}", ldr: 10 + i * 20)).ToArray();
            var state = new GameState();
            state.Adventurers.AddRange(members);
            var system = new PartyFormationSystem();
            var party = system.CreateParty(state, "第1部隊");
            foreach (var m in members)
                Assert.True(system.TryAssignMember(state, party, m.Id));
            return (state, party, members);
        }

        // ---------------- リーダー選出 ----------------

        [Fact]
        public void TryPromoteToLeader_MovesMemberToFront_AndShiftsOthersRight()
        {
            var (state, party, m) = MakeParty(4);

            Assert.True(new PartyFormationSystem().TryPromoteToLeader(state, party, m[2].Id));

            // 3番目を先頭へ。残りは元の順（0,1,3）を保って右へずれる
            Assert.Equal(new[] { m[2].Id, m[0].Id, m[1].Id, m[3].Id }, party.MemberIds);
        }

        [Fact]
        public void TryPromoteToLeader_LastMember()
        {
            var (state, party, m) = MakeParty(4);

            Assert.True(new PartyFormationSystem().TryPromoteToLeader(state, party, m[3].Id));

            Assert.Equal(new[] { m[3].Id, m[0].Id, m[1].Id, m[2].Id }, party.MemberIds);
        }

        [Fact]
        public void TryPromoteToLeader_Fails_ForCurrentLeaderOrNonMember()
        {
            var (state, party, m) = MakeParty(3);
            var outsider = Make("部外者");
            state.Adventurers.Add(outsider);
            var before = party.MemberIds.ToList();
            var system = new PartyFormationSystem();

            Assert.False(system.TryPromoteToLeader(state, party, m[0].Id));
            Assert.False(system.TryPromoteToLeader(state, party, outsider.Id));
            Assert.Equal(before, party.MemberIds);
        }

        [Fact]
        public void TryPromoteToLeader_Fails_WhileDispatched()
        {
            var (state, party, m) = MakeParty(3);
            m[1].IsDispatched = true;
            var before = party.MemberIds.ToList();

            Assert.False(new PartyFormationSystem().TryPromoteToLeader(state, party, m[2].Id));
            Assert.Equal(before, party.MemberIds);
        }

        [Fact]
        public void TryPromoteToLeader_ChangesLeaderLdrInMetrics()
        {
            // 隊員のLDRは 10・30・50。リーダーを3人目（LDR50）にすると、隊長LDRが効く指標が上がる。
            var (state, party, m) = MakeParty(3);
            var before = PartyFormationSystem.PreviewMetrics(state, party.MemberIds);

            Assert.True(new PartyFormationSystem().TryPromoteToLeader(state, party, m[2].Id));
            var after = PartyFormationSystem.PreviewMetrics(state, party.MemberIds);

            Assert.Equal((50 - 10) * DungeonTraversalBalance.WeightLdr, after.TraversalPower - before.TraversalPower, precision: 6);
            Assert.Equal((50 - 10) * ScoutingBalance.StealthWeightLdr, after.StealthLeaderBonus - before.StealthLeaderBonus, precision: 6);
            Assert.True(after.GatheringScore > before.GatheringScore);
            Assert.Equal(before.GuardPower, after.GuardPower, precision: 6); // 護衛・解析・火力は隊長に依らない
            Assert.Equal(before.AnalysisScore, after.AnalysisScore, precision: 6);
            Assert.Equal(before.BossPower, after.BossPower, precision: 6);
        }

        [Fact]
        public void WithLeader_ReturnsReorderedCopy_WithoutTouchingSource()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var c = Guid.NewGuid();
            var source = new List<Guid> { a, b, c };

            Assert.Equal(new[] { c, a, b }, PartyFormationSystem.WithLeader(source, c));
            Assert.Equal(new[] { a, b, c }, source);
            Assert.Equal(new[] { a, b, c }, PartyFormationSystem.WithLeader(source, Guid.NewGuid()));
        }

        // ---------------- 仮の編成での指標（増減プレビュー） ----------------

        [Fact]
        public void PreviewMetrics_WithExtraCandidate_DoesNotModifySavedParty()
        {
            var (state, party, _) = MakeParty(2);
            var candidate = Make("候補", JobClass.Ranger, stat: 50);
            state.Adventurers.Add(candidate);
            var saved = party.MemberIds.ToList();

            var current = PartyFormationSystem.PreviewMetrics(state, party.MemberIds);
            var withCandidate = PartyFormationSystem.PreviewMetrics(state, party.MemberIds.Append(candidate.Id));

            Assert.Equal(saved, party.MemberIds);
            Assert.True(withCandidate.TraversalPower > current.TraversalPower);
            Assert.True(withCandidate.GuardPower > current.GuardPower);
            Assert.True(withCandidate.BossPower > current.BossPower);
            Assert.True(withCandidate.GatheringScore > current.GatheringScore);
            Assert.Equal(PartyFormationSystem.CalculateMetrics(
                PartyFormationSystem.BuildDispatchParty(state, party.MemberIds.Append(candidate.Id)), state), withCandidate);
        }

        [Fact]
        public void PreviewMetrics_ExcludesUnavailableMembers_LikeDispatch()
        {
            var (state, party, m) = MakeParty(2);
            var injured = Make("負傷者", stat: 90);
            injured.Injury = InjurySeverity.Severe;
            state.Adventurers.Add(injured);

            var current = PartyFormationSystem.PreviewMetrics(state, party.MemberIds);
            var withInjured = PartyFormationSystem.PreviewMetrics(state, party.MemberIds.Append(injured.Id));

            Assert.Equal(current, withInjured);
        }

        // ---------------- 任務別の指標一式 ----------------

        [Fact]
        public void CalculateMetrics_IncludesAllMissionScores()
        {
            var warrior = Make("戦士", JobClass.Warrior, stat: 40, ldr: 20);
            warrior.STR = 60;
            var ranger = Make("斥候", JobClass.Ranger, stat: 30);
            var party = new Party();
            party.TryAdd(warrior);
            party.TryAdd(ranger);

            var metrics = PartyFormationSystem.CalculateMetrics(party);

            Assert.Equal(DungeonPowerCalculator.PartyPower(party.Members), metrics.BossPower, precision: 6);
            Assert.Equal(GatheringResolver.CalculateGatheringScore(party), metrics.GatheringScore, precision: 6);
            Assert.Equal("戦士", metrics.GuardCarrierName);
            Assert.Equal("STR", metrics.GuardCarrierStat);
            Assert.Equal(60, metrics.GuardCarrierValue, precision: 6);
            Assert.Equal(30 * ScoutingBalance.GuardSupportRatio, metrics.GuardSupportPower, precision: 6);
            Assert.Equal(metrics.GuardCarrierValue + metrics.GuardSupportPower, metrics.GuardPower, precision: 6);
            Assert.Equal(20 * ScoutingBalance.StealthWeightLdr, metrics.StealthLeaderBonus, precision: 6);
            Assert.Equal(ScoutingBalance.StealthBonusRangerThief, metrics.StealthSpecialistBonus, precision: 6);
            Assert.Equal(ScoutingBalance.StealthHeavyArmorPenalty, metrics.StealthHeavyPenalty, precision: 6);
        }

        [Fact]
        public void CalculateMetrics_EmptyParty_IsAllZero()
        {
            var metrics = PartyFormationSystem.CalculateMetrics(new Party());

            Assert.Equal(0, metrics.TraversalPower);
            Assert.Equal(0, metrics.BossPower);
            Assert.Equal(0, metrics.GuardPower);
            Assert.Equal(0, metrics.StealthScore);
            Assert.Equal(0, metrics.AnalysisScore);
            Assert.Equal(0, metrics.GatheringScore);
            Assert.Equal("", metrics.GuardCarrierName);
        }

        // ---------------- 隊員1名ごとの貢献値（一覧の貢献列） ----------------

        [Fact]
        public void MemberValues_SumToPartyScores()
        {
            var a = Make("A", stat: 30, ldr: 0);
            var b = Make("B", stat: 45, ldr: 0);
            b.MND = 10;
            var party = new Party();
            party.TryAdd(a);
            party.TryAdd(b);

            // 隊長LDRが0なので、走破力・解析は隊員ごとの値の合計に一致する
            Assert.Equal(DungeonTraversalResolver.GetMemberTraversalValue(a) + DungeonTraversalResolver.GetMemberTraversalValue(b),
                DungeonTraversalResolver.CalculateTraversalScore(party), precision: 6);
            Assert.Equal(ScoutingResolver.GetAnalysisValue(a) + ScoutingResolver.GetAnalysisValue(b),
                ScoutingResolver.CalculateAnalysisScore(party), precision: 6);
            Assert.Equal((ScoutingResolver.GetStealthValue(a) + ScoutingResolver.GetStealthValue(b)) / 2,
                ScoutingResolver.CalculateBaseStealthScore(party), precision: 6);
            Assert.Equal(45, ScoutingResolver.GetGuardValue(b), precision: 6);
            Assert.Equal(30 * GatheringBalance.AgiCoefficient + 30 * GatheringBalance.DexCoefficient,
                GatheringResolver.GetMemberGatheringValue(a), precision: 6);
        }
    }
}
