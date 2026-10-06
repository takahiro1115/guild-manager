namespace GuildManager.Core.Models
{
    /// <summary>
    /// 施設の専門（2026年10月・§0.76、→ 03 §6.2）。Lv3→4の工事を始めるときに施設ごとに2つから1つを選び、
    /// Lv4・5で上乗せされる効果は選んだ専門の分だけになる（Lv1〜3の効果は専門に関係なく同じ）。宿舎は専門を持たない。
    /// 選び直しは改装（→ FacilitySystem.TryStartRemodel）。
    /// </summary>
    public enum FacilitySpecialty
    {
        /// <summary>未選択（Lv3以下、または宿舎）。</summary>
        None,

        /// <summary>訓練施設：精鋭（1枠のまま成長の確率が上がる）。</summary>
        Elite,

        /// <summary>訓練施設：切磋琢磨（2枠になるが、成長の確率はLv3より低い）。</summary>
        Rivalry,

        /// <summary>医務室：療養院（HP回復の倍率・重傷の回復が上がる）。</summary>
        Sanatorium,

        /// <summary>医務室：戦地救護（ボス戦の致命の損耗の閾値が上がる）。</summary>
        FieldAid,

        /// <summary>冒険者支援室：目利き（副官の見立ての目利きが上がる、§0.74）。</summary>
        Appraisal,

        /// <summary>冒険者支援室：募集（新春採用試験の応募者が増える）。</summary>
        Recruiting,

        /// <summary>ギルド酒場：憩い（満足度の回復が上がる）。</summary>
        Leisure,

        /// <summary>ギルド酒場：商い（内職の売上が上がる）。</summary>
        Trade,

        /// <summary>作戦資料室：解析（迷宮調査の解析率が上がる）。</summary>
        Analysis,

        /// <summary>作戦資料室：踏破（道中の走破力が上がる）。</summary>
        Pathfinding,
    }
}
