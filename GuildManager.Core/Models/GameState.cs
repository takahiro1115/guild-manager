using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// 初期資金。→ BAL: 経済/初期資金（economy.csv、EconomyBalance.InitialGold。
        /// → 03 §10.1、項目58）。SaveDataからのロード時（FromSaveData）はオブジェクト
        /// 初期化子の評価順序により、この初期値がdata.Moneyで確実に上書きされる。
        /// </summary>
        public int Gold { get; set; } = EconomyBalance.InitialGold;

        public List<Adventurer> Adventurers { get; set; } = new();

        /// <summary>
        /// 永続的なパーティー編成の一覧（仕様書 03 §4.0.2。v1.9改訂で新設）。
        /// クエスト派遣のたびに毎回4人を選び直す必要はなく、事前に編成したこの一覧から
        /// 1つ選ぶだけでよい（→ PartyFormationSystem・QuestDispatchSystem）。
        /// どのSavedPartyのMemberIdsにも含まれない現役冒険者は「未編成」として扱う
        /// （→ PartyFormationSystem.GetUnassignedAdventurers）。
        /// </summary>
        public List<SavedParty> SavedParties { get; set; } = new();

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

        /// <summary>
        /// 任命されているスカウト顧問（引退済み冒険者。仕様書 03 §7.3）。1名まで。null＝未任命。
        /// v1.4改訂：冒険者支援室（RecruitmentOffice）に紐づく役職となった。同施設がLv0
        /// （未建設）の間は、AdvisorSystem.TryAssignScoutMaster側でガードし配置できない。
        /// </summary>
        public Guid? AssignedScoutMaster { get; set; }

        /// <summary>
        /// 9施設の現在状態（仕様書 03 §6。v1.3改訂：訓練場・道場の4分割により4大施設から
        /// 8施設に拡張。v1.4改訂：冒険者支援室（RecruitmentOffice）を新設し9施設に拡張）。
        /// 宿舎・医務室・ギルド酒場のみ初期Lv1、残り6施設（訓練4施設・作戦資料室・
        /// 冒険者支援室）は初期Lv0（未建設）で開始する（→ §6）。
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
        /// 「最終討伐クエストの依頼が持ち込まれるようになった」フラグ（仕様書 03 §8.2。
        /// v1.10改訂で新設）。ギルド格付けが初めてAランク以上に到達した時点でtrueになり、
        /// 以後は降格しても取り消されない（→ GuildRankSystem.UpdateRank）。最終討伐クエスト
        /// 自体（出現条件・難易度・専用ロジック）は未設計のまま（ストーリー検討後に別途設計）。
        /// </summary>
        public bool FinalQuestUnlocked { get; set; } = false;

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

        // ==================== セーブ/ロード（→ 03 §12） ====================

        /// <summary>
        /// 現在の状態をセーブ用データ（SaveData）へ変換する。System.Text.Jsonで
        /// 直接シリアライズできない形（タプルキーの辞書・Party等）を、
        /// JSON化可能な形（一覧・文字列キーの辞書）に変換する処理を担う。
        /// </summary>
        public SaveData ToSaveData()
        {
            var data = new SaveData
            {
                CurrentTurn = WeekNumber,
                Money = Gold,
                Reputation = Reputation,
                GuildRank = GuildRank.ToString(),
                ThreatLevel = ThreatLevel,
                ConsecutiveNegativeGoldWeeks = ConsecutiveNegativeGoldWeeks,
                DefeatReason = DefeatReason?.ToString(),
                WeeksSinceLastRankAppropriateQuest = WeeksSinceLastRankAppropriateQuest,
                FinalQuestUnlocked = FinalQuestUnlocked,
                ActiveAdventurers = new List<Adventurer>(Adventurers),
                RetiredAdvisorCandidates = new List<Adventurer>(RetiredAdventurers),
                FallenAdventurers = new List<Adventurer>(FallenAdventurers),
                AvailableQuests = new List<Quest>(AvailableQuests),
                SavedParties = new List<SavedParty>(SavedParties),
            };

            foreach (var kv in Compatibility)
                data.CompatibilityPairs.Add(new CompatibilityPairRecord { IdA = kv.Key.Item1, IdB = kv.Key.Item2, Value = kv.Value });

            foreach (var kv in TrainingAssignments)
                data.TrainingAssignments.Add(new TrainingAssignmentRecord { AdventurerId = kv.Key, Facility = kv.Value.ToString() });

            foreach (var facility in Facilities)
                data.FacilityLevels[facility.Type.ToString()] = facility.CurrentLevel;

            if (UnderConstruction != null)
            {
                data.FacilityUnderConstruction = UnderConstruction.Type.ToString();
                data.ConstructionTargetLevel = UnderConstruction.TargetLevel;
                data.ConstructionWeeksRemaining = UnderConstruction.WeeksRemaining;
            }

            // 教官（訓練施設ごと）・参謀（作戦資料室＝WarRoom）・スカウト（冒険者支援室＝
            // RecruitmentOffice）を、施設種別名→冒険者Idの単一辞書に統合する（v1.4改訂で
            // 参謀・スカウトも施設に紐づくポストになったため。→ SaveData.AdvisorAssignments）。
            foreach (var kv in AssignedTrainers)
                data.AdvisorAssignments[kv.Key.ToString()] = kv.Value;
            if (AssignedAdvisor.HasValue)
                data.AdvisorAssignments[FacilityType.WarRoom.ToString()] = AssignedAdvisor;
            if (AssignedScoutMaster.HasValue)
                data.AdvisorAssignments[FacilityType.RecruitmentOffice.ToString()] = AssignedScoutMaster;

            foreach (var dispatch in ActiveDispatches)
            {
                data.DispatchedQuests.Add(new DispatchedQuestRecord
                {
                    Quest = dispatch.Quest,
                    PartyMemberIds = dispatch.Party.Members.Select(m => m.Id).ToList(),
                    WeeksRemaining = dispatch.WeeksRemaining,
                    // 派遣中パーティが携行する消耗品はQuestResolver.Resolve時（満了週）まで
                    // 消費されずPartyに残り続けるため、複数週クエストの派遣中は必ず保存する
                    // （→ DispatchedQuestRecord.ConsumableItemIdsのコメント参照）。
                    ConsumableItemIds = new List<string>(dispatch.Party.ConsumableItemIds),
                });
            }

            return data;
        }

        /// <summary>
        /// セーブ用データ（SaveData）から状態を復元する。列挙型の文字列パースに
        /// 失敗した場合（セーブフォーマットの破損・非対応バージョンとの混入等）は
        /// FormatExceptionを投げる。呼び出し側（SaveLoadService.Load）はこれを
        /// 捕捉し、ロード失敗として扱う想定（→ 03 §12「ロード失敗時は新規ゲームの
        /// みを提示し、既存セーブを保護する」）。
        /// </summary>
        public static GameState FromSaveData(SaveData data)
        {
            var state = new GameState
            {
                WeekNumber = data.CurrentTurn,
                Gold = data.Money,
                Reputation = data.Reputation,
                GuildRank = ParseEnum<GuildRank>(data.GuildRank, nameof(GuildRank)),
                ThreatLevel = data.ThreatLevel,
                ConsecutiveNegativeGoldWeeks = data.ConsecutiveNegativeGoldWeeks,
                DefeatReason = data.DefeatReason == null ? null : ParseEnum<DefeatReason>(data.DefeatReason, nameof(DefeatReason)),
                WeeksSinceLastRankAppropriateQuest = data.WeeksSinceLastRankAppropriateQuest,
                FinalQuestUnlocked = data.FinalQuestUnlocked,
                Adventurers = new List<Adventurer>(data.ActiveAdventurers),
                RetiredAdventurers = new List<Adventurer>(data.RetiredAdvisorCandidates),
                FallenAdventurers = new List<Adventurer>(data.FallenAdventurers),
                AvailableQuests = new List<Quest>(data.AvailableQuests),
                SavedParties = new List<SavedParty>(data.SavedParties),
                Facilities = new List<Facility>(),
            };

            foreach (var record in data.CompatibilityPairs)
            {
                // CompatibilitySystem.NormalizeKeyと同じ正規化ルール（小さいGuid,大きいGuid）。
                // Modelsレイヤーの循環参照を避けるため、Systems層を参照せずここで直接計算する。
                var key = record.IdA.CompareTo(record.IdB) <= 0 ? (record.IdA, record.IdB) : (record.IdB, record.IdA);
                state.Compatibility[key] = record.Value;
            }

            foreach (var record in data.TrainingAssignments)
                state.TrainingAssignments[record.AdventurerId] = ParseEnum<FacilityType>(record.Facility, nameof(FacilityType));

            foreach (var kv in data.FacilityLevels)
                state.Facilities.Add(new Facility { Type = ParseEnum<FacilityType>(kv.Key, nameof(FacilityType)), CurrentLevel = kv.Value });

            if (data.FacilityUnderConstruction != null)
            {
                state.UnderConstruction = new FacilityConstruction
                {
                    Type = ParseEnum<FacilityType>(data.FacilityUnderConstruction, nameof(FacilityType)),
                    TargetLevel = data.ConstructionTargetLevel,
                    WeeksRemaining = data.ConstructionWeeksRemaining,
                };
            }

            foreach (var kv in data.AdvisorAssignments)
            {
                var facilityType = ParseEnum<FacilityType>(kv.Key, nameof(FacilityType));
                if (facilityType == FacilityType.WarRoom)
                    state.AssignedAdvisor = kv.Value;
                else if (facilityType == FacilityType.RecruitmentOffice)
                    state.AssignedScoutMaster = kv.Value;
                else
                    state.AssignedTrainers[facilityType] = kv.Value;
            }

            // Party・派遣中クエストの復元：同一のAdventurerインスタンスを使い回すため
            // （派遣中メンバーの状態変化が現役ロースター側にも同じインスタンスとして
            // 反映されるよう）、Id→Adventurerの参照辞書を1つ作ってから引く。
            var adventurersById = state.Adventurers
                .Concat(state.RetiredAdventurers)
                .Concat(state.FallenAdventurers)
                .ToDictionary(a => a.Id);

            foreach (var record in data.DispatchedQuests)
            {
                var party = new Party();
                foreach (var memberId in record.PartyMemberIds)
                {
                    if (!adventurersById.TryGetValue(memberId, out var member))
                        throw new FormatException($"セーブデータが破損しています：派遣中パーティのメンバーId {memberId} が見つかりません。");
                    party.TryAdd(member);
                }
                party.ConsumableItemIds = new List<string>(record.ConsumableItemIds);

                state.ActiveDispatches.Add(new ActiveDispatch { Party = party, Quest = record.Quest, WeeksRemaining = record.WeeksRemaining });
            }

            return state;
        }

        private static TEnum ParseEnum<TEnum>(string value, string enumTypeName) where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(value, out var result))
                return result;
            throw new FormatException($"セーブデータが破損しています：'{value}' は有効な{enumTypeName}ではありません。");
        }

        /// <summary>
        /// 9施設の初期状態（→ 03 §6）。v1.4改訂：宿舎・医務室・ギルド酒場（基幹3施設）は
        /// 既存どおりLv1スタート、それ以外（訓練4施設・作戦資料室・冒険者支援室）は
        /// Lv0（未建設）スタートに変更した。Lv0の施設は訓練枠・顧問スロットが0扱いになる
        /// （→ FacilityBalance.GetTrainingSlotCapacity・AdvisorSystemの各Try*Assign*）。
        /// </summary>
        private static List<Facility> CreateDefaultFacilities() => new()
        {
            new Facility { Type = FacilityType.Dormitory, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Infirmary, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Tavern, CurrentLevel = 1 },
            new Facility { Type = FacilityType.WarRoom, CurrentLevel = 0 },
            new Facility { Type = FacilityType.WarriorHall, CurrentLevel = 0 },
            new Facility { Type = FacilityType.Church, CurrentLevel = 0 },
            new Facility { Type = FacilityType.MageLab, CurrentLevel = 0 },
            new Facility { Type = FacilityType.ScoutPost, CurrentLevel = 0 },
            new Facility { Type = FacilityType.RecruitmentOffice, CurrentLevel = 0 },
        };
    }
}
