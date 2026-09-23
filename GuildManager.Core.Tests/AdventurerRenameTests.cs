using System;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 冒険者の任意改名（→ Adventurer.Rename、03 §2.1。2026年9月新設）のテスト。
    /// </summary>
    public class AdventurerRenameTests
    {
        [Theory]
        [InlineData("リ")]                        // 1文字
        [InlineData("アルベール・ヴァレンティ")]   // 12文字ちょうど
        [InlineData("クラウディア")]              // 日本語
        [InlineData("Claudia2")]                  // 英数
        [InlineData("紅の剣士ミラ")]              // 漢字・かな混在
        public void Rename_ValidName_UpdatesName(string newName)
        {
            var a = new Adventurer { Name = "旧名" };
            var id = a.Id;

            a.Rename(newName);

            Assert.Equal(newName, a.Name);
            Assert.Equal(id, a.Id); // 内部識別子は変わらない
        }

        [Fact]
        public void Rename_TrimsLeadingAndTrailingWhitespace()
        {
            var a = new Adventurer();

            a.Rename("  　フィオナ \t");

            Assert.Equal("フィオナ", a.Name);
        }

        [Fact]
        public void Rename_TwelveCharsAfterTrim_IsAccepted()
        {
            // 前後の空白は文字数に数えない（トリム後に判定）。
            var a = new Adventurer();
            a.Rename("   ABCDEFGHIJKL   ");
            Assert.Equal("ABCDEFGHIJKL", a.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("　　")]   // 全角スペースのみ
        [InlineData("\t\n")]
        public void Rename_NullOrBlank_Throws_AndKeepsOldName(string? newName)
        {
            var a = new Adventurer { Name = "旧名" };

            Assert.Throws<ArgumentException>(() => a.Rename(newName));
            Assert.Equal("旧名", a.Name);
        }

        [Theory]
        [InlineData("ABCDEFGHIJKLM")]            // 13文字（半角）
        [InlineData("あいうえおかきくけこさしす")] // 13文字（全角）
        public void Rename_ThirteenOrMoreChars_Throws_AndKeepsOldName(string newName)
        {
            var a = new Adventurer { Name = "旧名" };

            Assert.Equal(13, newName.Length);
            Assert.Throws<ArgumentException>(() => a.Rename(newName));
            Assert.Equal("旧名", a.Name);
        }
    }
}
