using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ゲーム全体の状態。将来的にJSONへシリアライズしてセーブする前提（→ 05 技術メモ §4）。
    /// そのため参照の持ち方はシンプルに保つ（循環参照を避ける）。
    /// </summary>
    public class GameState
    {
        public int WeekNumber { get; set; } = 1;

        /// <summary>初期資金。→ BAL: 経済/初期資金</summary>
        public int Gold { get; set; } = 3000;

        public List<Adventurer> Adventurers { get; set; } = new();

        /// <summary>
        /// 40歳強制引退した冒険者の一覧（仕様書 03 §3.7つづき）。現役ロースター
        /// （Adventurers）からは除外しつつ、データとしては破棄しない。
        /// §7顧問制度が未実装の間は「引退済み・顧問候補」として保持するのみで、
        /// 教官・スカウト・参謀としての実際の効果は付与しない。§7実装時にここから再任用する想定。
        /// </summary>
        public List<Adventurer> RetiredAdventurers { get; set; } = new();

        /// <summary>
        /// 戦死した冒険者の一覧（仕様書 03 §4.3.1）。現役ロースター（Adventurers）・
        /// パーティ編成・訓練場配置からは完全に除外する。氏名・戦死週（FellAtWeek）・
        /// 戦死時の年齢（Age）・職業（JobClass）は Adventurer 自身が保持したまま移される
        /// （RetiredAdventurersと同じパターン）。
        /// </summary>
        public List<Adventurer> FallenAdventurers { get; set; } = new();

        public List<Quest> AvailableQuests { get; set; } = new();

        /// <summary>
        /// 進行中（派遣中）の複数週クエスト一覧（仕様書 03 §4.0.1）。満了週になるまで
        /// QuestDispatchSystem.ProcessWeeklyDispatches がここから取り除かない限り残り続ける。
        /// </summary>
        public List<ActiveDispatch> ActiveDispatches { get; set; } = new();

        /// <summary>訓練場に配置されている冒険者ID（→ 03 §3.1〜3.4「成長トリガー・経路2」）。</summary>
        public HashSet<Guid> TrainingAssignments { get; set; } = new();

        /// <summary>
        /// 4大施設の現在状態（仕様書 03 §6）。デフォルトで全種Lv1を1つずつ持つ。
        /// </summary>
        public List<Facility> Facilities { get; set; } = CreateDefaultFacilities();

        /// <summary>
        /// 現在の建設キュー（仕様書 03 §6.1）。null＝工事中の施設なし。
        /// 同時に2件以上を持てない設計を、単一のnull許容フィールドで表現する。
        /// </summary>
        public FacilityConstruction? UnderConstruction { get; set; }

        /// <summary>ギルドの名声（仕様書 03 §8.1）。0未満にはならない。</summary>
        public int Reputation { get; set; } = 0;

        /// <summary>ギルドの現在の格付け。初期値はG（最下位）。</summary>
        public GuildRank GuildRank { get; set; } = GuildRank.G;

        /// <summary>
        /// 現ランク相当（同ランク帯以上）のクエストを最後に達成してから経過した週数（→ 03 §8.1.1）。
        /// 該当クエストを達成した週に0へリセットされ、それ以外の週は+1される
        /// （Adventurer.WeeksSinceLastDeploymentと同じパターン）。
        /// </summary>
        public int WeeksSinceLastRankAppropriateQuest { get; set; } = 0;

        /// <summary>指定した種類の施設の現在Lvを返す。該当データが無い場合は1を返す（防御的フォールバック）。</summary>
        public int GetFacilityLevel(FacilityType type)
        {
            foreach (var facility in Facilities)
                if (facility.Type == type) return facility.CurrentLevel;
            return 1;
        }

        private static List<Facility> CreateDefaultFacilities() => new()
        {
            new Facility { Type = FacilityType.Dormitory, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Infirmary, CurrentLevel = 1 },
            new Facility { Type = FacilityType.TrainingGround, CurrentLevel = 1 },
            new Facility { Type = FacilityType.WarRoom, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Tavern, CurrentLevel = 1 },
        };
    }
}
