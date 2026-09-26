using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 冒険者データモデル。仕様書 03 §2 参照。
    /// 性格・相性はまだ実装しない（→ docs/06_タスクリスト.md Phase 3 で追加予定）。
    /// </summary>
    public class Adventurer
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "名無し";

        /// <summary>プレイヤーが改名で付けられる名前の最大文字数（→ Rename。個人詳細・編成スロットの表示幅に収めるため）。</summary>
        public const int MaxNameLength = 12;

        /// <summary>
        /// プレイヤーによる任意の改名（→ 03 §2.1、2026年9月新設。「部隊・冒険者」画面の個人詳細ペインの ✏️ ボタン）。
        /// 前後の空白を取り除いたうえで、1〜MaxNameLength（12）文字（string.Length で数える＝全角・半角を区別しない）なら
        /// Name を書き換える。null・空文字・空白のみ・13文字以上は ArgumentException。Id・セーブデータの構造は変えない。
        /// </summary>
        public void Rename(string? newName)
        {
            string trimmed = newName?.Trim() ?? "";
            if (trimmed.Length == 0)
                throw new ArgumentException("名前を入力してください（空白のみは不可）。", nameof(newName));
            if (trimmed.Length > MaxNameLength)
                throw new ArgumentException($"名前は{MaxNameLength}文字以内にしてください（{trimmed.Length}文字）。", nameof(newName));
            Name = trimmed;
        }
        public int Age { get; set; } = 18;
        public JobClass JobClass { get; set; }

        /// <summary>
        /// 性別。仕様書 03 §2.4参照（v1.8改訂で新設）。現時点ではステータス・成長・戦闘の
        /// いずれにも影響を与えない。氏名生成（NameGenerator）と、将来の立ち絵システム
        /// 連携（次フェーズ、post-MVP）のための土台として先行して追加したフィールドである。
        /// </summary>
        public Gender Gender { get; set; }

        /// <summary>
        /// ポートレート画像のID（→ 03 §2.1、項目62で新設）。null（未設定）が正常系
        /// （採用で生成される冒険者は基本的に持たない）。装備の VisualPartId と同じ
        /// 「IDだけCoreに持たせ、実際の画像解決はGodot側」というパターンに揃えてある：
        /// Godot側は `res://assets/portraits/{PortraitId}.png` を解決し、null または
        /// ファイルが存在しない場合はシルエット画像（unknown_silhouette.png）を表示する。
        /// 将来のモンタージュ方式（職業×性別×装備での合成）へ差し替える際も、
        /// 「PortraitIdが無い場合の分岐」を合成ロジックに置き換えるだけで済む設計。
        /// </summary>
        public string? PortraitId { get; set; }

        /// <summary>
        /// 配置（前衛/後衛）。仕様書 03 §4.2 参照。冒険者個人に紐づく永続状態で、
        /// クエストをまたいで保持される。生成時に PlacementRules.GetDefault(JobClass) で
        /// 職業に応じた初期値を設定する想定（→ SampleData・RecruitmentSystem）。
        /// 配置は職業で固定されない。どの職業でも自由に前衛/後衛を選べる
        /// （ユーザー決定：「魔法使いや僧侶も前衛になることができる。職業で固定になることはない」）。
        /// 配置は火力に影響しない（職業×配置の個人CP補正は2026年9月に撤廃、→ DungeonPowerCalculator）。
        /// 実プレイでは職業から一意に決まる値（→ PlacementRules）がUIの前衛／後衛表示に使われる。
        /// </summary>
        public Placement Placement { get; set; } = Placement.Front;

        /// <summary>
        /// 配置を変更する。職業による制約は無いため常に成功するが、Party.TryAdd等の
        /// 既存の「Try」系メソッドと同じ形（bool戻り値）に揃えてある。
        /// </summary>
        public bool TrySetPlacement(Placement placement)
        {
            Placement = placement;
            return true;
        }

        // ---- 能力値（実効値 1〜100）。仕様書 03 §2.2 ----
        public int STR { get; set; }
        public int AGI { get; set; }
        public int VIT { get; set; }
        public int MND { get; set; }
        public int DEX { get; set; }
        public int LDR { get; set; }

        /// <summary>
        /// v1.2改訂：予約フィールドから活性化し、個人CP・成長ロール対象・総合PA算出に参加する
        /// （→ 03 §2.2・§4.2・§10）。
        /// </summary>
        public int INT { get; set; }

        // ---- 潜在能力 PA（各ステータスの成長上限。1〜100）。仕様書 03 §2.2 ----
        // 実効値はデフォルトでは0から始まるため、PAのデフォルトは範囲の最大値にしておき、
        // 明示的に潜在能力を絞らない限り既存の挙動（成長上限なし相当）を壊さないようにする。
        public int PA_STR { get; set; } = 100;
        public int PA_AGI { get; set; } = 100;
        public int PA_VIT { get; set; } = 100;
        public int PA_MND { get; set; } = 100;
        public int PA_DEX { get; set; } = 100;
        public int PA_LDR { get; set; } = 100;
        public int PA_INT { get; set; } = 100;

        /// <summary>総合PA＝7つのPAの平均（v1.2改訂：INT活性化に伴い6値→7値平均。採用試験・スカウト評価用。仕様書 03 §2.2）。</summary>
        public double TotalPA => (PA_STR + PA_AGI + PA_VIT + PA_MND + PA_DEX + PA_LDR + PA_INT) / 7.0;

        // 生涯ピーク値（PeakSTR等）は、加齢衰微の廃止（v2.0、8年稼働モデル）により実効値が
        // 下がる経路が無くなったため撤廃した。顧問効果は引退時の実効ステータス（STR等）を
        // 直接参照する（→ AdvisorSystem、仕様書 03 §2.2・§7）。旧セーブに残る Peak* 項目は
        // デシリアライズ時に無視される。

        // ---- 特性（Trait）。仕様書 03 §5.3・§4.3 参照。 ----

        /// <summary>
        /// 特性スロットの上限（→ 特性伝授・スロット上限刷新仕様）。満杯の状態では、
        /// 致死判定の古傷付与・採用時の先天特性抽選・教官からの伝授（→ TrainingSystem.
        /// ProcessWeeklyTraitTransmission）のいずれも新規の特性を追加できない
        /// （TryAddTraitがfalseを返す。既存の呼び出し側は元々戻り値を無視できる設計のため、
        /// 満杯時に静かに失敗しても既存の処理は壊れない）。
        ///
        /// 2026年9月（特性スロット自由枠、→ 03 §5.3）：5枠は先天・修練・障害の内訳を持たない**自由枠**で、
        /// 1本のリスト（TraitIds）で管理する。障害・呪い特性（TraitDefinition.IsCurseOrInjury：古傷・トラウマ）は
        /// 忘却・上書きできず、枠を恒久的に占有する（→ CanRemoveTrait）。
        /// </summary>
        public const int MaxTraitCount = 5;

        /// <summary>この冒険者が保持している特性のIdリスト。重複するIdは持てない。</summary>
        public List<string> TraitIds { get; set; } = new();

        public bool HasTrait(string traitId) => TraitIds.Contains(traitId);

        /// <summary>特性を追加できるか：未所持で、かつ所持数が上限（MaxTraitCount）未満。</summary>
        public bool CanAddTrait(string traitId) => !TraitIds.Contains(traitId) && TraitIds.Count < MaxTraitCount;

        /// <summary>
        /// 特性を付与する。既に持っている、またはスロットが満杯（→ MaxTraitCount）の場合は
        /// 何もせずfalseを返す。
        /// ただし障害・呪い特性（古傷・トラウマ）は満杯でも通常特性を侵食して必ず付く（→ TryAddCurseTrait）。
        /// 「5枠を埋めておけば障害を受けない」という抜け道を作らないため（2026年9月、→ 03 §5.3.2）。
        /// </summary>
        public bool TryAddTrait(string traitId) =>
            TraitCatalog.FindById(traitId)?.IsCurseOrInjury == true
                ? TryAddCurseTrait(traitId, out _)
                : TryAddNormalTrait(traitId);

        private bool TryAddNormalTrait(string traitId)
        {
            if (!CanAddTrait(traitId)) return false;
            TraitIds.Add(traitId);
            return true;
        }

        /// <summary>
        /// 障害・呪い特性を付与する（→ 03 §5.3.2「障害特性の侵食」）。
        /// 空き枠があれば末尾に追加する。5枠満杯なら、所持している**通常特性（障害でないもの）のうち最も後ろの枠**を
        /// 強制的に忘却させ、その枠を障害特性で上書きする（erodedTraitId に消えた特性のId）。
        /// 5枠すべてが障害特性で埋まっている場合、または既に同じ特性を持っている場合は何もせず false。
        /// 障害特性でない特性を渡した場合は侵食せず、通常の追加（空き枠がある時だけ）として扱う。
        /// </summary>
        public bool TryAddCurseTrait(string traitId, out string? erodedTraitId)
        {
            erodedTraitId = null;
            if (TraitCatalog.FindById(traitId)?.IsCurseOrInjury != true)
                return TryAddNormalTrait(traitId);
            if (TraitIds.Contains(traitId)) return false;

            if (TraitIds.Count < MaxTraitCount)
            {
                TraitIds.Add(traitId);
                return true;
            }

            for (int i = TraitIds.Count - 1; i >= 0; i--)
            {
                if (!CanRemoveTrait(TraitIds[i])) continue;
                erodedTraitId = TraitIds[i];
                TraitIds[i] = traitId;
                return true;
            }

            return false; // 5枠すべてが障害特性
        }

        /// <summary>
        /// 特性を外せる（忘却・上書きの削除側にできる）か：所持しており、障害・呪い特性
        /// （TraitDefinition.IsCurseOrInjury）でないこと。障害特性は枠を恒久的に占有する。
        /// カタログから引けない特性（旧データ）は障害扱いにせず、外せるものとして扱う。
        /// </summary>
        public bool CanRemoveTrait(string traitId) =>
            TraitIds.Contains(traitId) && TraitCatalog.FindById(traitId)?.IsCurseOrInjury != true;

        /// <summary>特性を忘却する。CanRemoveTrait を満たさなければ何もせず false。</summary>
        public bool TryRemoveTrait(string traitId) => CanRemoveTrait(traitId) && TraitIds.Remove(traitId);

        /// <summary>
        /// 特性を差し替える（oldTraitId の枠を newTraitId で上書きする）。旧特性が外せて（→ CanRemoveTrait）、
        /// 新特性を未所持なら成功する。枠数は入替の前後で変わらないため、満杯（5枠）でも入替はできる。
        /// 並び順（何枠目か）は保つ。条件を満たさなければ何もせず false。
        /// </summary>
        public bool TryReplaceTrait(string oldTraitId, string newTraitId)
        {
            if (oldTraitId == newTraitId) return false;
            if (!CanRemoveTrait(oldTraitId)) return false;
            if (TraitIds.Contains(newTraitId)) return false;

            TraitIds[TraitIds.IndexOf(oldTraitId)] = newTraitId;
            return true;
        }

        /// <summary>
        /// 特性による効果（StatPercentReduction）を反映した実効値に、装備の能力値補正
        /// （→ GetEquipmentStatBonus、03 §4.2.2）を加えた値を返す（→ 03 §4.3・§5.3）。
        /// 討伐火力・最大HP計算・大迷宮の各部隊指標（走破・隠密・解析・護衛）の共通ヘルパー。
        /// 特性の割合減は素の能力値にだけ掛かり、装備補正は減らない（古傷を負っても剣の重みは変わらない）。
        /// </summary>
        public double GetEffectiveStat(string statName)
        {
            int baseValue = statName switch
            {
                "STR" => STR,
                "VIT" => VIT,
                "AGI" => AGI,
                "DEX" => DEX,
                "MND" => MND,
                "INT" => INT,
                "LDR" => LDR,
                _ => throw new ArgumentException($"未知のステータス: {statName}")
            };

            double totalReduction = SumTraitEffect(TraitEffectType.StatPercentReduction, statName);

            // 減少しすぎて0以下にならないよう下限をクランプ（暫定：最大80%減まで）
            double multiplier = Math.Max(1.0 + totalReduction, 0.2);
            return baseValue * multiplier + GetEquipmentStatBonus(statName);
        }

        /// <summary>
        /// 保有する特性のうち、指定した効果種別（・対象ステータス）に該当する効果量の合計を返す
        /// （→ 03 §5.3）。GetEffectiveStat・QuestResolverの索敵/致死判定補正が共通で使う。
        /// targetStatをnullにすると対象ステータスを問わず合算する（SurvivalThresholdModifier等、
        /// TargetStatを使わない効果種別向け）。
        /// 乗数として使う効果種別（CompatibilityGainMultiplier）は加算ではなく積で合成すべきため、
        /// このヘルパーの対象外（呼び出し側で個別に扱う）。
        /// </summary>
        public double SumTraitEffect(TraitEffectType effectType, string? targetStat = null)
        {
            double total = 0;
            foreach (var traitId in TraitIds)
            {
                var def = TraitCatalog.FindById(traitId);
                if (def == null) continue;

                foreach (var effect in def.Effects)
                {
                    if (effect.EffectType == effectType && (targetStat == null || effect.TargetStat == targetStat))
                        total += effect.Value;
                }
            }
            return total;
        }

        /// <summary>
        /// 年齢帯（仕様書 03 §3.0 の定義表）。表の範囲外は近い側の帯に丸める：
        /// 18歳以下（旧モデルの15〜17歳を含む）は新鋭期、27歳以上（満期引退を越えた
        /// 旧データ・異常値）はすべて全盛期へ安全にクランプする（→ 03 §0.11）。
        /// </summary>
        public AgeBand AgeBand =>
            Age <= 18 ? AgeBand.Young :
            Age <= 22 ? AgeBand.Growing :
            AgeBand.Peak;

        // ---- 稼働期間と功績（→ 8年稼働・満期引退モデル。03 §3.7改訂） ----

        /// <summary>
        /// ギルドに在籍して稼働した週数（→ AgingSystem.ProcessWeeklyAging が毎週+1する）。
        /// 満期は AgingBalance.MaxActiveWeeks（8年＝384週）。退職金の功績加算と、
        /// UIでの「あと何週で満期か」の表示に使う。
        ///
        /// 引退判定そのものは年齢（26歳の年度末）で行う。加入年齢がばらつく初期メンバーでも
        /// 「26歳で引退する」という世界観上の約束を優先するため、稼働週数は退職金の算定根拠に留める。
        /// </summary>
        public int ActiveWeeks { get; set; } = 0;

        /// <summary>
        /// 累積功績（→ QuestDispatchSystem がクエスト達成時に加算する）。
        /// 退職金の上乗せ分の算定根拠（→ EconomyBalance.SeveranceContributionCoefficient）。
        /// 「危険な仕事をさせた分だけ手厚く送り出す」という方針を数値で表現したもの。
        /// </summary>
        public int TotalContributionScore { get; set; } = 0;

        /// <summary>26歳年度末で満期引退したか（仕様書 03 §3.7）。→ AgingSystem が設定する。
        /// 引退した冒険者は GameState.Adventurers から GameState.RetiredAdventurers へ移される。
        /// このフラグ自体は移動後も参照できるよう残す（防御的なガード・監査用）。</summary>
        public bool IsRetired { get; set; } = false;

        // ---- 引退記録（仕様書 03 §3.7つづき）。§7顧問制度が未実装の間の「引退済み・顧問候補」データ。 ----

        /// <summary>引退時点の年齢。強制引退は常に40だが、将来の任意引退等に備えて記録する。</summary>
        public int? RetiredAtAge { get; set; }

        /// <summary>引退した週番号（GameState.WeekNumber）。§7実装時の参考情報として保持。</summary>
        public int? RetiredAtWeek { get; set; }

        /// <summary>退職金（週給×12週。仕様書 03 §7）が支給済みか。</summary>
        public bool SeverancePaid { get; set; } = false;

        /// <summary>戦死した週番号（GameState.WeekNumber）。null＝戦死していない（仕様書 03 §4.3.1）。
        /// 戦死者記録として保持し、GameState.Adventurers から GameState.FallenAdventurers へ移される。</summary>
        public int? FellAtWeek { get; set; }

        // ---- 動的・コンディション属性。仕様書 03 §2.3 ----

        /// <summary>
        /// 最大HP = 実効VIT×係数 + 基礎値（仕様書 03 §2.3。→ BAL: 戦闘/combat.csv）
        /// ＋装備の最大HP加算（→ §4.2.2）。特性（例：古傷）による実効値低下、防具・
        /// アクセサリーの固定加算の両方を反映する。
        ///
        /// 事前調査メモ（項目58）：この計算式はModelであるAdventurer.csに直書きされていた
        /// （05技術メモ§3の方針違反。他のSystemクラスへの直書きと同様の問題）。
        /// CombatBalance（combat.csv）へ集約した。
        /// </summary>
        public int MaxHP => (int)(GetEffectiveStat("VIT") * CombatBalance.MaxHpVitCoefficient) + CombatBalance.MaxHpBase + GetEquipmentHpBonus();

        // ---- 装備（Weapon/Armor/Accessory1/Accessory2）。仕様書 03 §4.2.2 参照。 ----
        //
        // 2026年9月改訂（ギルド保管庫からの着脱、→ §4.2.2・§4.7）：スロットが持つのは
        // カタログIdの文字列ではなく「個体」（EquipmentItem）になった。理由は、装備を外した
        // ときに**どの個体**を保管庫（GameState.Armory）へ戻すのかを一意に決める必要があるため
        // （同じ鉄の剣を2本持てる。→ Models.EquipmentItem）。
        // 旧セーブ（Id文字列を持つ）との互換は下の Legacy* プロパティで吸収する。

        /// <summary>装備中の武器の個体。null＝未装備。</summary>
        public EquipmentItem? EquippedWeapon { get; set; }

        /// <summary>装備中の防具の個体。null＝未装備。</summary>
        public EquipmentItem? EquippedArmor { get; set; }

        /// <summary>装備中のアクセサリー1の個体。null＝未装備。</summary>
        public EquipmentItem? EquippedAccessory1 { get; set; }

        /// <summary>装備中のアクセサリー2の個体。null＝未装備。</summary>
        public EquipmentItem? EquippedAccessory2 { get; set; }

        /// <summary>
        /// 指定したスロットの装備個体を返す（スロット横断の汎用アクセサ。
        /// EquipmentSystem・UI側の装備状況表示の両方から使う）。
        /// </summary>
        public EquipmentItem? GetEquipped(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Weapon => EquippedWeapon,
            EquipmentSlot.Armor => EquippedArmor,
            EquipmentSlot.Accessory1 => EquippedAccessory1,
            EquipmentSlot.Accessory2 => EquippedAccessory2,
            _ => null,
        };

        /// <summary>指定したスロットへ装備個体を設定する（null＝解除）。保管庫との受け渡しは EquipmentSystem の責務。</summary>
        public void SetEquipped(EquipmentSlot slot, EquipmentItem? item)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon: EquippedWeapon = item; break;
                case EquipmentSlot.Armor: EquippedArmor = item; break;
                case EquipmentSlot.Accessory1: EquippedAccessory1 = item; break;
                case EquipmentSlot.Accessory2: EquippedAccessory2 = item; break;
            }
        }

        /// <summary>全4スロットを浅い順に列挙する（UI・EquipmentSystem・保管庫への一括返却で使う）。</summary>
        public static readonly EquipmentSlot[] AllSlots =
        {
            EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2,
        };

        /// <summary>
        /// 指定したスロットの装備のカタログId（未装備ならnull）。個体（EquippedWeapon等）から
        /// 導出される読み取り専用の値で、セーブデータには書き出さない（→ LegacyEquippedWeaponId）。
        /// </summary>
        public string? GetEquippedId(EquipmentSlot slot) => GetEquipped(slot)?.ItemId;

        // 装備中のカタログId（旧APIと同じ名前・同じ意味）。個体から導出される読み取り専用の値で、
        // 正本は EquippedWeapon 等の個体。JsonIgnoreにしてあるのは、セーブデータへ二重に
        // 書き出さないため（旧キー名の受け皿は下の Legacy* が担う）。
        [JsonIgnore] public string? EquippedWeaponId => EquippedWeapon?.ItemId;
        [JsonIgnore] public string? EquippedArmorId => EquippedArmor?.ItemId;
        [JsonIgnore] public string? EquippedAccessory1Id => EquippedAccessory1?.ItemId;
        [JsonIgnore] public string? EquippedAccessory2Id => EquippedAccessory2?.ItemId;

        /// <summary>
        /// 指定したスロットへカタログIdで装備を設定する（null＝解除）。カタログ定義から**新しい個体**を
        /// 作って差し込むため、保管庫の在庫を消費しない購入経路（→ EquipmentSystem.TryPurchaseAndEquip）
        /// 専用。保管庫の現物を着ける場合は EquipmentSystem.TryEquip を使う。
        /// 未知のIdを渡した場合は解除として扱う（防御的）。
        /// </summary>
        public void SetEquippedId(EquipmentSlot slot, string? itemId)
        {
            var definition = ItemCatalog.FindById(itemId);
            SetEquipped(slot, definition == null ? null : EquipmentItem.FromCatalog(definition));
        }

        // ---- 旧セーブ互換（→ 03 §12） ----
        //
        // 2026年9月改訂前のセーブは、各スロットを "EquippedWeaponId":"IronSword" のような
        // カタログId文字列で持っている。下の4つは**セット専用**（getterを持たない＝新しい
        // セーブには書き出されない）のプロパティで、JSON側の旧キー名をそのまま受け取り、
        // カタログ定義から個体を復元する。個体（EquippedWeapon等）が既に入っている場合は
        // 何もしない（新形式が正本）。

        [JsonPropertyName("EquippedWeaponId")]
        public string? LegacyEquippedWeaponId { set => RestoreLegacyEquipment(EquipmentSlot.Weapon, value); }

        [JsonPropertyName("EquippedArmorId")]
        public string? LegacyEquippedArmorId { set => RestoreLegacyEquipment(EquipmentSlot.Armor, value); }

        [JsonPropertyName("EquippedAccessory1Id")]
        public string? LegacyEquippedAccessory1Id { set => RestoreLegacyEquipment(EquipmentSlot.Accessory1, value); }

        [JsonPropertyName("EquippedAccessory2Id")]
        public string? LegacyEquippedAccessory2Id { set => RestoreLegacyEquipment(EquipmentSlot.Accessory2, value); }

        private void RestoreLegacyEquipment(EquipmentSlot slot, string? itemId)
        {
            if (itemId == null || GetEquipped(slot) != null)
                return;

            SetEquippedId(slot, itemId);
        }

        /// <summary>
        /// 装備中の4枠（武器・防具・アクセサリー1・アクセサリー2）の最大HP加算の合計（→ Item.MaxHpBonus、03 §4.2.2）。
        /// 2026年9月・§0.37：旧 GetEquipmentBonus(効果種別) は個人CPの撤廃に伴い最大HP専用のこのメソッドへ置き換えた。
        /// §0.39：鑑定品のアフィックスによる加算（→ EquipmentItem.AffixHpBonus）も全枠ぶん足す。
        /// </summary>
        public int GetEquipmentHpBonus()
        {
            int total = 0;
            foreach (var slot in AllSlots)
            {
                var equipped = GetEquipped(slot);
                if (equipped == null) continue;
                total += (equipped.GetDefinition()?.MaxHpBonus ?? 0) + equipped.AffixHpBonus;
            }
            return total;
        }

        /// <summary>
        /// 装備中の4枠すべての、指定能力値への補正の合計（→ Item.StatBonuses、03 §4.2.2）。
        /// §0.39：鑑定品のアフィックスによる補正（→ EquipmentItem.AffixStatBonuses）も含む。
        /// GetEffectiveStat が加算するため、部隊指標・最大HP・UIの装備補正（水色）へそのまま連動する。
        /// カタログから引けない個体（旧データ）のカタログ分は0として扱う（個体に焼き付いたアフィックス分は数える）。
        /// </summary>
        public int GetEquipmentStatBonus(string statName)
        {
            int total = 0;
            foreach (var slot in AllSlots)
            {
                var equipped = GetEquipped(slot);
                if (equipped == null) continue;
                total += (equipped.GetDefinition()?.GetStatBonus(statName) ?? 0) + equipped.GetAffixStatBonus(statName);
            }
            return total;
        }
        public int CurrentHP { get; set; }
        public int Satisfaction { get; set; } = 70;
        public InjurySeverity Injury { get; set; } = InjurySeverity.None;

        // ---- 満足度・契約交渉（仕様書 03 §5.1・§5.2）。→ SatisfactionSystem が更新する。 ----

        /// <summary>直近出撃してからの連続週数（出撃した週に0へリセット）。出場機会ペナルティ判定に使う（§5.1）。</summary>
        public int WeeksSinceLastDeployment { get; set; } = 0;

        /// <summary>満足度20未満で立つ交渉警告フラグ（§5.2「昇給要求」「移籍検討」）。</summary>
        public bool NeedsNegotiation { get; set; } = false;

        /// <summary>交渉警告が立ってから経過した週数。2週を超えて未対応だと契約解除される。</summary>
        public int NegotiationWeeksElapsed { get; set; } = 0;

        /// <summary>負傷が治るまでの残り週数。0ならNoneに戻る（→ 03 §3.6 負傷回復処理）。</summary>
        public int InjuryWeeksRemaining { get; set; } = 0;

        public int WeeklyWage { get; set; }

        /// <summary>
        /// 複数週クエストに派遣中かどうか（仕様書 03 §4.0.1）。拘束期間が満了し
        /// QuestDispatchSystemが解決するまでtrueのまま。派遣中は他クエストへの
        /// 再編成・訓練場への配置ができない（IsAvailableに反映）。中断（呼び戻し）は実装しない。
        /// </summary>
        public bool IsDispatched { get; set; } = false;

        /// <summary>出撃可能かどうか（重傷・引退済み・派遣中なら不可）。疲労（Fatigue）は廃止済み（→ 03 §3.5改）。</summary>
        public bool IsAvailable => Injury != InjurySeverity.Severe && !IsRetired && !IsDispatched;
    }
}
