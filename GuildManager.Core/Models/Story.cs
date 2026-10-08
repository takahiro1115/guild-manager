using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>台詞の行の種類（→ docs/04_バランス表/story/*.txt の書式、§0.86）。</summary>
    public enum StoryLineKind
    {
        /// <summary>名前「…」（名前（表情）「…」）。</summary>
        Speech,
        /// <summary>〔…〕：地の文。</summary>
        Narration,
        /// <summary>〔背景：名前〕：背景の切り替え。</summary>
        Background,
        /// <summary>【画面へ】…：小窓に付ける画面へのボタン。</summary>
        Jump,
        /// <summary>【手引き】□ …：ギルドの手引きのやること（③-2でチェックリストにする）。</summary>
        Guide,
        /// <summary>【手引き（説明）】…：ギルドの手引きの説明。</summary>
        GuideNote,
    }

    /// <summary>台詞の1行。</summary>
    public sealed record StoryLine(StoryLineKind Kind, string Text, string Speaker = "", string Expression = "");

    /// <summary>場面1つ（→ Systems.StorySystem）。Pages は ▼ で区切ったページ。</summary>
    public sealed class StoryScene
    {
        public string Id { get; init; } = "";
        /// <summary>見出し（書き手向け。画面には出さない）。</summary>
        public string Title { get; init; } = "";
        public List<List<StoryLine>> Pages { get; init; } = new();
        /// <summary>@variants：台詞の行のうち1つだけを順番に出す（毎回の一言）。</summary>
        public bool Variants { get; init; }
    }

    /// <summary>場面を出す時機（→ StorySystem.DueScenes）。</summary>
    public enum StoryTiming
    {
        /// <summary>操作のあと（部隊を組んだ・方針を付けたなど）。画面を再描画したときに見る。</summary>
        Interactive,
        /// <summary>月を進めたあと、月報の前（ボスの撃破・来訪・交流戦の結果など）。</summary>
        BeforeReport,
        /// <summary>月報を閉じたあと（月のはじめの場面）。</summary>
        AfterReport,
        /// <summary>クリアしたとき、エンディングの窓の前（都の心臓・祝宴・初出撃、§0.89）。</summary>
        Ending,
    }
}
