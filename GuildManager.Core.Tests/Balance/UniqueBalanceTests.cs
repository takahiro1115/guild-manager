using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests.Balance
{
    /// <summary>
    /// 固有武具の一覧（uniques.csv → UniqueBalance、03 §4.7.5・§0.45）の読み込みと検証のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~UniqueBalance`
    /// </summary>
    public class UniqueBalanceTests
    {
        private static readonly string[] Header =
        {
            "Id", "Grade", "Name", "BaseItemId", "HpBonus",
            "BonusStr", "BonusVit", "BonusAgi", "BonusDex", "BonusInt", "BonusMnd", "BonusLdr",
            "CounterGimmick", "DropFieldId", "DropFloor", "SellPrice",
        };

        private static string[] ArtifactRow(string id = "A1") =>
            new[] { id, "Artifact", "試しの剣", "IronSword", "0", "3", "0", "0", "0", "0", "0", "0", "", "", "0", "1000" };

        private static string[] LegendRow(string id = "L1", string field = "forest", string floor = "40") =>
            new[] { id, "Legendary", "試しの護符", "GuardCharm", "10", "0", "0", "0", "0", "0", "2", "0", "Poison", field, floor, "0" };

        private static IReadOnlyList<UniqueDefinition> Parse(params string[][] rows) => UniqueBalance.Parse(Header, rows);

        // ---------------- 実データ ----------------

        [Fact]
        public void UniquesCsv_LoadsFiveArtifactsAndFiveLegendaries()
        {
            Assert.Equal(5, UniqueBalance.All.Count(d => d.Grade == UniqueGrade.Artifact));
            Assert.Equal(5, UniqueBalance.All.Count(d => d.Grade == UniqueGrade.Legendary));
            Assert.Equal(5, UniqueBalance.Artifacts.Count);
        }

        [Fact]
        public void UniquesCsv_ParsesArtifactRow()
        {
            var def = UniqueBalance.FindById("ArtifactElvenBlade")!;

            Assert.Equal(UniqueGrade.Artifact, def.Grade);
            Assert.Equal("古代エルフの双刃", def.Name);
            Assert.Equal(ItemCatalog.IronSwordId, def.BaseItemId);
            Assert.Equal(5, def.GetStatBonus("STR"));
            Assert.Equal(2, def.GetStatBonus("AGI"));
            Assert.Equal(5, def.GetStatBonus("DEX"));
            Assert.Equal(0, def.GetStatBonus("INT"));
            Assert.Null(def.CounterGimmick);
            Assert.Null(def.DropFieldId);
            Assert.Equal(1500, def.SellPrice);
            Assert.Equal("STR+5・AGI+2・DEX+5", def.DescribeBonuses());
        }

        [Theory]
        [InlineData("LegendForestSigil", "forest", 40, BossGimmickType.Poison)]
        [InlineData("LegendCaveCleaver", "cave", 40, BossGimmickType.HeavyArmor)]
        [InlineData("LegendRuinsBow", "ruins", 40, BossGimmickType.Flying)]
        [InlineData("LegendCanyonCrown", "canyon", 40, BossGimmickType.InstantKill)]
        public void UniquesCsv_LegendariesCoverEachGimmick(string id, string field, int floor, BossGimmickType gimmick)
        {
            var def = UniqueBalance.FindById(id)!;

            Assert.Equal(UniqueGrade.Legendary, def.Grade);
            Assert.Equal(gimmick, def.CounterGimmick);
            Assert.Same(def, UniqueBalance.FindBossDrop(field, floor));
            Assert.Equal(0, def.SellPrice);
        }

        [Fact]
        public void EveryLegendary_PointsAtAnExistingBoss()
        {
            var fields = SampleData.CreateDefaultFields();
            foreach (var def in UniqueBalance.All.Where(d => d.Grade == UniqueGrade.Legendary))
            {
                var field = Assert.Single(fields, f => f.Id == def.DropFieldId);
                Assert.Contains(field.Bosses, b => b.Floor == def.DropFloor);
            }
        }

        [Fact]
        public void FindById_And_FindBossDrop_ReturnNull_WhenMissing()
        {
            Assert.Null(UniqueBalance.FindById("NoSuchUnique"));
            Assert.Null(UniqueBalance.FindById(null));
            Assert.Null(UniqueBalance.FindBossDrop("forest", 10));
            Assert.Null(UniqueBalance.FindBossDrop("nowhere", 40));
        }

        [Fact]
        public void ArtifactRates_OnlyEpicAndLegendary()
        {
            Assert.Equal(0, RelicBalance.Get(ItemRarity.Common).ArtifactRate);
            Assert.Equal(0, RelicBalance.Get(ItemRarity.Rare).ArtifactRate);
            Assert.Equal(4, RelicBalance.Get(ItemRarity.Epic).ArtifactRate);
            Assert.Equal(12, RelicBalance.Get(ItemRarity.Legendary).ArtifactRate);
        }

        // ---------------- 書式違反 ----------------

        [Fact]
        public void Parse_AcceptsValidRows()
        {
            var defs = Parse(ArtifactRow(), LegendRow());
            Assert.Equal(2, defs.Count);
            Assert.Equal(BossGimmickType.Poison, defs[1].CounterGimmick);
            Assert.Equal(10, defs[1].HpBonus);
        }

        public static IEnumerable<object[]> InvalidTables()
        {
            string[] With(string[] row, int index, string value) { var copy = (string[])row.Clone(); copy[index] = value; return copy; }

            yield return new object[] { new[] { ArtifactRow("X"), ArtifactRow("X") } };                  // Id重複
            yield return new object[] { new[] { With(ArtifactRow(), 0, "") } };                           // Id空
            yield return new object[] { new[] { With(ArtifactRow(), 1, "Mythic") } };                     // Grade不正
            yield return new object[] { new[] { With(ArtifactRow(), 2, "") } };                           // Name空
            yield return new object[] { new[] { With(ArtifactRow(), 3, "NoSuchItem") } };                 // BaseItemIdがカタログに無い
            yield return new object[] { new[] { With(ArtifactRow(), 5, "abc") } };                        // 数値パース失敗
            yield return new object[] { new[] { With(LegendRow(), 12, "Fire") } };                        // CounterGimmick不正
            yield return new object[] { new[] { With(LegendRow(), 13, "") } };                            // 伝説級に入手元が無い
            yield return new object[] { new[] { LegendRow(floor: "45") } };                               // ボス階層でない
            yield return new object[] { new[] { LegendRow(floor: "110") } };                              // 範囲外
            yield return new object[] { new[] { LegendRow("L1"), LegendRow("L2") } };                     // 同じボスに2本
            yield return new object[] { new[] { With(LegendRow(), 15, "500") } };                         // 伝説級は売却不可
            yield return new object[] { new[] { With(ArtifactRow(), 13, "forest") } };                    // アーティファクトに入手元
            yield return new object[] { new[] { With(ArtifactRow(), 15, "0") } };                         // アーティファクトの売値が0
            yield return new object[] { new[] { ArtifactRow().Take(10).ToArray() } };                     // 列数不一致
        }

        [Theory]
        [MemberData(nameof(InvalidTables))]
        public void Parse_Throws_OnInvalidRows(string[][] rows)
        {
            Assert.Throws<BalanceDataException>(() => Parse(rows));
        }

        [Fact]
        public void Parse_Throws_WhenRequiredColumnMissing()
        {
            var header = Header.Where(h => h != "SellPrice").ToArray();
            Assert.Throws<BalanceDataException>(() => UniqueBalance.Parse(header, Array.Empty<string[]>()));
        }
    }
}
