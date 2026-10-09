using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>物語の背景と立ち絵（§0.91）：story_images.csv の読み込みと、小窓の1ページの舞台（StorySystem.StageOf）。</summary>
    public class StoryImageTests
    {
        private static readonly string[] Header = { "Kind", "Name", "Expression", "File", "note" };

        private static StoryShowing Showing(params string[] lines) =>
            new() { SceneId = "test", Pages = StoryBalance.Parse(new[] { "## test　試し" }.Concat(lines)).Single().Pages };

        [Fact]
        public void 台詞の背景はすべて画像の表にある()
        {
            var missing = StoryBalance.Scenes.Values.SelectMany(s => s.Pages).SelectMany(p => p)
                .Where(l => l.Kind == StoryLineKind.Background && StoryImageBalance.BackgroundFile(l.Text) == null)
                .Select(l => l.Text).Distinct().ToList();
            Assert.Empty(missing);
        }

        [Fact]
        public void 小窓に出る場面には背景がある()
        {
            var noBackground = StorySystem.SceneIds
                .Where(id => !StoryBalance.Get(id).Pages[0].Any(l => l.Kind == StoryLineKind.Background))
                .ToList();
            Assert.Empty(noBackground);
        }

        [Fact]
        public void 立ち絵は別名でも引けて_無い表情は通常になる()
        {
            Assert.Equal("portraits/lumina.png", StoryImageBalance.PortraitFile("ルミナ"));
            Assert.Equal("portraits/lumina.png", StoryImageBalance.PortraitFile("少女"));
            Assert.Equal("portraits/lumina.png", StoryImageBalance.PortraitFile("ルミナ", "まだ無い表情"));
            Assert.Null(StoryImageBalance.PortraitFile("報告"));
        }

        [Fact]
        public void 表情ごとの立ち絵を選ぶ()
        {
            var rows = new List<string[]>
            {
                new[] { "portrait", "ルミナ|少女", "", "p/lumina.png", "" },
                new[] { "portrait", "ルミナ", "笑顔", "p/lumina_smile.png", "" },
                new[] { "background", "研究室", "", "bg/lab.png", "" },
            };
            var defs = StoryImageBalance.Parse(Header, rows);
            Assert.Equal(StoryImageBalance.NormalExpression, defs[0].Expression);
            Assert.Equal(new[] { "ルミナ", "少女" }, defs[0].Names);
            Assert.Equal(StoryImageKind.Background, defs[2].Kind);
        }

        [Theory]
        [InlineData("people", "ルミナ", "", "p/a.png")]           // Kind の誤り
        [InlineData("portrait", "", "", "p/a.png")]              // Name が空
        [InlineData("portrait", "ルミナ", "", "")]               // File が空
        [InlineData("portrait", "ルミナ", "", "p/ルミナ.png")]   // File に全角
        [InlineData("portrait", "ルミナ", "", "../a.png")]       // File が上へ出る
        [InlineData("background", "研究室", "夜", "bg/a.png")]   // 背景に表情
        public void 書式の誤りは起動失敗にする(string kind, string name, string expression, string file)
        {
            var rows = new List<string[]> { new[] { kind, name, expression, file, "" } };
            Assert.Throws<BalanceDataException>(() => StoryImageBalance.Parse(Header, rows));
        }

        [Fact]
        public void 名前とファイルの重複は起動失敗にする()
        {
            Assert.Throws<BalanceDataException>(() => StoryImageBalance.Parse(Header, new List<string[]>
            {
                new[] { "portrait", "ルミナ|少女", "", "p/a.png", "" },
                new[] { "portrait", "少女", "通常", "p/b.png", "" },
            }));
            Assert.Throws<BalanceDataException>(() => StoryImageBalance.Parse(Header, new List<string[]>
            {
                new[] { "background", "研究室", "", "bg/a.png", "" },
                new[] { "background", "医務室", "", "bg/a.png", "" },
            }));
            Assert.Throws<BalanceDataException>(() => StoryImageBalance.Parse(new[] { "Kind", "Name", "File" }, new List<string[]>()));
        }

        [Fact]
        public void 背景はそのページまでの最後の指定を引き継ぐ()
        {
            var showing = Showing("〔背景：森の祠〕", "報告「見つけた」", "▼", "〔背景：医務室〕", "少女「ここは……？」", "▼", "アルベール「よかった」");
            Assert.Equal("森の祠", StorySystem.StageOf(showing, 0).Background);
            Assert.Equal("bg/forest_shrine.png", StorySystem.StageOf(showing, 0).BackgroundFile);
            Assert.Equal("医務室", StorySystem.StageOf(showing, 1).Background);
            Assert.Equal("医務室", StorySystem.StageOf(showing, 2).Background);

            var none = StorySystem.StageOf(Showing("アルベール「……」"), 0);
            Assert.Equal("", none.Background);
            Assert.Null(none.BackgroundFile);
        }

        [Fact]
        public void 立ち絵は話す人を左右に最大2人_場面の中で同じ側に置く()
        {
            var showing = Showing(
                "〔背景：研究室〕",
                "アルベール「最初に話す人は左」", "ルミナ「次に話す人は右」", "報告「立ち絵の無い人は出さない」",
                "▼",
                "ルミナ「このページはわたしだけ」",
                "▼",
                "〔地の文だけ〕",
                "▼",
                "イザベラ「3人目は左」", "ルミナ「ルミナは右のまま」", "アルベール「3人目からは出さない」");

            var p0 = StorySystem.StageOf(showing, 0);
            Assert.Equal("アルベール", p0.Left?.Speaker);
            Assert.Equal("portraits/albert.png", p0.Left?.File);
            Assert.Equal("ルミナ", p0.Right?.Speaker);

            var p1 = StorySystem.StageOf(showing, 1);
            Assert.Null(p1.Left);
            Assert.Equal("ルミナ", p1.Right?.Speaker);

            var p2 = StorySystem.StageOf(showing, 2);
            Assert.Null(p2.Left);
            Assert.Null(p2.Right);

            var p3 = StorySystem.StageOf(showing, 3);
            Assert.Equal("イザベラ", p3.Left?.Speaker);
            Assert.Equal("ルミナ", p3.Right?.Speaker);
        }

        [Fact]
        public void 同じ側の2人が同じページなら2人目を反対側へ寄せる()
        {
            var showing = Showing(
                "アルベール「左」", "ルミナ「右」",
                "▼",
                "イザベラ「本来は左」", "アルベール「本来も左」");
            var p1 = StorySystem.StageOf(showing, 1);
            Assert.Equal("イザベラ", p1.Left?.Speaker);
            Assert.Equal("アルベール", p1.Right?.Speaker);
        }

        [Fact]
        public void 表情つきの台詞は表情の立ち絵_無ければ通常()
        {
            var stage = StorySystem.StageOf(Showing("ルミナ（照れ）「えへへ」"), 0);
            Assert.Equal("portraits/lumina.png", stage.Left?.File);
        }
    }
}
