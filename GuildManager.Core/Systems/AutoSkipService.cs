using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 自動スキップ（早送り）機能。仕様書 03 §1.3 参照（v1.10改訂で新設）。
    ///
    /// WeekProcessingSystem.ProcessWeekを繰り返し呼び出し、いずれかの停止条件
    /// （→ WeekResult.ShouldStopAutoSkip）が成立した週、または上限週数に達した時点で
    /// ループを終了する。自動スキップ中はパーティーの派遣操作を一切行わない
    /// （WeekProcessingSystem.ProcessWeek自体が派遣操作を含まない設計のため、
    /// これは自然に満たされる。→ WeekProcessingSystemのクラスdocコメント参照）。
    ///
    /// 事前調査メモ：指示書のサンプルシグネチャ`List&lt;WeekResult&gt; AutoSkip(int maxWeeks = 1000)`
    /// にはGameStateを受け取る引数が無かったが、対象の状態を渡さなければ処理しようがない
    /// （このプロジェクトの全Systemクラスと同様、GameStateは呼び出し側が明示的に渡す
    /// 設計で統一されている）ため、第一引数としてGameState stateを追加した。
    /// </summary>
    public class AutoSkipService
    {
        /// <summary>無限ループ防止のためのデフォルト上限週数（→ 03 §1.3）。</summary>
        public const int DefaultMaxWeeks = 1000;

        private readonly WeekProcessingSystem _weekProcessingSystem;
        private readonly SquadOrderSystem? _squadOrderSystem;

        /// <param name="squadOrderSystem">
        /// 部隊の方針（→ SquadOrderSystem、§0.63）。渡すと、毎週の決算の前に方針の部隊を自動で出撃・扉前判断させる。
        /// 省略すると従来どおり派遣操作を一切行わない。
        /// </param>
        public AutoSkipService(WeekProcessingSystem weekProcessingSystem, SquadOrderSystem? squadOrderSystem = null)
        {
            _weekProcessingSystem = weekProcessingSystem;
            _squadOrderSystem = squadOrderSystem;
        }

        /// <summary>
        /// 停止条件が成立するか、maxWeeksに達するまで週次決算処理を繰り返す。
        /// 進めた各週のWeekResult（停止判定フラグ一式）を、進めた順に返す
        /// （最後の要素が停止した週、または上限到達時の最終週）。
        /// </summary>
        public List<WeekResult> AutoSkip(GameState state, int maxWeeks = DefaultMaxWeeks) =>
            AutoSkipDetailed(state, maxWeeks).Select(w => w.Settlement.Flags).ToList();

        /// <summary>
        /// AutoSkip と同じだが、各週の方針による行動（決算の前）と決算の詳細をそのまま返す（週報を省略せず出すため、§0.63）。
        /// </summary>
        public List<AutoSkipWeek> AutoSkipDetailed(GameState state, int maxWeeks = DefaultMaxWeeks)
        {
            var weeks = new List<AutoSkipWeek>();

            for (int i = 0; i < maxWeeks; i++)
            {
                var orders = _squadOrderSystem?.Execute(state) ?? new List<SquadOrderEvent>();
                var settlement = _weekProcessingSystem.ProcessWeek(state);
                weeks.Add(new AutoSkipWeek(orders, settlement));

                if (settlement.Flags.ShouldStopAutoSkip)
                    break;
            }

            return weeks;
        }

        /// <summary>
        /// 自動スキップできるか：出撃中の部隊がすべて方針の自動出撃（→ ActiveDungeonMission.SavedPartyId）であること。
        /// 手動で出した部隊が残っていると、その決着を見落とさないよう先に「次週へ」で決着させてもらう。
        /// </summary>
        public static bool CanAutoSkip(GameState state) =>
            state.ActiveDungeonMissions.All(m => SquadOrderSystem.FindOrderedParty(state, m) != null);
    }

    /// <summary>自動スキップの1週分：決算の前の方針による行動と、決算の結果。</summary>
    public sealed record AutoSkipWeek(List<SquadOrderEvent> Orders, WeeklySettlementResult Settlement);
}
