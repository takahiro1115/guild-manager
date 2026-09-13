using System;
using System.Collections.Generic;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 1回の遠征解決（QuestResolver.Resolve）の結果まとめ。
    /// UI側（Godot）はこれを読んで週報ログに表示する想定。
    /// </summary>
    public class WeekResolutionResult
    {
        public EncounterResult Encounter { get; set; }

        /// <summary>
        /// 討伐（Subjugation）の勝敗区分（4区分。→ 03 §4.2）。討伐以外（探索・護衛）では
        /// nullになり、代わりに <see cref="NonCombatOutcome"/> が設定される（→ 03 §4.2.3、項目63）。
        /// 両者は排他：どちらか一方だけが必ず値を持つ。
        /// </summary>
        public CombatOutcome? Outcome { get; set; }

        /// <summary>
        /// 探索・護衛の判定区分（3区分。→ 03 §4.2.3、項目63で新設）。討伐ではnull。
        /// 討伐の <see cref="Outcome"/> とは別概念として扱う（致死判定・古傷・戦死が
        /// 発生しない／HP消費が軽量、という性質の違いを型で区別するため）。
        /// </summary>
        public NonCombatOutcome? NonCombatOutcome { get; set; }

        public double Ratio { get; set; }

        /// <summary>
        /// 今回の解決で発生したランダムイベント（→ 03 §4.2.3、項目65）の結果。
        /// 探索・護衛のみが対象で、討伐では必ず全て未発生（各プロパティがnull）になる。
        /// イベント由来の追加報酬は RewardGold にも合算済み（内訳は Events 側で参照できる）。
        /// </summary>
        public QuestEventResults Events { get; } = new();
        public bool QuestAchieved { get; set; }
        public int RewardGold { get; set; }

        /// <summary>冒険者IDごとの、今回の遠征で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();

        /// <summary>
        /// 今回の遠征でダウンした（現在HPが0になった）冒険者ID。
        /// フェーズ3の致死判定対象の絞り込みに使う（→ 03 §4.3）。生存・古傷・戦死のいずれの
        /// 結果になった者も含む（戦死者はさらに FallenAdventurerIds にも含まれる）。
        /// </summary>
        public HashSet<Guid> DownedAdventurerIds { get; set; } = new();

        /// <summary>
        /// 今回の遠征で戦死した冒険者ID（→ 03 §4.3・§4.3.1）。
        /// QuestDispatchSystem側でこれを見て、GameState.Adventurers から
        /// GameState.FallenAdventurers への移動と、仲間ロストの満足度ペナルティ（§5.1）を行う。
        /// </summary>
        public HashSet<Guid> FallenAdventurerIds { get; set; } = new();
    }
}
