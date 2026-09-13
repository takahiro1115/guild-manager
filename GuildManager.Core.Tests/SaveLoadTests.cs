using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// セーブ/ロードシステム（仕様書 03 §12）のテスト。
    /// GameState.ToSaveData/FromSaveDataの往復（丸め）と、SaveLoadServiceの
    /// ファイルIO・失敗時フォールバックを検証する。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class SaveLoadTests
    {
        // ---------------- GameState.ToSaveData / FromSaveData（往復） ----------------

        [Fact]
        public void RoundTrip_PreservesBasicScalars()
        {
            var state = new GameState
            {
                WeekNumber = 42,
                Gold = 12345,
                Reputation = 987,
                GuildRank = GuildRank.B,
                ThreatLevel = 30,
                ConsecutiveNegativeGoldWeeks = 2,
                DefeatReason = null,
                WeeksSinceLastRankAppropriateQuest = 5,
            };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(42, restored.WeekNumber);
            Assert.Equal(12345, restored.Gold);
            Assert.Equal(987, restored.Reputation);
            Assert.Equal(GuildRank.B, restored.GuildRank);
            Assert.Equal(30, restored.ThreatLevel);
            Assert.Equal(2, restored.ConsecutiveNegativeGoldWeeks);
            Assert.Null(restored.DefeatReason);
            Assert.Equal(5, restored.WeeksSinceLastRankAppropriateQuest);
        }

        [Fact]
        public void RoundTrip_PreservesFinalQuestUnlocked_WhenTrue()
        {
            // v1.10改訂で新設されたフィールド（→ 03 §8.2）。v1.8作成時点ではGameStateに
            // 存在しなかったため、SaveDataへの反映漏れが無いか確認する項目。
            var state = new GameState { FinalQuestUnlocked = true };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.True(restored.FinalQuestUnlocked);
        }

        [Fact]
        public void RoundTrip_FinalQuestUnlocked_DefaultsToFalse_ForNewGame()
        {
            var state = new GameState();

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.False(restored.FinalQuestUnlocked);
        }

        [Fact]
        public void RoundTrip_PreservesDefeatReason_WhenAlreadyDefeated()
        {
            // 指示書のSaveDataサンプルにDefeatReasonが含まれていなかったため追加した項目。
            // 省略するとロードのたびに敗北状態が解除されてしまう（実質的なチート）。
            var state = new GameState { DefeatReason = DefeatReason.Bankruptcy };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(DefeatReason.Bankruptcy, restored.DefeatReason);
        }

        [Fact]
        public void RoundTrip_PreservesActiveRetiredAndFallenAdventurers()
        {
            var active = new Adventurer { Name = "現役", STR = 50 };
            var retired = new Adventurer { Name = "引退", STR = 80 };
            retired.PeakSTR = 90; // 生涯ピーク値（顧問効果算出の基準）を明示的に記録
            var fallen = new Adventurer { Name = "戦死", FellAtWeek = 10 };

            var state = new GameState
            {
                Adventurers = { active },
                RetiredAdventurers = { retired },
                FallenAdventurers = { fallen },
            };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Single(restored.Adventurers);
            Assert.Equal("現役", restored.Adventurers[0].Name);
            Assert.Single(restored.RetiredAdventurers);
            Assert.Equal("引退", restored.RetiredAdventurers[0].Name);
            // 生涯ピーク値が失われていないことを確認（顧問効果算出に必須。→ 実装コメント参照）
            Assert.Equal(90, restored.RetiredAdventurers[0].PeakSTR);
            Assert.Single(restored.FallenAdventurers);
            Assert.Equal(10, restored.FallenAdventurers[0].FellAtWeek);
        }

        /// <summary>
        /// ポートレート画像ID（→ 03 §2.1、項目62で新設）のセーブ/ロード往復を確認する。
        /// SaveData.ActiveAdventurersはAdventurer型をそのまま保持する設計のため、
        /// マッピングコードの追記は不要のはずだが、新規永続データのため専用テストで
        /// 明示的に確認する（採用組が持つ「未設定（null）」のケースも合わせて検証）。
        /// </summary>
        [Fact]
        public void RoundTrip_PreservesPortraitId_WhenSet()
        {
            var withPortrait = new Adventurer { Name = "ガレス", PortraitId = "gareth" };
            var state = new GameState { Adventurers = { withPortrait } };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal("gareth", restored.Adventurers[0].PortraitId);
        }

        [Fact]
        public void RoundTrip_PortraitId_StaysNull_WhenUnset()
        {
            var recruited = new Adventurer { Name = "新人" }; // PortraitId未設定＝採用組の通常ケース
            var state = new GameState { Adventurers = { recruited } };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Null(restored.Adventurers[0].PortraitId);
        }

        [Fact]
        public void RoundTrip_PreservesCompatibilityPairs_WithNormalizedKey()
        {
            var a = new Adventurer();
            var b = new Adventurer();
            var state = new GameState { Adventurers = { a, b } };
            var key = a.Id.CompareTo(b.Id) <= 0 ? (a.Id, b.Id) : (b.Id, a.Id);
            state.Compatibility[key] = 75;

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(75, CompatibilitySystem.GetCompatibility(restored, a.Id, b.Id));
            // 引数の順序を入れ替えても同じ値が引けること（正規化キーの往復確認）
            Assert.Equal(75, CompatibilitySystem.GetCompatibility(restored, b.Id, a.Id));
        }

        [Fact]
        public void RoundTrip_PreservesTrainingAssignments()
        {
            var adventurer = new Adventurer();
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments[adventurer.Id] = FacilityType.WarriorHall;

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(FacilityType.WarriorHall, restored.TrainingAssignments[adventurer.Id]);
        }

        [Fact]
        public void RoundTrip_PreservesFacilityLevels()
        {
            var state = new GameState();
            foreach (var f in state.Facilities)
                if (f.Type == FacilityType.WarriorHall) f.CurrentLevel = 3;

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(9, restored.Facilities.Count);
            Assert.Equal(3, restored.GetFacilityLevel(FacilityType.WarriorHall));
            Assert.Equal(1, restored.GetFacilityLevel(FacilityType.Dormitory)); // 変更していない施設もそのまま
            Assert.Equal(0, restored.GetFacilityLevel(FacilityType.RecruitmentOffice)); // Lv0のままの施設も保持される
        }

        [Fact]
        public void RoundTrip_PreservesUnderConstruction()
        {
            var state = new GameState();
            state.UnderConstruction = new FacilityConstruction { Type = FacilityType.Tavern, TargetLevel = 2, WeeksRemaining = 3 };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.NotNull(restored.UnderConstruction);
            Assert.Equal(FacilityType.Tavern, restored.UnderConstruction!.Type);
            Assert.Equal(2, restored.UnderConstruction.TargetLevel);
            Assert.Equal(3, restored.UnderConstruction.WeeksRemaining);
        }

        [Fact]
        public void RoundTrip_NullUnderConstruction_StaysNull_WhenNothingUnderConstruction()
        {
            var state = new GameState();

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Null(restored.UnderConstruction);
        }

        [Fact]
        public void RoundTrip_PreservesTrainerAdvisorAndScoutMasterAssignments()
        {
            // 教官(施設ごと)・参謀(WarRoom)・スカウト(RecruitmentOffice)が
            // 単一のAdvisorAssignments辞書に統合され、正しく分配復元されることを確認する。
            var trainer = new Adventurer { Name = "教官" };
            var advisor = new Adventurer { Name = "参謀" };
            var scoutMaster = new Adventurer { Name = "スカウト" };
            var state = new GameState { RetiredAdventurers = { trainer, advisor, scoutMaster } };
            state.AssignedTrainers[FacilityType.WarriorHall] = trainer.Id;
            state.AssignedAdvisor = advisor.Id;
            state.AssignedScoutMaster = scoutMaster.Id;

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(trainer.Id, restored.AssignedTrainers[FacilityType.WarriorHall]);
            Assert.Equal(advisor.Id, restored.AssignedAdvisor);
            Assert.Equal(scoutMaster.Id, restored.AssignedScoutMaster);
        }

        [Fact]
        public void RoundTrip_NoAdvisorAssignments_WhenNoneAssigned()
        {
            var state = new GameState();

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Empty(restored.AssignedTrainers);
            Assert.Null(restored.AssignedAdvisor);
            Assert.Null(restored.AssignedScoutMaster);
        }

        [Fact]
        public void RoundTrip_PreservesAvailableQuests()
        {
            var state = new GameState
            {
                AvailableQuests = { new Quest { Name = "ゴブリン討伐", Difficulty = 20, RewardGold = 100 } },
            };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Single(restored.AvailableQuests);
            Assert.Equal("ゴブリン討伐", restored.AvailableQuests[0].Name);
            Assert.Equal(20, restored.AvailableQuests[0].Difficulty);
            Assert.Equal(100, restored.AvailableQuests[0].RewardGold);
        }

        [Fact]
        public void RoundTrip_PreservesDispatchedQuest_IncludingFullQuestData()
        {
            // 派遣されたクエストはDispatch時にAvailableQuestsから除去されるため、
            // DispatchedQuestRecordがクエスト本体（Id以外の全データ）を保持している
            // ことを確認する（指示書のQuestIdのみの設計だと復元できないため変更した点）。
            var member = new Adventurer { Name = "メンバー" };
            var quest = new Quest { Name = "大規模討伐", Difficulty = 80, RewardGold = 500, Scale = QuestScale.Large };
            var state = new GameState { Adventurers = { member } };
            var party = new Party();
            party.TryAdd(member);
            state.ActiveDispatches.Add(new ActiveDispatch { Party = party, Quest = quest, WeeksRemaining = 2 });

            var restored = GameState.FromSaveData(state.ToSaveData());

            var dispatch = Assert.Single(restored.ActiveDispatches);
            Assert.Equal("大規模討伐", dispatch.Quest.Name);
            Assert.Equal(80, dispatch.Quest.Difficulty);
            Assert.Equal(500, dispatch.Quest.RewardGold);
            Assert.Equal(2, dispatch.WeeksRemaining);
            Assert.Single(dispatch.Party.Members);
            Assert.Equal("メンバー", dispatch.Party.Members[0].Name);
        }

        [Fact]
        public void RoundTrip_DispatchedPartyMember_IsSameInstance_AsInActiveAdventurers()
        {
            // 派遣中パーティのメンバーと現役ロースターの同一人物が、復元後も
            // 同じインスタンス（参照）になっていることを確認する（状態の二重管理を防ぐため）。
            var member = new Adventurer { Name = "メンバー" };
            var state = new GameState { Adventurers = { member } };
            var party = new Party();
            party.TryAdd(member);
            state.ActiveDispatches.Add(new ActiveDispatch { Party = party, Quest = new Quest(), WeeksRemaining = 1 });

            var restored = GameState.FromSaveData(state.ToSaveData());

            var rosterMember = restored.Adventurers.Single();
            var partyMember = restored.ActiveDispatches.Single().Party.Members.Single();
            Assert.Same(rosterMember, partyMember);
        }

        // ---------------- 永続パーティー編成（→ 03 §4.0.2、v1.9改訂：項目53「v1.8作成時の漏れ」対応） ----------------

        [Fact]
        public void RoundTrip_PreservesSavedParties()
        {
            var a = new Adventurer { Name = "甲" };
            var b = new Adventurer { Name = "乙" };
            var state = new GameState { Adventurers = { a, b } };
            var party = new PartyFormationSystem().CreateParty(state, "第一遠征隊");
            party.MemberIds.Add(a.Id);
            party.MemberIds.Add(b.Id);

            var restored = GameState.FromSaveData(state.ToSaveData());

            var restoredParty = Assert.Single(restored.SavedParties);
            Assert.Equal("第一遠征隊", restoredParty.Name);
            Assert.Equal(party.Id, restoredParty.Id);
            Assert.Equal(new[] { a.Id, b.Id }, restoredParty.MemberIds);
        }

        [Fact]
        public void RoundTrip_PreservesMultipleSavedParties_Independently()
        {
            var a = new Adventurer();
            var b = new Adventurer();
            var state = new GameState { Adventurers = { a, b } };
            var system = new PartyFormationSystem();
            var partyA = system.CreateParty(state, "A隊");
            var partyB = system.CreateParty(state, "B隊");
            system.TryAssignMember(state, partyA, a.Id);
            system.TryAssignMember(state, partyB, b.Id);

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(2, restored.SavedParties.Count);
            Assert.Contains(restored.SavedParties, p => p.Name == "A隊" && p.MemberIds.Contains(a.Id));
            Assert.Contains(restored.SavedParties, p => p.Name == "B隊" && p.MemberIds.Contains(b.Id));
        }

        [Fact]
        public void RoundTrip_SavedParties_Empty_WhenNoneCreated()
        {
            var state = new GameState();

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Empty(restored.SavedParties);
        }

        [Fact]
        public void RoundTrip_DispatchedQuest_IsIndependent_OfSavedParties()
        {
            // 派遣中のパーティー（DispatchedQuestRecord）は出撃時点のメンバーIdの
            // スナップショットであり、SavedParty.Idへの参照は持たない。そのため、
            // SavedPartyを削除・編集しても進行中の派遣には一切影響しないことを確認する。
            var a = new Adventurer { Name = "遠征中メンバー" };
            var state = new GameState { Adventurers = { a } };
            var system = new PartyFormationSystem();
            var savedParty = system.CreateParty(state, "元の編成");
            system.TryAssignMember(state, savedParty, a.Id);

            var dispatchParty = new Party();
            dispatchParty.TryAdd(a);
            state.ActiveDispatches.Add(new ActiveDispatch { Party = dispatchParty, Quest = new Quest { Name = "遠征" }, WeeksRemaining = 3 });

            // 派遣後にSavedPartyを削除（実運用では有り得るが、影響してはいけない）
            system.DeleteParty(state, savedParty);

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Empty(restored.SavedParties); // SavedPartyは消えている
            var dispatch = Assert.Single(restored.ActiveDispatches); // が、派遣中クエストは無事に残る
            Assert.Equal("遠征中メンバー", dispatch.Party.Members.Single().Name);
        }

        [Fact]
        public void FromSaveData_Throws_ForInvalidGuildRankString()
        {
            var data = new GameState().ToSaveData();
            data.GuildRank = "NotARealRank";

            Assert.Throws<FormatException>(() => GameState.FromSaveData(data));
        }

        [Fact]
        public void FromSaveData_Throws_ForInvalidFacilityTypeString()
        {
            var data = new GameState().ToSaveData();
            data.FacilityLevels.Clear();
            data.FacilityLevels["NotARealFacility"] = 1;

            Assert.Throws<FormatException>(() => GameState.FromSaveData(data));
        }

        [Fact]
        public void FromSaveData_Throws_WhenDispatchedPartyMemberIdIsMissing()
        {
            var data = new GameState().ToSaveData();
            data.DispatchedQuests.Add(new DispatchedQuestRecord
            {
                Quest = new Quest(),
                PartyMemberIds = { Guid.NewGuid() }, // どのリストにも存在しないId
                WeeksRemaining = 1,
            });

            Assert.Throws<FormatException>(() => GameState.FromSaveData(data));
        }

        // ---------------- JSON実シリアライズの往復（System.Text.Json自体の互換性確認） ----------------

        [Fact]
        public void SaveData_SurvivesActualJsonSerialization_RoundTrip()
        {
            var member = new Adventurer { Name = "冒険者A", STR = 42, TraitIds = { TraitCatalog.OldWoundId } };
            var state = new GameState { Adventurers = { member }, Gold = 5000 };
            state.TrainingAssignments[member.Id] = FacilityType.Church;

            var json = JsonSerializer.Serialize(state.ToSaveData(), new JsonSerializerOptions { WriteIndented = true });
            var deserialized = JsonSerializer.Deserialize<SaveData>(json);
            var restored = GameState.FromSaveData(deserialized!);

            Assert.Equal(5000, restored.Gold);
            Assert.Single(restored.Adventurers);
            Assert.Equal("冒険者A", restored.Adventurers[0].Name);
            Assert.Equal(42, restored.Adventurers[0].STR);
            Assert.True(restored.Adventurers[0].HasTrait(TraitCatalog.OldWoundId));
            Assert.Equal(FacilityType.Church, restored.TrainingAssignments[member.Id]);
        }

        // ---------------- SaveLoadService（ファイルIO） ----------------

        private static string CreateTempSaveDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "GuildManagerSaveLoadTests_" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void SaveFileExists_ReturnsFalse_WhenNoSaveHasBeenMade()
        {
            var dir = CreateTempSaveDirectory();
            try
            {
                var service = new SaveLoadService(dir);
                Assert.False(service.SaveFileExists());
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Save_ThenLoad_RoundTripsGameStateViaRealFile()
        {
            var dir = CreateTempSaveDirectory();
            try
            {
                var service = new SaveLoadService(dir);
                var state = new GameState { WeekNumber = 10, Gold = 7777, GuildRank = GuildRank.C };

                service.Save(state);

                Assert.True(service.SaveFileExists());
                var loaded = service.Load();
                Assert.NotNull(loaded);
                Assert.Equal(10, loaded!.WeekNumber);
                Assert.Equal(7777, loaded.Gold);
                Assert.Equal(GuildRank.C, loaded.GuildRank);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Save_Overwrites_PreviousSave_SingleSlot()
        {
            var dir = CreateTempSaveDirectory();
            try
            {
                var service = new SaveLoadService(dir);
                service.Save(new GameState { WeekNumber = 1 });
                service.Save(new GameState { WeekNumber = 99 });

                var loaded = service.Load();

                Assert.Equal(99, loaded!.WeekNumber);
                // 単一スロット：セーブファイルは1つだけのはず
                Assert.Single(Directory.GetFiles(dir));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Load_ReturnsNull_WhenSaveFileDoesNotExist()
        {
            var dir = CreateTempSaveDirectory();
            try
            {
                var service = new SaveLoadService(dir);
                Assert.Null(service.Load());
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Load_ReturnsNull_ForCorruptedJson()
        {
            var dir = CreateTempSaveDirectory();
            try
            {
                File.WriteAllText(Path.Combine(dir, "savegame.json"), "{ this is not valid json !!!");
                var service = new SaveLoadService(dir);

                Assert.Null(service.Load());
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Load_ReturnsNull_ForMismatchedSaveVersion()
        {
            var dir = CreateTempSaveDirectory();
            try
            {
                var data = new GameState().ToSaveData();
                data.SaveVersion = SaveLoadService.CurrentSaveVersion + 1; // 未来のバージョン
                File.WriteAllText(Path.Combine(dir, "savegame.json"), JsonSerializer.Serialize(data));
                var service = new SaveLoadService(dir);

                Assert.Null(service.Load());
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Load_ReturnsNull_ForInvalidEnumInsideSavedFile()
        {
            var dir = CreateTempSaveDirectory();
            try
            {
                var data = new GameState().ToSaveData();
                data.GuildRank = "GarbageRank";
                File.WriteAllText(Path.Combine(dir, "savegame.json"), JsonSerializer.Serialize(data));
                var service = new SaveLoadService(dir);

                Assert.Null(service.Load()); // GameState.FromSaveDataのFormatExceptionを捕捉して null
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Load_DoesNotDeleteOrOverwrite_CorruptedSaveFile()
        {
            // ロード失敗時に既存セーブを保護する（→ 03 §12「既存セーブを上書きしない」）ことの確認。
            var dir = CreateTempSaveDirectory();
            try
            {
                var path = Path.Combine(dir, "savegame.json");
                const string corrupted = "{ not valid json";
                File.WriteAllText(path, corrupted);
                var service = new SaveLoadService(dir);

                service.Load();

                Assert.Equal(corrupted, File.ReadAllText(path)); // ファイル内容が変化していない
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }
}
