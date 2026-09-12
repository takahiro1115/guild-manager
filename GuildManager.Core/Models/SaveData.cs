using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// セーブデータのルートモデル。仕様書 03 §12 参照。ゲーム全体の状態
    /// （GameState）を1つのJSONオブジェクトへシリアライズするための変換先。
    ///
    /// GameStateをそのまま直接シリアライズしない理由：
    /// - Compatibilityが`Dictionary&lt;(Guid,Guid),int&gt;`で、タプルキーは
    ///   System.Text.Jsonで直接シリアライズできない（→ CompatibilityPairs）。
    /// - TrainingAssignmentsも同様にGuidキーの辞書のため、一覧形式に変換する
    ///   （→ TrainingAssignments、こちらも一覧に変換）。
    /// - Party（ActiveDispatch内）はprivateな内部リストしか持たず、外部からの
    ///   直接構築ができないため、メンバーIdの一覧に変換する（→ DispatchedQuests）。
    /// - 教官（施設ごと）・参謀（作戦資料室）・スカウト（冒険者支援室）の3種類の
    ///   顧問割り当てto、v1.4改訂でスカウト・参謀も「1つの施設に紐づくポスト」と
    ///   同じ扱いになったため、施設種別名→冒険者Idの単一辞書に統合する
    ///   （→ AdvisorAssignments）。
    /// </summary>
    public class SaveData
    {
        /// <summary>
        /// セーブフォーマットのバージョン。将来の互換性のため必ず持たせる。
        /// 現時点ではマイグレーション処理は実装しないが、バージョン不一致を
        /// 検知できるようにしておく（→ SaveLoadService.CurrentSaveVersion）。
        /// </summary>
        public int SaveVersion { get; set; } = 1;

        // ---- ギルド全体のスカラー値 ----
        public int CurrentTurn { get; set; }
        public int Money { get; set; }
        public int Reputation { get; set; }
        public string GuildRank { get; set; } = ""; // enum→文字列で保存
        public int ThreatLevel { get; set; }

        /// <summary>
        /// 破産判定（→ 03 §8.3）の連続週数カウンタ。指示書のSaveDataサンプルには
        /// 含まれていなかったが、これを保存しないとロード直後に破産寸前の
        /// 状況がリセットされてしまう（実質的なチート状態になる）ため追加した。
        /// </summary>
        public int ConsecutiveNegativeGoldWeeks { get; set; }

        /// <summary>
        /// 敗北理由（→ 03 §8.3）。null＝まだ敗北していない。指示書のSaveData
        /// サンプルには含まれていなかったが、省略すると既に敗北確定した
        /// セーブをロードした際に敗北状態が解除されてしまうため追加した。
        /// </summary>
        public string? DefeatReason { get; set; }

        /// <summary>
        /// 現ランク相当のクエストを最後に達成してから経過した週数（→ 03 §8.1.1）。
        /// 指示書のSaveDataサンプルには含まれていなかったが、省略すると名声の
        /// 自然減衰カウンタがロードのたびにリセットされてしまうため追加した。
        /// </summary>
        public int WeeksSinceLastRankAppropriateQuest { get; set; }

        // ---- 冒険者関連 ----

        public List<Adventurer> ActiveAdventurers { get; set; } = new();

        /// <summary>
        /// 引退済み・顧問候補の一覧。指示書は`RetiredAdventurerRecord`という
        /// 氏名・引退週・年齢・職業のみを持つ最小限の型を提案していたが、
        /// 既存の`GameState.RetiredAdventurers`は`List&lt;Adventurer&gt;`
        /// （Adventurer本体をそのまま保持する設計）であり、顧問効果の算出
        /// （AdvisorSystem.GetTrainerBonus等）は生涯ピーク値（PeakSTR等）や
        /// 特性（TraitIds）を参照する。最小限のRecordに変換すると、これらの
        /// 情報がロード後に失われ、ロード直後の顧問効果が正しく算出できなく
        /// なってしまう。指示書の「既存実装をそのまま流用する」という方針
        /// （Record新設は"無ければ"のフォールバック）に従い、既存の
        /// `List&lt;Adventurer&gt;`をそのまま流用する。
        /// </summary>
        public List<Adventurer> RetiredAdvisorCandidates { get; set; } = new();

        /// <summary>
        /// 戦死者記録の一覧。RetiredAdvisorCandidatesと同じ理由でAdventurer型を
        /// そのまま流用する（氏名・戦死週・年齢・職業に加え、将来の一覧表示等で
        /// 他のフィールドが必要になった場合にも対応できるようにするため）。
        /// </summary>
        public List<Adventurer> FallenAdventurers { get; set; } = new();

        // ---- 相性（→ CompatibilitySystem） ----
        public List<CompatibilityPairRecord> CompatibilityPairs { get; set; } = new();

        // ---- 訓練施設への配置（GameState.TrainingAssignmentsのGuidキー辞書を一覧化） ----
        public List<TrainingAssignmentRecord> TrainingAssignments { get; set; } = new();

        // ---- 施設 ----
        public Dictionary<string, int> FacilityLevels { get; set; } = new(); // FacilityType名→Lv
        public string? FacilityUnderConstruction { get; set; } // FacilityType名 or null
        public int ConstructionWeeksRemaining { get; set; }
        public int ConstructionTargetLevel { get; set; }

        /// <summary>
        /// 顧問割り当て（施設種別名→冒険者Id）。教官（訓練4施設）・参謀（作戦資料室
        /// ＝WarRoom）・スカウト（冒険者支援室＝RecruitmentOffice）をまとめて
        /// 1つの辞書に統合する（v1.4改訂でスカウト・参謀も施設に紐づくポストに
        /// なったため。→ GameState.FromSaveData/ToSaveDataでの分配ロジック参照）。
        /// </summary>
        public Dictionary<string, Guid?> AdvisorAssignments { get; set; } = new();

        // ---- クエスト ----
        public List<Quest> AvailableQuests { get; set; } = new();
        public List<DispatchedQuestRecord> DispatchedQuests { get; set; } = new();

        // 装備カタログは静的コード定義のため保存不要（Adventurer側は装備IDの
        // 文字列のみ保持しているため、カタログさえコード内にあれば復元できる）
    }

    public class CompatibilityPairRecord
    {
        public Guid IdA { get; set; }
        public Guid IdB { get; set; }
        public int Value { get; set; }
    }

    /// <summary>訓練施設への配置1件分（→ GameState.TrainingAssignments）。</summary>
    public class TrainingAssignmentRecord
    {
        public Guid AdventurerId { get; set; }
        public string Facility { get; set; } = "";
    }

    /// <summary>
    /// 派遣中（複数週クエスト）1件分。指示書のサンプルは`QuestId`のみを保持する
    /// 設計だったが、派遣されたクエストは`QuestDispatchSystem.Dispatch`の時点で
    /// `GameState.AvailableQuests`から取り除かれる（→ QuestDispatchSystem.cs）ため、
    /// IDだけを保存してもロード後に参照先が存在せず、名前・難易度・報酬額等を
    /// 復元できない。そのため、クエスト本体をこの中に直接保持する設計に変更した。
    /// </summary>
    public class DispatchedQuestRecord
    {
        public Quest Quest { get; set; } = new();
        public List<Guid> PartyMemberIds { get; set; } = new();
        public int WeeksRemaining { get; set; }
    }
}
