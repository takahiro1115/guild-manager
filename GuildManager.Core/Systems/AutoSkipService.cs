using System.Collections.Generic;
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

        public AutoSkipService(WeekProcessingSystem weekProcessingSystem)
        {
            _weekProcessingSystem = weekProcessingSystem;
        }

        /// <summary>
        /// 停止条件が成立するか、maxWeeksに達するまで週次決算処理を繰り返す。
        /// 進めた各週のWeekResult（停止判定フラグ一式）を、進めた順に返す
        /// （最後の要素が停止した週、または上限到達時の最終週）。
        /// </summary>
        public List<WeekResult> AutoSkip(GameState state, int maxWeeks = DefaultMaxWeeks)
        {
            var results = new List<WeekResult>();

            for (int i = 0; i < maxWeeks; i++)
            {
                var settlement = _weekProcessingSystem.ProcessWeek(state);
                results.Add(settlement.Flags);

                if (settlement.Flags.ShouldStopAutoSkip)
                    break;
            }

            return results;
        }
    }
}
