using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
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
                MasterMood = 73,
                ConsecutiveNegativeGoldWeeks = 2,
                DefeatReason = null,
                WeeksSinceLastGuildActivity = 5,
            };

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(42, restored.WeekNumber);
            Assert.Equal(12345, restored.Gold);
            Assert.Equal(73, restored.MasterMood);
            Assert.Equal(2, restored.ConsecutiveNegativeGoldWeeks);
            Assert.Null(restored.DefeatReason);
            Assert.Equal(5, restored.WeeksSinceLastGuildActivity);
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
        public void LegacySave_WithPeakStatFields_StillDeserializes()
        {
            // 生涯ピーク値（PeakSTR等）撤廃前のセーブには Peak* 項目が残っている。
            // 未知の項目として無視され、実効ステータスはそのまま読み込めること。
            var json = "{\"Name\":\"古参の顧問\",\"STR\":80,\"LDR\":60,\"PeakSTR\":95,\"PeakLDR\":70}";

            var restored = JsonSerializer.Deserialize<Adventurer>(json)!;

            Assert.Equal("古参の顧問", restored.Name);
            Assert.Equal(80, restored.STR);
            Assert.Equal(60, restored.LDR);
        }

        [Fact]
        public void RoundTrip_PreservesActiveRetiredAndFallenAdventurers()
        {
            var active = new Adventurer { Name = "現役", STR = 50 };
            var retired = new Adventurer { Name = "引退", STR = 80 };
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
            // 引退時の実効ステータスが失われていないことを確認（顧問効果算出に必須。→ 実装コメント参照）
            Assert.Equal(80, restored.RetiredAdventurers[0].STR);
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
        public void RoundTrip_DispatchedPartyMember_IsSameInstance_AsInActiveAdventurers()
        {
            // 大迷宮へ出撃中の部隊のメンバーと現役ロースターの同一人物が、復元後も
            // 同じインスタンス（参照）になっていることを確認する（状態の二重管理を防ぐため）。
            var member = new Adventurer { Name = "メンバー" };
            var state = new GameState { Adventurers = { member }, DungeonFields = SampleData.CreateDefaultFields() };
            var party = new Party();
            party.TryAdd(member);
            state.ActiveDungeonMissions.Add(new ActiveDungeonMission
            {
                Party = party, Field = state.DungeonFields[0], MissionType = DungeonMissionType.Gathering,
            });

            var restored = GameState.FromSaveData(state.ToSaveData());

            var rosterMember = restored.Adventurers.Single();
            var partyMember = restored.ActiveDungeonMissions.Single().Party.Members.Single();
            Assert.Same(rosterMember, partyMember);
        }

        [Fact]
        public void LegacySave_WithRemovedQuestFields_StillLoads()
        {
            // 旧通常クエストの撤去（2026年9月）以前のセーブには、受注可能クエスト・派遣中クエスト・
            // 昇格試験フラグが残っている。未知の項目として無視され、残りはそのまま読み込めること。
            var data = new GameState { Gold = 777 }.ToSaveData();
            var json = JsonSerializer.Serialize(data);
            json = json.TrimEnd('}') +
                ",\"AvailableQuests\":[{\"Name\":\"ゴブリン討伐\",\"Difficulty\":10}]" +
                ",\"DispatchedQuests\":[{\"Quest\":{\"Name\":\"遠征\"},\"PartyMemberIds\":[],\"WeeksRemaining\":2}]" +
                ",\"PromotionExamOffered\":true,\"PromotionExamPassed\":false}";

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            Assert.Equal(777, restored.Gold);
            Assert.Empty(restored.ActiveDungeonMissions);
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
        public void RoundTrip_DungeonMission_IsIndependent_OfSavedParties()
        {
            // 出撃中の部隊（DungeonMissionRecord）は出撃時点のメンバーIdのスナップショットであり、
            // SavedParty.Idへの参照は持たない。そのため、SavedPartyを削除・編集しても
            // 出撃中の部隊には一切影響しないことを確認する。
            var a = new Adventurer { Name = "遠征中メンバー" };
            var state = new GameState { Adventurers = { a }, DungeonFields = SampleData.CreateDefaultFields() };
            var system = new PartyFormationSystem();
            var savedParty = system.CreateParty(state, "元の編成");
            system.TryAssignMember(state, savedParty, a.Id);

            var dispatchParty = new Party();
            dispatchParty.TryAdd(a);
            state.ActiveDungeonMissions.Add(new ActiveDungeonMission
            {
                Party = dispatchParty, Field = state.DungeonFields[0], MissionType = DungeonMissionType.Gathering,
            });

            // 出撃後にSavedPartyを削除（実運用では有り得るが、影響してはいけない）
            system.DeleteParty(state, savedParty);

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Empty(restored.SavedParties); // SavedPartyは消えている
            var mission = Assert.Single(restored.ActiveDungeonMissions); // が、出撃中の部隊は無事に残る
            Assert.Equal("遠征中メンバー", mission.Party.Members.Single().Name);
        }

        [Fact]
        public void FromSaveData_Throws_ForInvalidDefeatReasonString()
        {
            var data = new GameState().ToSaveData();
            data.DefeatReason = "NotARealReason";

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
        public void FromSaveData_Throws_WhenDungeonMissionMemberIdIsMissing()
        {
            var data = new GameState { DungeonFields = SampleData.CreateDefaultFields() }.ToSaveData();
            data.DungeonMissions.Add(new DungeonMissionRecord
            {
                FieldId = data.DungeonFields[0].Id,
                MissionType = DungeonMissionType.Gathering.ToString(),
                PartyMemberIds = { Guid.NewGuid() }, // どのリストにも存在しないId
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
                var state = new GameState { WeekNumber = 10, Gold = 7777, MasterMood = 77 };

                service.Save(state);

                Assert.True(service.SaveFileExists());
                var loaded = service.Load();
                Assert.NotNull(loaded);
                Assert.Equal(10, loaded!.WeekNumber);
                Assert.Equal(7777, loaded.Gold);
                Assert.Equal(77, loaded.MasterMood);
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
                data.DefeatReason = "GarbageReason";
                File.WriteAllText(Path.Combine(dir, "savegame.json"), JsonSerializer.Serialize(data));
                var service = new SaveLoadService(dir);

                Assert.Null(service.Load()); // GameState.FromSaveDataのFormatExceptionを捕捉して null
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Load_RestoresOldSave_WithReputationAndGuildRank_InitializesMasterMood()
        {
            // 名声・ギルド格付けの時代（2026年9月以前）のセーブ：MasterMood・WeeksSinceLastGuildActivity を持たず、
            // Reputation・GuildRank・WeeksSinceLastRankAppropriateQuest を持つ。例外なく読み込めて、
            // 機嫌は初期値（平常50）、経過週数は旧キーの値を引き継ぐこと。
            var dir = CreateTempSaveDirectory();
            try
            {
                var json = JsonSerializer.Serialize(new GameState { WeekNumber = 23, Gold = 999 }.ToSaveData());
                using (var doc = JsonDocument.Parse(json))
                {
                    var legacy = doc.RootElement.EnumerateObject()
                        .Where(p => p.Name is not ("MasterMood" or "WeeksSinceLastGuildActivity"))
                        .ToDictionary(p => p.Name, p => (object)p.Value.Clone());
                    legacy["Reputation"] = 1234;
                    legacy["GuildRank"] = "B";
                    legacy["WeeksSinceLastRankAppropriateQuest"] = 6;
                    json = JsonSerializer.Serialize(legacy);
                }
                Assert.DoesNotContain("MasterMood", json);
                File.WriteAllText(Path.Combine(dir, "savegame.json"), json);

                var restored = new SaveLoadService(dir).Load();

                Assert.NotNull(restored);
                Assert.Equal(23, restored!.WeekNumber);
                Assert.Equal(999, restored.Gold);
                Assert.Equal(MasterMoodBalance.InitialMood, restored.MasterMood);
                Assert.Equal(50, restored.MasterMood);
                Assert.Equal(6, restored.WeeksSinceLastGuildActivity);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void ToSaveData_DoesNotWriteLegacyActivityKey()
        {
            var json = JsonSerializer.Serialize(new GameState { WeeksSinceLastGuildActivity = 2 }.ToSaveData());

            Assert.DoesNotContain("WeeksSinceLastRankAppropriateQuest", json);
            Assert.DoesNotContain("Reputation", json);
            Assert.Contains("\"WeeksSinceLastGuildActivity\":2", json);
        }

        [Theory]
        [InlineData("ThreatLevel")]
        [InlineData("threat_level")]
        public void Load_RestoresOldSave_ContainingRemovedThreatLevelField(string removedFieldName)
        {
            // 脅威度システムの撤去（2026年9月）で SaveData.ThreatLevel を削除した。
            // 撤去前に作られたセーブには当該フィールドが残っているため、未知のメンバーとして
            // 黙って無視され、他の値が正常に復元されることを保証する（→ 03 §12のセーブ互換性方針）。
            var dir = CreateTempSaveDirectory();
            try
            {
                var data = new GameState
                {
                    WeekNumber = 17,
                    Gold = 4321,
                    MasterMood = 33,
                    ConsecutiveNegativeGoldWeeks = 1,
                }.ToSaveData();

                // 旧形式を再現：シリアライズ済みJSONへ撤去済みフィールドを差し込む。
                var json = JsonSerializer.Serialize(data);
                json = "{" + $"\"{removedFieldName}\":75," + json.Substring(1);
                File.WriteAllText(Path.Combine(dir, "savegame.json"), json);
                var service = new SaveLoadService(dir);

                var restored = service.Load();

                Assert.NotNull(restored);
                Assert.Equal(17, restored!.WeekNumber);
                Assert.Equal(4321, restored.Gold);
                Assert.Equal(33, restored.MasterMood);
                Assert.Equal(1, restored.ConsecutiveNegativeGoldWeeks);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Theory]
        [InlineData("\"Mature\"")]
        [InlineData("\"Declining\"")]
        [InlineData("2")] // 旧列挙子 MaturePeriod の数値表現
        [InlineData("3")] // 旧列挙子 LimitPeriod の数値表現
        public void Load_RestoresOldSave_ContainingRemovedAgeBandValue(string legacyAgeBandJson)
        {
            // 8年稼働モデルで到達不能になった円熟期・限界期を撤去した（→ 03 §0.11）。
            // AgeBandは年齢から求める読み取り専用プロパティのため、旧セーブに残る値は
            // デシリアライズ時に無視され、年齢から算出し直される（27歳以上は全盛期へクランプ）。
            var dir = CreateTempSaveDirectory();
            try
            {
                var veteran = new Adventurer { Name = "老練", Age = 30 };
                var data = new GameState { Adventurers = { veteran } }.ToSaveData();

                // 旧形式を再現：冒険者オブジェクトへ撤去済みの年齢帯の値を差し込む。
                var json = JsonSerializer.Serialize(data)
                    .Replace("\"Age\":30", $"\"Age\":30,\"AgeBand\":{legacyAgeBandJson}");
                File.WriteAllText(Path.Combine(dir, "savegame.json"), json);
                var service = new SaveLoadService(dir);

                var restored = service.Load();

                Assert.NotNull(restored);
                var loaded = Assert.Single(restored!.Adventurers);
                Assert.Equal(30, loaded.Age);
                Assert.Equal(AgeBand.Peak, loaded.AgeBand);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        // ---------------- 未鑑定遺物・ギルド保管庫（→ 03 §4.7、2026年9月新設） ----------------

        [Fact]
        public void RoundTrip_PreservesUnidentifiedItemsAndArmory()
        {
            var state = new GameState { Gold = 5000 };
            state.UnidentifiedItems.Add(new UnidentifiedItem
            {
                Name = "？？？ 歪んだ古代装具", Rarity = ItemRarity.Legendary,
                OriginFieldId = "abyss", OriginFloor = 70, AppraisalCost = 1200,
            });
            state.Armory.Add(EquipmentItem.FromCatalog(ItemCatalog.HeavyArmor, acquiredAtWeek: 40, acquiredFrom: "鑑定"));

            // 実際のJSONシリアライズを通しても往復できること（EquipmentItem.GetDefinitionを
            // プロパティではなくメソッドにしてあるのは、カタログ定義がJSONへ書き出されるのを
            // 避けるため。→ Models.EquipmentItem）。
            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var relic = Assert.Single(restored.UnidentifiedItems);
            Assert.Equal(ItemRarity.Legendary, relic.Rarity);
            Assert.Equal("abyss", relic.OriginFieldId);
            Assert.Equal(70, relic.OriginFloor);
            Assert.Equal(1200, relic.AppraisalCost);

            var equipment = Assert.Single(restored.Armory);
            Assert.Equal(ItemCatalog.HeavyArmorId, equipment.ItemId);
            Assert.Equal("重装鎧", equipment.Name);
            Assert.Equal(40, equipment.AcquiredAtWeek);
        }

        [Fact]
        public void LegacySave_WithoutUnidentifiedItemsAndArmory_StillLoads()
        {
            // 遺物・保管庫の新設（2026年9月）以前のセーブには unidentified_items / armory に
            // 相当するキーが存在しない。System.Text.Jsonはプロパティ初期化子の空リストを
            // そのまま残すため、空在庫として正常に復元されること（→ 03 §12の互換性方針）。
            var dir = CreateTempSaveDirectory();
            try
            {
                var data = new GameState { WeekNumber = 23, Gold = 8888 }.ToSaveData();
                var json = JsonSerializer.Serialize(data);

                // シリアライズ済みJSONから当該キーを丸ごと取り除き、旧形式を再現する。
                json = json.Replace("\"UnidentifiedItems\":[],", "").Replace("\"Armory\":[],", "");
                Assert.DoesNotContain("\"UnidentifiedItems\"", json);
                Assert.DoesNotContain("\"Armory\"", json);

                File.WriteAllText(Path.Combine(dir, "savegame.json"), json);
                var restored = new SaveLoadService(dir).Load();

                Assert.NotNull(restored);
                Assert.Equal(23, restored!.WeekNumber);
                Assert.Equal(8888, restored.Gold);
                Assert.Empty(restored.UnidentifiedItems);
                Assert.Empty(restored.Armory);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        // ---------------- 冒険者の装備状態（→ 03 §4.2.2、2026年9月改訂：個体保持化） ----------------

        [Fact]
        public void RoundTrip_PreservesAdventurerEquipment_AndArmoryStock()
        {
            var adventurer = new Adventurer { Name = "甲", JobClass = JobClass.Warrior, VIT = 40 };
            adventurer.CurrentHP = adventurer.MaxHP;
            var state = new GameState { Adventurers = { adventurer } };
            var system = new EquipmentSystem();

            // 保管庫の在庫から3枠へ装備し、1点を在庫に残す。
            var weapon = EquipmentItem.FromCatalog(ItemCatalog.IronSword, acquiredAtWeek: 3, acquiredFrom: "鑑定");
            var armor = EquipmentItem.FromCatalog(ItemCatalog.LeatherArmor, acquiredAtWeek: 4, acquiredFrom: "鑑定");
            var accessory = EquipmentItem.FromCatalog(ItemCatalog.PowerRing, acquiredAtWeek: 5, acquiredFrom: "鑑定");
            var spare = EquipmentItem.FromCatalog(ItemCatalog.GuardCharm, acquiredAtWeek: 6, acquiredFrom: "鑑定");
            state.Armory.AddRange(new[] { weapon, armor, accessory, spare });
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, weapon));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Armor, armor));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Accessory1, accessory));

            int maxHpBefore = adventurer.MaxHP;
            int strBonusBefore = adventurer.GetEquipmentStatBonus("STR");

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var loaded = Assert.Single(restored.Adventurers);
            Assert.Equal(ItemCatalog.IronSwordId, loaded.EquippedWeaponId);
            Assert.Equal(ItemCatalog.LeatherArmorId, loaded.EquippedArmorId);
            Assert.Equal(ItemCatalog.PowerRingId, loaded.EquippedAccessory1Id);
            Assert.Null(loaded.EquippedAccessory2Id);

            // 個体の識別子・入手履歴まで保たれる（→ EquipmentItem）。
            Assert.Equal(weapon.Id, loaded.EquippedWeapon!.Id);
            Assert.Equal(3, loaded.EquippedWeapon.AcquiredAtWeek);
            Assert.Equal("鑑定", loaded.EquippedWeapon.AcquiredFrom);

            // 装備補正も復元後に同じ値になる。
            Assert.Equal(maxHpBefore, loaded.MaxHP);
            Assert.Equal(strBonusBefore, loaded.GetEquipmentStatBonus("STR"));

            // 保管庫に残した在庫はそのまま1点。装備中の3点が保管庫に二重計上されていないこと。
            var stock = Assert.Single(restored.Armory);
            Assert.Equal(ItemCatalog.GuardCharmId, stock.ItemId);
        }

        [Fact]
        public void RoundTrip_EquipmentStaysNull_WhenNothingEquipped()
        {
            var state = new GameState { Adventurers = { new Adventurer { Name = "乙" } } };

            var restored = GameState.FromSaveData(state.ToSaveData());

            var loaded = Assert.Single(restored.Adventurers);
            foreach (var slot in Adventurer.AllSlots)
                Assert.Null(loaded.GetEquipped(slot));
        }

        [Fact]
        public void LegacySave_WithEquippedItemIdStrings_RestoresEquipmentInstances()
        {
            // 2026年9月の個体保持化より前のセーブは、各スロットをカタログId文字列
            // （"EquippedWeaponId":"IronSword"）で持っている。Adventurer側のセット専用
            // プロパティ（Legacy*）がカタログから個体を復元すること（→ 03 §12）。
            var data = new GameState { Adventurers = { new Adventurer { JobClass = JobClass.Warrior, VIT = 40 } } }
                .ToSaveData();
            var json = JsonSerializer.Serialize(data);

            // 旧形式を再現：新形式の個体キーを、旧形式のId文字列キーへ置き換える
            // （アンカーにする文字列はASCIIに限る：System.Text.Jsonは既定で非ASCIIを
            // \uXXXX へエスケープするため、日本語の値では一致しない）。
            json = json
                .Replace("\"EquippedWeapon\":null", "\"EquippedWeaponId\":\"IronSword\"")
                .Replace("\"EquippedArmor\":null", "\"EquippedArmorId\":\"LeatherArmor\"")
                .Replace("\"EquippedAccessory1\":null", "\"EquippedAccessory1Id\":\"PowerRing\"")
                .Replace("\"EquippedAccessory2\":null", "\"EquippedAccessory2Id\":null");
            Assert.Contains("\"EquippedWeaponId\":\"IronSword\"", json);
            Assert.DoesNotContain("\"EquippedWeapon\":", json);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var loaded = Assert.Single(restored.Adventurers);
            Assert.Equal(ItemCatalog.IronSwordId, loaded.EquippedWeaponId);
            Assert.Equal(ItemCatalog.LeatherArmorId, loaded.EquippedArmorId);
            Assert.Equal(ItemCatalog.PowerRingId, loaded.EquippedAccessory1Id);
            Assert.Null(loaded.EquippedAccessory2Id);
            Assert.Equal("鉄の剣", loaded.EquippedWeapon!.Name);
            // 装備補正も旧セーブどおりに効く。
            Assert.Equal(ItemCatalog.LeatherArmor.MaxHpBonus, loaded.GetEquipmentHpBonus());
        }

        [Fact]
        public void NewSave_DoesNotWriteLegacyEquippedIdKeys()
        {
            // 正本は個体（EquippedWeapon）であり、派生値であるIdはセーブへ書き出さない
            // （二重管理を避けるため。→ Adventurer の JsonIgnore）。
            var adventurer = new Adventurer { Name = "丁", JobClass = JobClass.Warrior, VIT = 40 };
            var state = new GameState { Gold = 10000, Adventurers = { adventurer } };
            Assert.True(new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId));

            var json = JsonSerializer.Serialize(state.ToSaveData());

            Assert.DoesNotContain("EquippedWeaponId", json);
            Assert.Contains("EquippedWeapon", json); // 個体そのものは書き出される
        }

        [Fact]
        public void LegacySave_WithUnknownEquippedItemId_LoadsWithEmptySlot()
        {
            // カタログから消えたアイテムIdを持つ旧セーブは、当該スロットを未装備として復元する
            // （ロード自体は失敗させない。→ 03 §12「既存セーブを保護する」）。
            var data = new GameState { Adventurers = { new Adventurer() } }.ToSaveData();
            var json = JsonSerializer.Serialize(data)
                .Replace("\"EquippedWeapon\":null", "\"EquippedWeaponId\":\"RemovedLegendarySword\"");
            Assert.Contains("RemovedLegendarySword", json);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var loaded = Assert.Single(restored.Adventurers);
            Assert.Null(loaded.EquippedWeapon);
            Assert.Null(loaded.EquippedWeaponId);
        }

        [Fact]
        public void LegacySave_WithExplicitNullUnidentifiedItems_StillLoads()
        {
            // 手編集・別バージョンの書き出し等でキーが null になっていても、
            // FromSaveData側のnull合体（→ GameState.FromSaveData）で空在庫へ落ちること。
            var data = new GameState { Gold = 321 }.ToSaveData();
            var json = JsonSerializer.Serialize(data)
                .Replace("\"UnidentifiedItems\":[]", "\"UnidentifiedItems\":null")
                .Replace("\"Armory\":[]", "\"Armory\":null");

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            Assert.Equal(321, restored.Gold);
            Assert.Empty(restored.UnidentifiedItems);
            Assert.Empty(restored.Armory);
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
