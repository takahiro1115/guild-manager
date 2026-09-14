using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// クエストデータモデル。仕様書 03 §4.0 参照。
    /// </summary>
    public class Quest
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public QuestRank Rank { get; set; }

        /// <summary>
        /// クエスト種別（討伐／調査・探索／護衛）。脅威度（§4.4）への影響は討伐のみが対象
        /// （→ 03 §4.0）。デフォルトはSubjugation（既存クエストとの後方互換のため）。
        /// </summary>
        public QuestType QuestType { get; set; } = QuestType.Subjugation;

        /// <summary>1〜100。値が高いほど難しい。→ BAL: クエスト</summary>
        public int Difficulty { get; set; }

        /// <summary>1〜100。DEXと同スケール。→ BAL: クエスト</summary>
        public int ScoutRequirement { get; set; }

        /// <summary>
        /// 探索規模。難易度とは独立した属性（仕様書 03 §4.0）。デフォルトはSmall
        /// （＝拘束1週。§4.0.1導入前の既存クエストと同じ挙動を保つ）。
        /// </summary>
        public QuestScale Scale { get; set; } = QuestScale.Small;

        /// <summary>
        /// 拘束週数。Scaleから導出する（仕様書 03 §4.0.1）。派遣してからこの週数が
        /// 満了するまで、パーティは「派遣中」状態になり索敵〜治安の解決は行われない。
        /// </summary>
        public int DurationWeeks => QuestBalance.GetDurationWeeks(Scale);

        public int RewardGold { get; set; }

        /// <summary>
        /// 受注可能な残り週数。QuestBoardSystem.ProcessWeeklyBoardが受注されないまま
        /// 経過した週ごとに1減らし、0以下になったら期限切れとして受注可能一覧から除去する
        /// （→ 03 §4.0・§4.4「放置」）。派遣（受注）されると受注可能一覧から除かれるため、
        /// 以降はこのカウントダウンの対象外になる。
        /// </summary>
        public int DeadlineWeeks { get; set; }

        /// <summary>
        /// このクエストに設定された環境ギミック（0件以上。「環境ギミック」刷新仕様参照）。
        /// 各タグごとにパーティの対策達成度（3段階）が判定され、未対策・一部対策の場合は
        /// フェーズ1（索敵）・フェーズ2（点数）・損耗（HP消費）のいずれかにペナルティが乗算される
        /// （→ Systems.GimmickEvaluator、Balance.GimmickBalance）。デフォルトは空（ギミックなし＝
        /// 既存クエストと同じ挙動）。
        /// </summary>
        public List<EnvironmentTag> EnvironmentTags { get; set; } = new();
    }
}
