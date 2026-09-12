using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;

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
        /// 引退した冒険者の一覧（40歳強制引退・早期引退の両方。仕様書 03 §3.7・§7）。
        /// 現役ロースター（Adventurers）からは除外しつつ、データとしては破棄しない。
        /// 「顧問候補」として、AssignedTrainers・AssignedAdvisor・AssignedScoutMasterの
        /// いずれかに任意で任命できる。
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
        /// 冒険者2人1組ごとの相性値（0〜100。仕様書 03 §5.3.1）。キーは常に
        /// (小さいGuid, 大きいGuid) の順に正規化して格納する（→ CompatibilitySystem.
        /// NormalizeKey）。未登録のペアは初期値50（中立）として扱う
        /// （→ CompatibilitySystem.GetCompatibility。値が変化するまでは
        /// このDictionaryにエントリを作らない）。
        /// </summary>
        public Dictionary<(Guid, Guid), int> Compatibility { get; set; } = new();

        /// <summary>
        /// 進行中（派遣中）の複数週クエスト一覧（仕様書 03 §4.0.1）。満了週になるまで
        /// QuestDispatchSystem.ProcessWeeklyDispatches がここから取り除かない限り残り続ける。
        /// </summary>
        public List<ActiveDispatch> ActiveDispatches { get; set; } = new();

        /// <summary>
        /// 訓練施設に配置されている冒険者と、配置先の施設種別（→ 03 §3.1〜3.4「成長トリガー・
        /// 経路2」・§6）。v1.3改訂：訓練場・道場の4分割に伴い、単一のHashSet&lt;Guid&gt;から
        /// 「どの施設に配置されているか」まで持つDictionaryに変更した。
        /// </summary>
        public Dictionary<Guid, FacilityType> TrainingAssignments { get; set; } = new();

        /// <summary>
        /// 各訓練施設（戦士訓練所/教会/魔法研究所/斥候所）に配置されている教官
        /// （引退済み冒険者。仕様書 03 §7.1）。施設ごとに1名まで。未配置の施設はキー自体が
        /// 存在しないか値がnull。
        /// </summary>
        public Dictionary<FacilityType, Guid?> AssignedTrainers { get; set; } = new();

        /// <summary>作戦資料室に配置されている参謀（引退済み冒険者。仕様書 03 §7.2）。1名まで。null＝未配置。</summary>
        public Guid? AssignedAdvisor { get; set; }

        /// <summary>任命されているスカウト顧問（引退済み冒険者。仕様書 03 §7.3）。施設に紐づかない。1名まで。null＝未任命。</summary>
        public Guid? AssignedScoutMaster { get; set; }

        /// <summary>
        /// 8施設の現在状態（仕様書 03 §6。v1.3改訂：訓練場・道場の4分割により4大施設から
        /// 8施設に拡張）。デフォルトで全種Lv1を1つずつ持つ。
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

        /// <summary>
        /// 治安の脅威度（0〜100。仕様書 03 §4.4・§8.3）。討伐クエストの放置・失敗で上昇、
        /// 達成で減少する。75%超で月次助成金50%カット、100%到達で即時敗北。
        /// </summary>
        public int ThreatLevel { get; set; } = SecurityBalance.InitialThreatLevel;

        /// <summary>
        /// 所持金がマイナスの週が連続何週続いているか（仕様書 03 §8.3「破産」）。
        /// プラスに戻った週に0へリセットされる。DefeatSystem.ProcessWeeklySettlementが更新する。
        /// </summary>
        public int ConsecutiveNegativeGoldWeeks { get; set; } = 0;

        /// <summary>
        /// 敗北理由（仕様書 03 §8.3）。null＝まだ敗北していない。一度確定したら変化しない
        /// （DefeatSystemが上書きしない）。
        /// </summary>
        public DefeatReason? DefeatReason { get; set; }

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
            new Facility { Type = FacilityType.WarRoom, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Tavern, CurrentLevel = 1 },
            new Facility { Type = FacilityType.WarriorHall, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Church, CurrentLevel = 1 },
            new Facility { Type = FacilityType.MageLab, CurrentLevel = 1 },
            new Facility { Type = FacilityType.ScoutPost, CurrentLevel = 1 },
        };
    }
}
