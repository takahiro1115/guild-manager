using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 氏名ジェネレーター（和名・洋名、仕様書 03 §2.4）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class NameGeneratorTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        /// <summary>呼び出し順に決め打ちの値を1つずつ返すテスト用スタブ（[min,max]にクランプ）。</summary>
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            public SequenceRng(params int[] values) => _values = new Queue<int>(values);
            public int NextInt(int min, int max) =>
                System.Math.Clamp(_values.Count > 0 ? _values.Dequeue() : min, min, max);
        }

        private static readonly string[] WesternMaleNames =
        {
            "エドガー", "ロラン", "バルタザール", "ガレス", "レオン", "カディン", "ユリアン", "オスカー",
            "カリム", "リュカ", "トーマス", "アレク", "ヴァルター", "コンラッド", "ギルバート", "シドニー",
            "ダニエル", "ベネディクト", "マックス", "ルパート",
        };

        private static readonly string[] WesternFemaleNames =
        {
            "セリア", "クラウディア", "イネス", "ミレイ", "マリー", "エレオノーラ", "セレスティア", "アリス",
            "ヴェロニカ", "シェリル", "ソフィア", "ディアナ", "ヘレナ", "ベアトリス", "マグラダ", "ロザリア",
            "アイリーン", "カトリーナ", "シルヴィア", "シャルロット",
        };

        private static readonly string[] EasternMaleNames =
        {
            "レン", "イツキ", "ハヤテ", "ジン", "カゲツ", "ゲンシン", "タクマ", "タツミ", "ソウマ", "ヤマト",
            "カズマ", "シオン", "トウマ", "ナオツグ", "ライゾウ",
        };

        private static readonly string[] EasternFemaleNames =
        {
            "シノ", "カエデ", "葵", "凛", "桔梗", "サクラ", "トモエ", "千代", "鈴", "弥生", "梢",
        };

        // ---------------- 文化圏・性別ごとの名前プール（→ 03 §2.4） ----------------

        [Fact]
        public void GenerateFirstName_WesternMale_ReturnsNameFromWesternMalePool()
        {
            var name = NameGenerator.GenerateFirstName(Gender.Male, NameCulture.Western, new AlwaysMinRng());
            Assert.Contains(name, WesternMaleNames);
        }

        [Fact]
        public void GenerateFirstName_WesternFemale_ReturnsNameFromWesternFemalePool()
        {
            var name = NameGenerator.GenerateFirstName(Gender.Female, NameCulture.Western, new AlwaysMinRng());
            Assert.Contains(name, WesternFemaleNames);
        }

        [Fact]
        public void GenerateFirstName_EasternMale_ReturnsNameFromEasternMalePool()
        {
            var name = NameGenerator.GenerateFirstName(Gender.Male, NameCulture.Eastern, new AlwaysMinRng());
            Assert.Contains(name, EasternMaleNames);
        }

        [Fact]
        public void GenerateFirstName_EasternFemale_ReturnsNameFromEasternFemalePool()
        {
            var name = NameGenerator.GenerateFirstName(Gender.Female, NameCulture.Eastern, new AlwaysMinRng());
            Assert.Contains(name, EasternFemaleNames);
        }

        [Fact]
        public void GenerateFirstName_NeverCrossesPools_AcrossManyRolls()
        {
            // AlwaysMaxRngは各プールの末尾要素を指す。max境界でのインデックス範囲外アクセスが
            // 無いこと、かつプールを跨がないことを併せて確認する。
            var maxRng = new AlwaysMaxRng();
            Assert.Equal(WesternMaleNames[^1], NameGenerator.GenerateFirstName(Gender.Male, NameCulture.Western, maxRng));
            Assert.Equal(WesternFemaleNames[^1], NameGenerator.GenerateFirstName(Gender.Female, NameCulture.Western, maxRng));
            Assert.Equal(EasternMaleNames[^1], NameGenerator.GenerateFirstName(Gender.Male, NameCulture.Eastern, maxRng));
            Assert.Equal(EasternFemaleNames[^1], NameGenerator.GenerateFirstName(Gender.Female, NameCulture.Eastern, maxRng));
        }

        // ---------------- 重複回避（GenerateUniqueFirstName） ----------------

        [Fact]
        public void GenerateUniqueFirstName_RetriesUntilNonDuplicateNameFound()
        {
            // 1回目は既存名と重複するインデックス(0)、2回目で別のインデックス(1)を返すRNG。
            var rng = new SequenceRng(0, 1);
            var existing = new HashSet<string> { WesternMaleNames[0] };

            var name = NameGenerator.GenerateUniqueFirstName(Gender.Male, NameCulture.Western, existing, rng);

            Assert.Equal(WesternMaleNames[1], name);
        }

        [Fact]
        public void GenerateUniqueFirstName_FallsBackToNumberedName_AfterRetryLimitExhausted()
        {
            // 常に同じ名前（プール先頭）を引き続けるRNG。既存名にプール先頭が含まれていれば、
            // リトライ上限(NameGeneratorBalance.UniqueNameRetryLimit)を超えてフォールバックする。
            var rng = new AlwaysMinRng();
            var existing = new HashSet<string> { WesternMaleNames[0] };

            var name = NameGenerator.GenerateUniqueFirstName(Gender.Male, NameCulture.Western, existing, rng);

            Assert.Equal($"{WesternMaleNames[0]}2世", name);
        }

        [Fact]
        public void GenerateUniqueFirstName_ReturnsImmediately_WhenNoCollision()
        {
            var rng = new AlwaysMinRng();
            var existing = new HashSet<string>(); // 空＝重複なし

            var name = NameGenerator.GenerateUniqueFirstName(Gender.Male, NameCulture.Western, existing, rng);

            Assert.Equal(WesternMaleNames[0], name);
        }

        // ---------------- 性別・文化圏のランダム決定（RollGenderAndCulture） ----------------

        [Fact]
        public void RollGenderAndCulture_AlwaysMin_ReturnsFemaleAndWestern()
        {
            // 世界観設定（女性限定ギルド）→ MaleGenderChancePercent=0のため、
            // NextInt(1,100)=1 は男性率(0%)を必ず上回り常にFemaleになる。洋名率80%は
            // 従来通り境界含みで成功するため、文化圏はWestern。
            var (gender, culture) = NameGenerator.RollGenderAndCulture(new AlwaysMinRng());

            Assert.Equal(Gender.Female, gender);
            Assert.Equal(NameCulture.Western, culture);
        }

        [Fact]
        public void RollGenderAndCulture_AlwaysMax_ReturnsFemaleAndEastern()
        {
            // NextInt(1,100)=100 は男性率(0%)・洋名率80%のいずれも上回るため、常に(Female, Eastern)。
            var (gender, culture) = NameGenerator.RollGenderAndCulture(new AlwaysMaxRng());

            Assert.Equal(Gender.Female, gender);
            Assert.Equal(NameCulture.Eastern, culture);
        }

        [Fact]
        public void RollGenderAndCulture_MaleChanceIsZero_AlwaysReturnsFemale_AcrossFullRollRange()
        {
            // 世界観設定（女性限定ギルド）：MaleGenderChancePercent=0のため、
            // NextInt(1,100)が取りうる全ての値（1〜100）でGender.Maleは一切出現しない
            // （閾値0に対して「ロール<=0」を満たす値が存在しないため、確率分布ではなく
            // 構造的に100%女性になることを保証する）。
            Assert.Equal(0, NameGeneratorBalance.MaleGenderChancePercent);

            for (int roll = 1; roll <= 100; roll++)
            {
                var (gender, _) = NameGenerator.RollGenderAndCulture(new SequenceRng(roll, 1));
                Assert.Equal(Gender.Female, gender);
            }
        }

        [Fact]
        public void RollGenderAndCulture_UsesConfiguredCultureThreshold()
        {
            // 洋名率80%ちょうどの境界値が「成功（洋名）」側であることは変わらない
            // （性別は0%閾値のため、境界値であっても常にFemale）。
            var rng = new SequenceRng(1, NameGeneratorBalance.WesternCultureChancePercent);

            var (gender, culture) = NameGenerator.RollGenderAndCulture(rng);

            Assert.Equal(Gender.Female, gender);
            Assert.Equal(NameCulture.Western, culture);
        }
    }
}
