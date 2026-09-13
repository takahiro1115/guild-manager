using System;
using System.IO;
using GuildManager.Core.Balance;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// バランス値CSVローダー（BalanceData、→ 03 §10.1・項目58）のテスト。
    ///
    /// 「全CSVファイルが正常に読み込めること」は、GetTableが素直な行分割パーサーであり、
    /// key,value,unit,note形式・テーブル形式のどちらも「ヘッダー行＋列数の揃ったデータ行」
    /// という構造は共通なので、全18ファイル（項目58で14ファイル・そのフォローアップで
    /// trait.csv・equipment.csvの2ファイル・項目63でquest_type_weights.csv・
    /// quest_scoring.csvの2ファイルを追加）に対してGetTableで汎用的に検証できる
    /// （実際の意味付け・型変換は各Balanceクラス側のテスト・既存の回帰テストで
    /// 別途カバーされている）。
    ///
    /// 「キー欠損」「パース失敗」の異常系は、本番のCSVを汚さないよう、テスト専用の
    /// 一時CSVファイルをテスト出力ディレクトリの04_バランス表フォルダ（BalanceDataが
    /// 解決する場所と同じ）に書き出してから検証する。
    /// </summary>
    public class BalanceDataTests
    {
        private static readonly string BalanceDir = Path.Combine(AppContext.BaseDirectory, "04_バランス表");

        [Theory]
        [InlineData("aging.csv")]
        [InlineData("combat.csv")]
        [InlineData("compatibility_advisor.csv")]
        [InlineData("economy.csv")]
        [InlineData("equipment.csv")]
        [InlineData("facility.csv")]
        [InlineData("growth_job_weights.csv")]
        [InlineData("guild_rank.csv")]
        [InlineData("guild_rank_params.csv")]
        [InlineData("quest.csv")]
        [InlineData("quest_scoring.csv")]
        [InlineData("quest_templates.csv")]
        [InlineData("quest_type_weights.csv")]
        [InlineData("recruitment.csv")]
        [InlineData("satisfaction.csv")]
        [InlineData("security.csv")]
        [InlineData("trait.csv")]
        [InlineData("training.csv")]
        public void GetTable_LoadsEveryBalanceCsvFile_WithoutError(string fileName)
        {
            var (header, rows) = BalanceData.GetTable(fileName);

            Assert.NotEmpty(header);
            Assert.NotEmpty(rows);
        }

        [Fact]
        public void GetInt_MissingFile_Throws()
        {
            var ex = Assert.Throws<BalanceDataException>(() => BalanceData.GetInt("__does_not_exist__.csv", "AnyKey"));
            Assert.Contains("__does_not_exist__.csv", ex.Message);
        }

        [Fact]
        public void GetInt_MissingKey_Throws()
        {
            const string fileName = "balancedatatests_missing_key.csv";
            WriteTestCsv(fileName, "key,value,unit,note\nSomeOtherKey,1,pt,テスト用\n");

            var ex = Assert.Throws<BalanceDataException>(() => BalanceData.GetInt(fileName, "NoSuchKey"));
            Assert.Contains("NoSuchKey", ex.Message);
            Assert.Contains(fileName, ex.Message);
        }

        [Fact]
        public void GetInt_UnparseableValue_Throws()
        {
            const string fileName = "balancedatatests_bad_int.csv";
            WriteTestCsv(fileName, "key,value,unit,note\nBadKey,not-a-number,pt,テスト用\n");

            var ex = Assert.Throws<BalanceDataException>(() => BalanceData.GetInt(fileName, "BadKey"));
            Assert.Contains("BadKey", ex.Message);
            Assert.Contains("not-a-number", ex.Message);
        }

        [Fact]
        public void GetDouble_UnparseableValue_Throws()
        {
            const string fileName = "balancedatatests_bad_double.csv";
            WriteTestCsv(fileName, "key,value,unit,note\nBadKey,abc,倍,テスト用\n");

            var ex = Assert.Throws<BalanceDataException>(() => BalanceData.GetDouble(fileName, "BadKey"));
            Assert.Contains("BadKey", ex.Message);
        }

        [Fact]
        public void GetDouble_InvariantCulture_ParsesDotAsDecimalSeparator()
        {
            const string fileName = "balancedatatests_invariant_culture.csv";
            WriteTestCsv(fileName, "key,value,unit,note\nRatio,0.35,倍,テスト用\n");

            double value = BalanceData.GetDouble(fileName, "Ratio");

            Assert.Equal(0.35, value, precision: 10);
        }

        [Fact]
        public void GetTable_ColumnCountMismatch_Throws()
        {
            const string fileName = "balancedatatests_bad_table.csv";
            WriteTestCsv(fileName, "a,b,c\n1,2\n"); // ヘッダー3列に対しデータ行が2列しかない

            var ex = Assert.Throws<BalanceDataException>(() => BalanceData.GetTable(fileName));
            Assert.Contains(fileName, ex.Message);
        }

        private static void WriteTestCsv(string fileName, string content)
        {
            Directory.CreateDirectory(BalanceDir);
            File.WriteAllText(Path.Combine(BalanceDir, fileName), content);
        }
    }
}
