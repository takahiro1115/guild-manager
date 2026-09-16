using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 氏名ジェネレーター（和名・洋名、仕様書 03 §2.4）のテスト。
    ///
    /// 女性限定ギルドの構造化（→ Models.Gender・Systems.NameGenerator）により、
    /// 男性名プールと性別の抽選は撤去済み。抽選対象は文化圏（洋名／和名）のみになった。
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

        private static readonly string[] WesternFemaleNames =
        {
            "セリア", "クラウディア", "イネス", "ミレイ", "マリー", "エレオノーラ", "セレスティア", "アリス",
            "ヴェロニカ", "シェリル", "ソフィア", "ディアナ", "ヘレナ", "ベアトリス", "マグラダ", "ロザリア",
            "アイリーン", "カトリーナ", "シルヴィア", "シャルロット",
        };

        private static readonly string[] EasternFemaleNames =
        {
            "シノ", "カエデ", "葵", "凛", "桔梗", "サクラ", "トモエ", "千代", "鈴", "弥生", "梢",
        };

        // ---------------- 文化圏ごとの名前プール（→ 03 §2.4） ----------------

        [Fact]
        public void GenerateFirstName_Western_ReturnsNameFromWesternFemalePool()
        {
            var name = NameGenerator.GenerateFirstName(NameCulture.Western, new AlwaysMinRng());
            Assert.Contains(name, WesternFemaleNames);
        }

        [Fact]
        public void GenerateFirstName_Eastern_ReturnsNameFromEasternFemalePool()
        {
            var name = NameGenerator.GenerateFirstName(NameCulture.Eastern, new AlwaysMinRng());
            Assert.Contains(name, EasternFemaleNames);
        }

        [Fact]
        public void GenerateFirstName_NeverCrossesPools_AtRangeBoundary()
        {
            // AlwaysMaxRngは各プールの末尾要素を指す。max境界でのインデックス範囲外アクセスが
            // 無いこと、かつプールを跨がないことを併せて確認する。
            var maxRng = new AlwaysMaxRng();
            Assert.Equal(WesternFemaleNames[^1], NameGenerator.GenerateFirstName(NameCulture.Western, maxRng));
            Assert.Equal(EasternFemaleNames[^1], NameGenerator.GenerateFirstName(NameCulture.Eastern, maxRng));
        }

        [Fact]
        public void GenerateFirstName_NeverReturnsAMaleName_BecauseThePoolsWereRemoved()
        {
            // 女性限定ギルドの構造化：男性名プールはコードから削除済みであり、
            // どの文化圏・どの乱数でも男性名が出ることはない（→ Systems.NameGenerator）。
            var formerMaleNames = new[] { "エドガー", "ガレス", "レオン", "レン", "イツキ", "ハヤテ" };

            for (int roll = 0; roll < 30; roll++)
            {
                var rng = new SequenceRng(roll, roll);
                Assert.DoesNotContain(NameGenerator.GenerateFirstName(NameCulture.Western, rng), formerMaleNames);
                Assert.DoesNotContain(NameGenerator.GenerateFirstName(NameCulture.Eastern, rng), formerMaleNames);
            }
        }

        // ---------------- 重複回避（GenerateUniqueFirstName） ----------------

        [Fact]
        public void GenerateUniqueFirstName_RetriesUntilNonDuplicateNameFound()
        {
            // 1回目は既存名と重複するインデックス(0)、2回目で別のインデックス(1)を返すRNG。
            var rng = new SequenceRng(0, 1);
            var existing = new HashSet<string> { WesternFemaleNames[0] };

            var name = NameGenerator.GenerateUniqueFirstName(NameCulture.Western, existing, rng);

            Assert.Equal(WesternFemaleNames[1], name);
        }

        [Fact]
        public void GenerateUniqueFirstName_FallsBackToNumberedName_AfterRetryLimitExhausted()
        {
            // 常に同じ名前（プール先頭）を引き続けるRNG。既存名にプール先頭が含まれていれば、
            // リトライ上限(NameGeneratorBalance.UniqueNameRetryLimit)を超えてフォールバックする。
            var rng = new AlwaysMinRng();
            var existing = new HashSet<string> { WesternFemaleNames[0] };

            var name = NameGenerator.GenerateUniqueFirstName(NameCulture.Western, existing, rng);

            Assert.Equal($"{WesternFemaleNames[0]}2世", name);
        }

        [Fact]
        public void GenerateUniqueFirstName_ReturnsImmediately_WhenNoCollision()
        {
            var rng = new AlwaysMinRng();
            var existing = new HashSet<string>(); // 空＝重複なし

            var name = NameGenerator.GenerateUniqueFirstName(NameCulture.Western, existing, rng);

            Assert.Equal(WesternFemaleNames[0], name);
        }

        // ---------------- 文化圏のランダム決定（RollCulture） ----------------

        [Fact]
        public void RollCulture_AlwaysMin_ReturnsWestern()
        {
            // NextInt(1,100)=1 <= 80(洋名率) のため常に Western。
            Assert.Equal(NameCulture.Western, NameGenerator.RollCulture(new AlwaysMinRng()));
        }

        [Fact]
        public void RollCulture_AlwaysMax_ReturnsEastern()
        {
            // NextInt(1,100)=100 は洋名率80%を上回るため常に Eastern。
            Assert.Equal(NameCulture.Eastern, NameGenerator.RollCulture(new AlwaysMaxRng()));
        }

        [Fact]
        public void RollCulture_UsesConfiguredThreshold_BoundaryIsWestern()
        {
            // 洋名率80%ちょうどの境界値が「成功（洋名）」側であること。
            var rng = new SequenceRng(NameGeneratorBalance.WesternCultureChancePercent);

            Assert.Equal(NameCulture.Western, NameGenerator.RollCulture(rng));
        }

        // ---------------- 性別（女性限定ギルドの構造化） ----------------

        [Fact]
        public void Gender_HasNoValueOtherThanFemale()
        {
            // 設定値の書き換えで男性が復活する余地を構造から取り除いたことの検証
            //（→ Models.Gender）。列挙型に Female 以外の値は存在しない。
            var values = System.Enum.GetValues<Gender>();

            Assert.Single(values);
            Assert.Equal(Gender.Female, values.Single());
        }

        [Fact]
        public void Gender_DefaultValueIsFemale()
        {
            // 明示的に設定しなくても女性になる（既定値が Female）。
            Assert.Equal(Gender.Female, new Adventurer().Gender);
        }
    }
}
