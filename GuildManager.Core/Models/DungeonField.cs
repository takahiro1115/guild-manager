using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮を構成する1フィールド（→ 大迷宮5フィールド拡張仕様）。
    ///
    /// 単一のダンジョンだった旧モデル（→ FloorBoss・SampleData.CreateFloorBosses、
    /// v2.0時点で撤去済み）を、5フィールド×各100階層のモデルへ拡張したもの。
    /// 各フィールドは BossInterval 階ごとに階層ボスを配置する（→ Bosses）。
    ///
    /// 開放条件・最高到達階層の更新は本クラス自身ではなく
    /// Systems.DungeonExpeditionSystem.ApplyFieldProgression が担う
    /// （複数フィールドをまたいだ判定が必要なため、単一フィールドの知識だけでは完結しないロジック）。
    /// </summary>
    public class DungeonField
    {
        /// <summary>全フィールド共通の最大階層。</summary>
        public const int MaxFloor = 100;

        /// <summary>
        /// 階層ボスを配置する間隔（→ BAL: dungeon.csv BossIntervalFloors、10階ごと）。
        /// CSV外部化に伴い const ではなく static readonly（→ DungeonBalance.BossIntervalFloors）。
        /// </summary>
        public static readonly int BossInterval = DungeonBalance.BossIntervalFloors;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>攻略順（1〜5）。若い順に開放される（→ DungeonExpeditionSystem.ApplyFieldProgression）。</summary>
        public int Order { get; set; }

        /// <summary>開放済みか。初期状態はOrder=1（森）のみtrue（→ SampleData.CreateDefaultFields）。</summary>
        public bool IsUnlocked { get; set; } = false;

        /// <summary>
        /// 最高到達階層の記録（1〜MaxFloor）。ボス撃破のたびに
        /// max(現在値, 撃破階層+1) で更新される（→ DungeonExpeditionSystem.ApplyFieldProgression）ほか、
        /// 道中進軍でより深く潜った場合も更新される。
        ///
        /// 「毎回1Fリセット」改訂（2026年9月）以降、これは表示・素材の抽選範囲用の記録であって
        /// 出撃の開始階層ではない（部隊は毎回1階層から潜る。→ ActiveDungeonMission.CurrentFloor）。
        /// </summary>
        public int ReachedFloor { get; set; } = 1;

        /// <summary>このフィールドの階層ボス一覧（5, 10, 15, ..., 100階の計20体）。</summary>
        public List<FloorBoss> Bosses { get; set; } = new();

        /// <summary>
        /// このフィールドの現在の攻略対象（未撃破のうち最も浅い階層のボス）。
        /// 全ボス撃破済み（フィールド制覇）ならnull。
        /// </summary>
        public FloorBoss? GetNextActiveBoss() =>
            Bosses.Where(b => !b.IsDefeated).OrderBy(b => b.Floor).FirstOrDefault();

        /// <summary>
        /// 指定階層以降で最初に立ちはだかる未撃破ボス（その階層ちょうどのボスも含む）。
        /// 潜行中の部隊の足止め先（→ ActiveDungeonMission.CurrentFloor）。無ければnull。
        /// </summary>
        public FloorBoss? GetNextUndefeatedBossFrom(int floor) =>
            Bosses.Where(b => !b.IsDefeated && b.Floor >= floor).OrderBy(b => b.Floor).FirstOrDefault();

        /// <summary>
        /// 指定階層から次の階層へ進む区間を担当するボス（＝その階層より深い最初のボス。撃破済みも含む）。
        /// 調査度連動の走破加速（→ DungeonTraversalResolver）はこのボスの解析率を参照する。無ければnull。
        /// </summary>
        public FloorBoss? GetSegmentBoss(int floor) =>
            Bosses.Where(b => b.Floor > floor).OrderBy(b => b.Floor).FirstOrDefault();
    }
}
