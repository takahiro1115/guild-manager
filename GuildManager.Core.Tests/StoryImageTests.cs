using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>物語の背景と立ち絵（§0.91）：story_images.csv の読み込みと、小窓のコマ（StorySystem.StepsOf、§0.107）。</summary>
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
            Assert.Equal("portraits/albert_angry.png", StoryImageBalance.PortraitFile("アルベール", "怒り"));
            Assert.Equal("portraits/isabella_blush.png", StoryImageBalance.PortraitFile("イザベラ", "照れ"));
            Assert.Equal("portraits/isabella.png", StoryImageBalance.PortraitFile("イザベラ", "怒り"));
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
        public void 背景はその行までの最後の指定を引き継ぐ()
        {
            var steps = StorySystem.StepsOf(Showing("〔背景：森の祠〕", "報告「見つけた」", "▼", "少女「あ……」", "〔背景：医務室〕", "少女「ここは……？」", "▼", "アルベール「よかった」"));
            Assert.Equal(new[] { "森の祠", "森の祠", "医務室", "医務室" }, steps.Select(s => s.Background));
            Assert.Equal("bg/forest_shrine.jpg", steps[0].BackgroundFile);

            var none = StorySystem.StepsOf(Showing("アルベール「……」")).Single();
            Assert.Equal("", none.Background);
            Assert.Null(none.BackgroundFile);
        }

        [Fact]
        public void コマは台詞と地の文を1行ずつ_ページをまたいで並べる()
        {
            var steps = StorySystem.StepsOf(Showing(
                "〔背景：研究室〕",
                "アルベール「一行目」", "〔地の文〕",
                "▼",
                "ルミナ「次のページ」",
                "【画面へ】大迷宮",
                "【手引き】□ やること"));
            Assert.Equal(new[] { "一行目", "地の文", "次のページ" }, steps.Select(s => s.Line.Text));
            Assert.Equal(StoryLineKind.Narration, steps[1].Line.Kind);
        }

        [Fact]
        public void 立ち絵は話す人の1枚だけ_別名でも同じ絵()
        {
            // 「？？？」はアルベールの別名：名乗る前と後で同じ立ち絵が1枚だけ出る（2人並ばない）
            var steps = StorySystem.StepsOf(Showing("？？？「私はアルベルティナ」", "〔地の文〕", "アルベール「アルベールでいいわ」", "報告「立ち絵の無い人」"));
            Assert.Equal("portraits/albert.png", steps[0].PortraitFile);
            Assert.Null(steps[1].PortraitFile);
            Assert.Equal("portraits/albert.png", steps[2].PortraitFile);
            Assert.Null(steps[3].PortraitFile);
        }

        [Fact]
        public void 表情つきの台詞は表情の立ち絵_無ければ通常()
        {
            Assert.Equal("portraits/lumina.png", StorySystem.StepsOf(Showing("ルミナ（照れ）「えへへ」")).Single().PortraitFile);
            Assert.Equal("portraits/albert_smile.png", StorySystem.StepsOf(Showing("アルベール（笑顔）「ふふ」")).Single().PortraitFile);
        }
    }
}
