using System;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 迷宮の異変の種類（→ Systems.DungeonAnomalySystem、03 §4.10・§0.64）。良い面と悪い面を組にして、
    /// 「今月はそこへ行くか」を考えさせる。倍率は commissions.csv の Anomaly*。
    /// </summary>
    public enum DungeonAnomalyType
    {
        /// <summary>瘴気：そのフィールドでの損耗（道中・討伐・採取のHP消費）が増えるが、採取の素材も増える。</summary>
        Miasma,

        /// <summary>遺物の鉱脈：そのフィールドの採取で遺物が出やすい。</summary>
        RelicVein,

        /// <summary>霧が晴れる：そのフィールドのボスの解析（迷宮調査・扉前の偵察）が進みやすい。</summary>
        ClearMist,

        /// <summary>主の衰え：そのフィールドの次のボスの要求火力が下がる。</summary>
        WeakenedLord,

        /// <summary>主の猛り：そのフィールドの次のボスの要求火力が上がるが、撃破報酬のゴールドも増える。</summary>
        EnragedLord,
    }

    /// <summary>
    /// 迷宮の異変1件（→ GameState.Anomaly、Systems.DungeonAnomalySystem、03 §4.10・§0.64）。
    /// 季節の途中で予告され（AnnouncedWeek）、翌週から一定期間（StartWeek〜EndWeek）だけ効く。
    /// セーブにはこのクラスをそのまま書き出す。
    /// </summary>
    public class DungeonAnomaly
    {
        public DungeonAnomalyType Type { get; set; }

        /// <summary>異変の起きるフィールドのId。</summary>
        public string FieldId { get; set; } = "";

        /// <summary>主の衰え・主の猛りの対象ボスのId（予告の時点の次のボス）。それ以外は null。</summary>
        public Guid? BossId { get; set; }

        /// <summary>予告した週。</summary>
        public int AnnouncedWeek { get; set; }

        /// <summary>効き始める週（この週の決算から効く）。</summary>
        public int StartWeek { get; set; }

        /// <summary>最後に効く週（この週の決算まで効く）。</summary>
        public int EndWeek { get; set; }

        /// <summary>指定の週に効いているか。</summary>
        public bool IsActive(int week) => week >= StartWeek && week <= EndWeek;

        /// <summary>まだ始まっていない（予告中）か。</summary>
        public bool IsUpcoming(int week) => week < StartWeek;
    }
}
