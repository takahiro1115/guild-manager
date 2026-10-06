using System;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 施設（Facility）関連のバランス値。仕様書 03 §6・§6.1・§6.2 参照。
    /// 値は docs/04_バランス表/facility.csv から読み込む（→ 03 §10.1、項目58）。
    ///
    /// 各Lv依存の値は「Lv1で、施設未実装時代の旧固定値と一致する」よう選んである
    /// （例：宿舎Lv1で8枠、医務室Lv1で速度1倍、酒場Lv1で回復+1）。
    /// これにより施設システム導入前の既存テストの期待値を変えずに済む。
    ///
    /// 施設の専門（§0.76、→ FacilitySpecialty）：SpecialtyFromLevel（Lv3）までの効果は専門に関係なく同じで、
    /// それより上のLvの上乗せは選んだ専門の分だけ（→ SpecialtyLevels）。Lv依存の関数は Lv と専門を受け取る。
    ///
    /// MaxLevel（施設の最大Lv）は構造値のためCSV化せずコードに残す（→ 03 §10.1）。
    /// 訓練施設の判定（IsTrainingFacility）・訓練対象ステータス対応（GetTrainingTargetStats）・
    /// 施設ごとの専門の選択肢（GetSpecialtyOptions）も、施設の定義そのものであり構造値のため据え置く。
    /// </summary>
    public static class FacilityBalance
    {
        private const string FileName = "facility.csv";

        public const int MaxLevel = 5;

        private static readonly int UpgradeCostPerLevel = BalanceData.GetInt(FileName, "UpgradeCostPerLevel");
        private static readonly int ConstructionWeeksPerLevel = BalanceData.GetInt(FileName, "ConstructionWeeksPerLevel");
        private static readonly int TrainingSlotCapacity = BalanceData.GetInt(FileName, "TrainingSlotCapacity");
        private static readonly double[] TrainingGrowthMultipliers =
        {
            BalanceData.GetDouble(FileName, "TrainingGrowthMultiplier_Lv1"),
            BalanceData.GetDouble(FileName, "TrainingGrowthMultiplier_Lv2"),
            BalanceData.GetDouble(FileName, "TrainingGrowthMultiplier_Lv3"),
            BalanceData.GetDouble(FileName, "TrainingGrowthMultiplier_Lv4"),
            BalanceData.GetDouble(FileName, "TrainingGrowthMultiplier_Lv5"),
        };

        /// <summary>訓練施設で片方の能力に特化したときの成長の確率の倍率（施設の倍率に掛ける。§0.75）。</summary>
        public static readonly double TrainingSpecialtyGrowthMultiplier = BalanceData.GetDouble(FileName, "TrainingSpecialtyGrowthMultiplier");
        private static readonly int DormitoryBaseCapacity = BalanceData.GetInt(FileName, "DormitoryBaseCapacity");
        private static readonly int DormitoryCapacityPerLevel = BalanceData.GetInt(FileName, "DormitoryCapacityPerLevel");
        private static readonly int InfirmaryRecoverySpeedPerLevel = BalanceData.GetInt(FileName, "InfirmaryRecoverySpeedPerLevel");
        private static readonly double InfirmaryHpMultiplierBase = BalanceData.GetDouble(FileName, "InfirmaryHpMultiplierBase");
        private static readonly double InfirmaryHpMultiplierPerLevel = BalanceData.GetDouble(FileName, "InfirmaryHpMultiplierPerLevel");
        private static readonly int TavernSatisfactionPerLevel = BalanceData.GetInt(FileName, "TavernSatisfactionPerLevel");

        // ---- 施設の専門（§0.76） ----
        /// <summary>このLvから次のLvへの着工で専門を選ぶ（このLvまでの効果は専門に関係なく同じ）。</summary>
        public static readonly int SpecialtyFromLevel = BalanceData.GetInt(FileName, "SpecialtyFromLevel");
        private static readonly int RemodelCostPerLevel = BalanceData.GetInt(FileName, "RemodelCostPerLevel");
        /// <summary>改装（専門の選び直し）の工期（週）。</summary>
        public static readonly int RemodelWeeks = BalanceData.GetInt(FileName, "RemodelWeeks");
        private static readonly int RivalrySlotCapacity = BalanceData.GetInt(FileName, "RivalrySlotCapacity");
        private static readonly double RivalryGrowthRateLv4 = BalanceData.GetDouble(FileName, "RivalryGrowthRate_Lv4");
        private static readonly double RivalryGrowthRateLv5 = BalanceData.GetDouble(FileName, "RivalryGrowthRate_Lv5");
        private static readonly double SanatoriumHpMultiplierPerLevel = BalanceData.GetDouble(FileName, "SanatoriumHpMultiplierPerLevel");
        private static readonly int SanatoriumInjurySpeedPerLevel = BalanceData.GetInt(FileName, "SanatoriumInjurySpeedPerLevel");
        private static readonly double FieldAidSurvivalPerLevel = BalanceData.GetDouble(FileName, "FieldAidSurvivalPerLevel");
        private static readonly double AppraisalEyePerLevel = BalanceData.GetDouble(FileName, "AppraisalEyePerLevel");
        private static readonly int RecruitingCandidatesPerLevel = BalanceData.GetInt(FileName, "RecruitingCandidatesPerLevel");
        private static readonly int LeisureSatisfactionPerLevel = BalanceData.GetInt(FileName, "LeisureSatisfactionPerLevel");
        private static readonly int TradeSideJobGoldPerLevel = BalanceData.GetInt(FileName, "TradeSideJobGoldPerLevel");
        private static readonly double AnalysisIntelRatePerLevel = BalanceData.GetDouble(FileName, "AnalysisIntelRatePerLevel");
        private static readonly double PathfindingTraversalPerLevel = BalanceData.GetDouble(FileName, "PathfindingTraversalPerLevel");

        /// <summary>
        /// Lvアップの改築費（現在Lvから+1する費用）。→ BAL: 施設/改築費。
        /// v1.4改訂：Lv0→Lv1（新設）の着工にも既存のLv1→Lv2と同額を課す
        /// （currentLevel*係数のままだとLv0の場合に0＝無料建設になってしまうバグの修正。
        /// 「資金を払って建設して初めてLv1になる」という仕様に反するため）。
        /// </summary>
        public static int GetUpgradeCost(Models.FacilityType type, int currentLevel) => Math.Max(currentLevel, 1) * UpgradeCostPerLevel;

        /// <summary>着工から完成までの工事期間（週）。→ BAL: 施設/工事期間。</summary>
        public static int GetConstructionWeeks(Models.FacilityType type, int currentLevel) => Math.Max(currentLevel, 1) * ConstructionWeeksPerLevel;

        /// <summary>改装（専門の選び直し、§0.76）の費用＝今のLv×RemodelCostPerLevel。</summary>
        public static int GetRemodelCost(int currentLevel) => currentLevel * RemodelCostPerLevel;

        // ---------------- 施設の専門（§0.76） ----------------

        /// <summary>施設が選べる2つの専門（宿舎は持たないので空）。並びは画面の表示順。</summary>
        public static FacilitySpecialty[] GetSpecialtyOptions(FacilityType type) => type switch
        {
            FacilityType.DrillHall or FacilityType.Academy or FacilityType.SkillHall => new[] { FacilitySpecialty.Elite, FacilitySpecialty.Rivalry },
            FacilityType.Infirmary => new[] { FacilitySpecialty.Sanatorium, FacilitySpecialty.FieldAid },
            FacilityType.RecruitmentOffice => new[] { FacilitySpecialty.Appraisal, FacilitySpecialty.Recruiting },
            FacilityType.Tavern => new[] { FacilitySpecialty.Leisure, FacilitySpecialty.Trade },
            FacilityType.WarRoom => new[] { FacilitySpecialty.Analysis, FacilitySpecialty.Pathfinding },
            _ => Array.Empty<FacilitySpecialty>(),
        };

        /// <summary>施設が専門を持つか（宿舎以外）。</summary>
        public static bool HasSpecialty(FacilityType type) => GetSpecialtyOptions(type).Length > 0;

        /// <summary>
        /// 専門が付いていないのにLvが SpecialtyFromLevel を超えているとき（テストで直接Lvを上げたとき等）に使う専門：
        /// 選択肢の1つ目（精鋭・療養院・目利き・憩い・解析＝Lv3までの伸び方に近い側）。
        /// </summary>
        public static FacilitySpecialty GetDefaultSpecialty(FacilityType type) =>
            GetSpecialtyOptions(type) is { Length: > 0 } options ? options[0] : FacilitySpecialty.None;

        /// <summary>
        /// 専門の効果が乗るLvの数：専門が wanted なら max(0, Lv − SpecialtyFromLevel)、違えば0。
        /// </summary>
        public static int SpecialtyLevels(int level, FacilitySpecialty specialty, FacilitySpecialty wanted) =>
            specialty == wanted ? Math.Max(0, level - SpecialtyFromLevel) : 0;

        /// <summary>専門に関係なく効くLv（SpecialtyFromLevel で頭打ち）。</summary>
        public static int BaseLevel(int level) => Math.Min(level, SpecialtyFromLevel);

        // ---------------- 訓練施設 ----------------

        /// <summary>
        /// 訓練施設（鍛錬所/学問所/技巧所）の配置枠数。§0.75：Lv1以上なら TrainingSlotCapacity（1名）、Lv0（未建設）は0。
        /// §0.76：専門「切磋琢磨」でLv4以上なら RivalrySlotCapacity（2名）。Lvは枠ではなく成長の確率に効く（→ GetTrainingGrowthMultiplier）。
        /// </summary>
        public static int GetTrainingSlotCapacity(int facilityLevel, FacilitySpecialty specialty)
        {
            if (facilityLevel < 1) return 0;
            return SpecialtyLevels(facilityLevel, specialty, FacilitySpecialty.Rivalry) > 0 ? RivalrySlotCapacity : TrainingSlotCapacity;
        }

        /// <summary>
        /// 訓練施設のLvに連動する成長の確率の倍率（§0.75、→ BAL: 施設/TrainingGrowthMultiplier_Lv1〜5）。
        /// Lvは1〜MaxLevelに丸める（Lv0は枠が0で訓練生を置けないが、直接配置したときはLv1と同じに扱う）。
        /// §0.76：Lv4以上は専門で分かれる。精鋭＝Lv4・Lv5の表の値、切磋琢磨＝Lv3の値×RivalryGrowthRate_Lv4／Lv5。
        /// </summary>
        public static double GetTrainingGrowthMultiplier(int facilityLevel, FacilitySpecialty specialty)
        {
            int level = Math.Clamp(facilityLevel, 1, MaxLevel);
            int rivalry = SpecialtyLevels(level, specialty, FacilitySpecialty.Rivalry);
            if (rivalry > 0)
                return TrainingGrowthMultipliers[SpecialtyFromLevel - 1] * (rivalry >= 2 ? RivalryGrowthRateLv5 : RivalryGrowthRateLv4);
            if (level > SpecialtyFromLevel && specialty != FacilitySpecialty.Elite)
                level = SpecialtyFromLevel; // 専門が無い（あり得ないが念のため）ときはLv3の値
            return TrainingGrowthMultipliers[level - 1];
        }

        /// <summary>指定した施設種別が訓練施設（§0.75の3施設）かどうか。</summary>
        public static bool IsTrainingFacility(FacilityType type) =>
            type is FacilityType.DrillHall or FacilityType.Academy or FacilityType.SkillHall;

        /// <summary>
        /// 訓練施設ごとの成長ロール対象ステータス（経路2、→ 03 §3.1〜3.4・§6）。
        /// 鍛錬所→STR・VIT、学問所→MND・INT、技巧所→AGI・DEX（§0.75）。片方に特化したときはその能力だけが伸びる
        /// （→ GameState.TrainingSpecialtyStats・TrainingSystem.GetGrowthStats）。
        /// 訓練施設以外を渡すと例外（呼び出し側の設計ミスを早期検知するため）。
        /// </summary>
        public static string[] GetTrainingTargetStats(FacilityType type) => type switch
        {
            FacilityType.DrillHall => new[] { "STR", "VIT" },
            FacilityType.Academy => new[] { "MND", "INT" },
            FacilityType.SkillHall => new[] { "AGI", "DEX" },
            _ => throw new ArgumentException($"施設{type}は訓練施設ではありません（訓練対象ステータスを持ちません）"),
        };

        // ---------------- 宿舎・医務室・酒場・冒険者支援室・作戦資料室 ----------------

        /// <summary>宿舎Lvに連動する現役枠の上限。→ BAL: 施設/宿舎（Lv1=基礎枠、以降+係数/Lv）。宿舎は専門を持たない。</summary>
        public static int GetDormitoryCapacity(int dormitoryLevel) => DormitoryBaseCapacity + (dormitoryLevel - 1) * DormitoryCapacityPerLevel;

        /// <summary>
        /// 医務室Lvに連動する重傷回復速度（1週あたりのInjuryWeeksRemaining減少量）。
        /// → BAL: 施設/医務室（Lv3までLv×係数。Lv1＝施設未実装時と同じ速度）＋専門「療養院」のLv4以上の上乗せ（§0.76）。
        /// </summary>
        public static int GetInfirmaryInjuryRecoverySpeed(int infirmaryLevel, FacilitySpecialty specialty) =>
            BaseLevel(infirmaryLevel) * InfirmaryRecoverySpeedPerLevel
            + SpecialtyLevels(infirmaryLevel, specialty, FacilitySpecialty.Sanatorium) * SanatoriumInjurySpeedPerLevel;

        /// <summary>
        /// 医務室Lvに連動するHP自然回復（§3.5改）の倍率。
        /// → BAL: 施設/医務室（Lv1=基礎倍率＝施設未実装時と同じ、Lv3まで+係数/Lv）＋専門「療養院」のLv4以上の上乗せ（§0.76）。
        /// </summary>
        public static double GetInfirmaryHpRecoveryMultiplier(int infirmaryLevel, FacilitySpecialty specialty) =>
            InfirmaryHpMultiplierBase + (BaseLevel(infirmaryLevel) - 1) * InfirmaryHpMultiplierPerLevel
            + SpecialtyLevels(infirmaryLevel, specialty, FacilitySpecialty.Sanatorium) * SanatoriumHpMultiplierPerLevel;

        /// <summary>医務室の専門「戦地救護」によるボス戦の致命の損耗の閾値の上乗せ（研究 SurvivalThresholdBonus と同じ単位。§0.76）。</summary>
        public static double GetFieldAidSurvivalBonus(int infirmaryLevel, FacilitySpecialty specialty) =>
            SpecialtyLevels(infirmaryLevel, specialty, FacilitySpecialty.FieldAid) * FieldAidSurvivalPerLevel;

        /// <summary>
        /// ギルド酒場Lvに連動する満足度の自然回復量（§5.1）。
        /// → BAL: 満足度/自然回復（Lv3までLv×係数。Lv1＝施設未実装時と同じ）＋専門「憩い」のLv4以上の上乗せ（§0.76）。
        /// </summary>
        public static int GetTavernSatisfactionRecovery(int tavernLevel, FacilitySpecialty specialty) =>
            BaseLevel(tavernLevel) * TavernSatisfactionPerLevel
            + SpecialtyLevels(tavernLevel, specialty, FacilitySpecialty.Leisure) * LeisureSatisfactionPerLevel;

        /// <summary>ギルド酒場の専門「商い」による内職の基本売上の上乗せ（G。機嫌の倍率の前。§0.76）。</summary>
        public static int GetTradeSideJobBonus(int tavernLevel, FacilitySpecialty specialty) =>
            SpecialtyLevels(tavernLevel, specialty, FacilitySpecialty.Trade) * TradeSideJobGoldPerLevel;

        /// <summary>冒険者支援室の専門「目利き」による目利きの上乗せ（上限は PotentialEstimateBalance.MaxEye。§0.76）。</summary>
        public static double GetAppraisalEyeBonus(int officeLevel, FacilitySpecialty specialty) =>
            SpecialtyLevels(officeLevel, specialty, FacilitySpecialty.Appraisal) * AppraisalEyePerLevel;

        /// <summary>冒険者支援室の専門「募集」による新春採用試験の応募者の上乗せ（名。§0.76）。</summary>
        public static int GetRecruitingCandidateBonus(int officeLevel, FacilitySpecialty specialty) =>
            SpecialtyLevels(officeLevel, specialty, FacilitySpecialty.Recruiting) * RecruitingCandidatesPerLevel;

        /// <summary>作戦資料室の専門「解析」による迷宮調査の解析率の上昇倍率の上乗せ（研究 IntelRateBonus と同じ単位。§0.76）。</summary>
        public static double GetAnalysisIntelBonus(int warRoomLevel, FacilitySpecialty specialty) =>
            SpecialtyLevels(warRoomLevel, specialty, FacilitySpecialty.Analysis) * AnalysisIntelRatePerLevel;

        /// <summary>作戦資料室の専門「踏破」による道中の走破力の上乗せ（研究 TraversalBonus と同じ単位。§0.76）。</summary>
        public static double GetPathfindingTraversalBonus(int warRoomLevel, FacilitySpecialty specialty) =>
            SpecialtyLevels(warRoomLevel, specialty, FacilitySpecialty.Pathfinding) * PathfindingTraversalPerLevel;
    }
}
