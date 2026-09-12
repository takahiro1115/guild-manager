using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;

/// <summary>
/// Phase 2 最小UI。「編成→週送り→結果→資金」の輪を一周させるためだけの画面。
/// 満足度・施設・格付け等はまだ扱わない（→ docs/06_タスクリスト.md Phase 3以降）。
///
/// 既知の割り切り（MVPの簡略化）：
///  - 週送りのたびにクエスト一覧・冒険者一覧の選択状態はリセットされる（毎週選び直す）。
///  - 重傷（出撃不可）の冒険者も選択自体は止めていない（ラベルで表示するのみ）。
/// </summary>
public partial class MainDashboard : Control
{
	private const int MaxPartySize = 4;

	private GameState _state = null!;
	private QuestResolver _questResolver = null!;
	private EconomySystem _economySystem = null!;
	private InjuryRecoverySystem _injuryRecoverySystem = null!;
	private AgingSystem _agingSystem = null!;
	private GrowthSystem _growthSystem = null!;
	private RestRecoverySystem _restRecoverySystem = null!;
	private TrainingSystem _trainingSystem = null!;
	private RecruitmentSystem _recruitmentSystem = null!;
	private SatisfactionSystem _satisfactionSystem = null!;
	private FacilitySystem _facilitySystem = null!;
	private QuestDispatchSystem _questDispatchSystem = null!;
	private GuildRankSystem _guildRankSystem = null!;
	private SecuritySystem _securitySystem = null!;
	private QuestBoardSystem _questBoardSystem = null!;
	private SubsidySystem _subsidySystem = null!;
	private DefeatSystem _defeatSystem = null!;

	private Label _weekLabel = null!;
	private Label _goldLabel = null!;
	private Label _rankLabel = null!;
	private Label _threatLabel = null!;
	private ItemList _questList = null!;
	private ItemList _adventurerList = null!;
	private RichTextLabel _adventurerDetailLabel = null!;
	private RichTextLabel _resultLog = null!;
	private Button _nextWeekButton = null!;
	private RecruitmentPopup _recruitmentPopup = null!;
	private Button _raiseWageButton = null!;
	private Button _payBonusButton = null!;
	private Button _placementButton = null!;
	private FacilityPopup _facilityPopup = null!;
	private Button _facilityButton = null!;

	/// <summary>ステータス詳細パネルに表示中の冒険者。週送り後もこの人物の表示を維持する。</summary>
	private Guid? _detailAdventurerId;

	public override void _Ready()
	{
		_weekLabel = GetNode<Label>("%WeekLabel");
		_goldLabel = GetNode<Label>("%GoldLabel");
		_rankLabel = GetNode<Label>("%RankLabel");
		_threatLabel = GetNode<Label>("%ThreatLabel");
		_questList = GetNode<ItemList>("%QuestList");
		_adventurerList = GetNode<ItemList>("%AdventurerList");
		_adventurerDetailLabel = GetNode<RichTextLabel>("%AdventurerDetailLabel");
		_resultLog = GetNode<RichTextLabel>("%ResultLog");
		_nextWeekButton = GetNode<Button>("%NextWeekButton");
		_recruitmentPopup = GetNode<RecruitmentPopup>("%RecruitmentPopup");
		_raiseWageButton = GetNode<Button>("%RaiseWageButton");
		_payBonusButton = GetNode<Button>("%PayBonusButton");
		_placementButton = GetNode<Button>("%PlacementButton");
		_facilityPopup = GetNode<FacilityPopup>("%FacilityPopup");
		_facilityButton = GetNode<Button>("%FacilityButton");

		_adventurerList.SelectMode = ItemList.SelectModeEnum.Multi;
		_adventurerList.MultiSelected += OnAdventurerMultiSelected;
		_adventurerList.ItemClicked += OnAdventurerItemClicked;
		_nextWeekButton.Pressed += OnNextWeekPressed;
		_recruitmentPopup.Closed += OnRecruitmentPopupClosed;
		_raiseWageButton.Pressed += OnRaiseWagePressed;
		_payBonusButton.Pressed += OnPayBonusPressed;
		_placementButton.Pressed += OnPlacementTogglePressed;
		_facilityButton.Pressed += OnFacilityButtonPressed;
		_facilityPopup.Closed += OnFacilityPopupClosed;

		_state = new GameState
		{
			Adventurers = SampleData.CreateStarterAdventurers(),
			AvailableQuests = SampleData.CreateStarterQuests(),
		};
		// シード固定の乱数。同じシードなら毎回同じ結果になる（デバッグしやすくするため）。
		// 戦闘用と加齢用で別インスタンス・別シードにし、互いの抽選回数が結果に影響しないようにする。
		_questResolver = new QuestResolver(new SeededRng(42));
		_economySystem = new EconomySystem();
		_injuryRecoverySystem = new InjuryRecoverySystem();
		_agingSystem = new AgingSystem(new SeededRng(99));
		_growthSystem = new GrowthSystem(new SeededRng(7));
		_restRecoverySystem = new RestRecoverySystem();
		_trainingSystem = new TrainingSystem();
		_recruitmentSystem = new RecruitmentSystem(new SeededRng(2024));
		_satisfactionSystem = new SatisfactionSystem();
		_facilitySystem = new FacilitySystem();
		_questDispatchSystem = new QuestDispatchSystem(_questResolver, _growthSystem, _economySystem, _satisfactionSystem);
		_guildRankSystem = new GuildRankSystem();
		_securitySystem = new SecuritySystem(new SeededRng(4649));
		_questBoardSystem = new QuestBoardSystem(new SeededRng(1192));
		_subsidySystem = new SubsidySystem();
		_defeatSystem = new DefeatSystem();

		RefreshAll();

		if (_questList.ItemCount > 0)
			_questList.Select(0);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Keycode: Key.Space })
		{
			OnNextWeekPressed();
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>
	/// 冒険者一覧の選択制御。
	/// ・5人目以降が選ばれそうになったら取り消す
	/// ・重傷・引退済み・派遣中の冒険者は選択させない（IsAvailableで判定）
	/// </summary>
	private void OnAdventurerMultiSelected(long index, bool selected)
	{
		if (!selected) return;

		var adventurer = _state.Adventurers[(int)index];
		if (!adventurer.IsAvailable)
		{
			_adventurerList.Deselect((int)index);
			string reason = adventurer.IsDispatched ? "派遣中" : adventurer.IsRetired ? "引退済み" : "重傷";
			AppendLog($"[color=gray]{adventurer.Name} は{reason}のため出撃できません。[/color]");
			return;
		}

		var selectedItems = _adventurerList.GetSelectedItems();
		if (selectedItems.Length > MaxPartySize)
		{
			_adventurerList.Deselect((int)index);
		}
	}

	/// <summary>
	/// 冒険者一覧のクリックでステータス詳細パネルを更新する（編成の選択/解除とは独立）。
	/// </summary>
	private void OnAdventurerItemClicked(long index, Vector2 atPosition, long mouseButtonIndex)
	{
		ShowAdventurerDetail(_state.Adventurers[(int)index]);
	}

	/// <summary>
	/// 新春採用試験ポップアップ（→ 03 §2.4・§9）が閉じた時のコールバック。
	/// 採用処理が確定したので「次週へ」を再度有効化し、ロスター・所持金の変化を反映する。
	/// </summary>
	private void OnRecruitmentPopupClosed()
	{
		_nextWeekButton.Disabled = false;
		RefreshAll();
	}

	/// <summary>
	/// 「次週へ」の本体。
	/// パーティを選んでいれば派遣（複数週クエストは満了週まで結果が出ない。→ 03 §4.0.1）し、
	/// 選んでいなければ「休養」として扱う。どちらの場合も、週給引き落とし・負傷回復・
	/// 週数の進行は必ず行う（→ 03 §3.6：全員負傷中でも時間を進めて回復を待てるようにするため）。
	/// </summary>
	private void OnNextWeekPressed()
	{
		int thisWeek = _state.WeekNumber;
		var selectedQuestIndices = _questList.GetSelectedItems();
		var selectedAdventurerIndices = _adventurerList.GetSelectedItems();
		bool wantsToDispatch = selectedAdventurerIndices.Length > 0;

		if (wantsToDispatch)
		{
			if (selectedQuestIndices.Length == 0)
			{
				AppendLog("[color=orange]クエストを選択してください。[/color]");
				return;
			}

			var quest = _state.AvailableQuests[selectedQuestIndices[0]];
			var party = new Party();
			foreach (int idx in selectedAdventurerIndices)
			{
				party.TryAdd(_state.Adventurers[idx]);
			}

			_questDispatchSystem.Dispatch(_state, party, quest);

			if (quest.DurationWeeks > 1)
			{
				AppendLog($"[color=cyan]第{thisWeek}週：{quest.Name}へ出発した" +
					$"（拘束{quest.DurationWeeks}週間、第{thisWeek + quest.DurationWeeks - 1}週に結果判明）。[/color]");
			}
		}
		else
		{
			AppendLog($"[color=gray]第{thisWeek}週：今週は誰も出撃せず、静養に努めた。[/color]");
		}

		// 派遣中（今週出発した分も含む）の冒険者は、HP自然回復・訓練場成長の対象から外す（→ 03 §4.0.1）。
		var dispatchedIds = _state.Adventurers.Where(a => a.IsDispatched).Select(a => a.Id).ToHashSet();

		// 満了した派遣（1週クエストは今週のうちに満了する）を解決し、結果を週報へ。
		var resolutions = _questDispatchSystem.ProcessWeeklyDispatches(_state);
		bool achievedRankAppropriateQuestThisWeek = false;
		foreach (var resolution in resolutions)
		{
			LogResult(thisWeek, resolution.Quest, resolution.Result);
			LogGrowthEvents(resolution.GrowthEvents);
			LogFallenAdventurers(thisWeek, resolution.Party, resolution.Result.FallenAdventurerIds); // → 03 §4.3.1

			// ギルド格付け（→ 03 §8.1）：名声は解決の都度加減算する。
			// 「現ランク相当のクエストを達成したか」は今週の全解決結果から判定し、
			// 週次決算（ProcessWeeklySettlement）へまとめて渡す。
			_guildRankSystem.ApplyQuestResult(_state, resolution.Result.QuestAchieved);
			if (resolution.Result.QuestAchieved &&
				resolution.Quest.Rank >= GuildRankBalance.ToQuestRankFloor(_state.GuildRank))
			{
				achievedRankAppropriateQuestThisWeek = true;
			}

			// 治安・脅威度（→ 03 §4.4）：対象は討伐クエストのみ（達成で減少・失敗で上昇）。
			int threatDelta = _securitySystem.ApplyQuestResolution(_state, resolution.Quest, resolution.Result.QuestAchieved);
			LogThreatChange(resolution.Quest, threatDelta);
		}

		// 受注可能クエスト一覧の週次管理（→ 03 §4.0・§4.4）：期限切れ（放置）の除去と補充。
		// 放置された討伐クエストは脅威度上昇の対象になる。
		var expiredQuests = _questBoardSystem.ProcessWeeklyBoard(_state);
		foreach (var expiredQuest in expiredQuests)
		{
			int abandonedThreatDelta = _securitySystem.ApplyAbandonedQuest(_state, expiredQuest);
			if (abandonedThreatDelta != 0)
				AppendLog($"[color=orange]「{expiredQuest.Name}」が期限切れで放置された。脅威度+{abandonedThreatDelta}%。[/color]");
		}

		// 出撃の有無にかかわらず、時間は必ず進む。
		_economySystem.ApplyWeeklyWages(_state);

		// 月次助成金（4週に1回。→ 03 §8.1・§4.4：脅威度75%超で50%カット）。
		var subsidyAmount = _subsidySystem.ProcessWeeklySubsidy(_state);
		if (subsidyAmount.HasValue)
			AppendLog($"[color=lime]月次助成金 {subsidyAmount.Value}G を受け取った{(_state.ThreatLevel > SecurityBalance.SubsidyCutThreatThreshold ? "（脅威度75%超のため50%カット済み）" : "")}。[/color]");
		_trainingSystem.ProcessWeeklyTraining(_state, dispatchedIds); // → 03 §3.1〜3.4・§3.5改：訓練場の週次費用・HP微減
		_injuryRecoverySystem.ProcessWeeklyRecovery(_state);
		_restRecoverySystem.ProcessWeeklyRest(_state, dispatchedIds); // → 03 §3.5改：静養・HP自然回復（訓練場配置中は対象外）
		var trainingGrowth = _growthSystem.ProcessTrainingGrowth(_state, dispatchedIds); // → 03 §3.1〜3.4：成長トリガー経路2（訓練場配置）
		LogGrowthEvents(trainingGrowth);
		_satisfactionSystem.ProcessWeeklySatisfaction(_state, dispatchedIds); // → 03 §5.1：満足度変動
		var terminated = _satisfactionSystem.ProcessWeeklyNegotiation(_state); // → 03 §5.2：契約交渉・退団
		LogNegotiationStatus(terminated);
		_agingSystem.ProcessWeeklyAging(_state); // → 03 §3：加齢・衰微モデル

		var completedFacility = _facilitySystem.ProcessWeeklyConstruction(_state); // → 03 §6.1：施設Lv投資
		if (completedFacility != null)
			AppendLog($"[color=lime][b]🏗 {FacilityLabel(completedFacility.Type)}がLv{completedFacility.CurrentLevel}に完成した！[/b][/color]");

		// ギルド格付け（→ 03 §8.1・§8.1.1）：名声自然減衰の判定と昇格・降格判定は週次決算で1回だけ行う。
		var rankChange = _guildRankSystem.ProcessWeeklySettlement(_state, achievedRankAppropriateQuestThisWeek);
		if (rankChange != null)
		{
			string message = rankChange.IsPromotion
				? $"[color=gold][b]🏅 ギルド格付けが{rankChange.Current}ランクに昇格しました！[/b][/color]"
				: $"[color=orange][b]⚠ ギルド格付けが{rankChange.Current}ランクに降格しました。[/b][/color]";
			AppendLog(message);
		}

		// 敗北条件判定（→ 03 §8.3）：週次決算の最後に1回だけ行う。
		// 破産（所持金マイナス4週連続、猶予あり）／治安崩壊（脅威度100%到達、猶予なし即時敗北）。
		var newDefeatReason = _defeatSystem.ProcessWeeklySettlement(_state);
		if (newDefeatReason != null)
		{
			string reasonLabel = newDefeatReason == DefeatReason.Bankruptcy ? "破産" : "治安崩壊";
			AppendLog($"[color=red][font_size=24][b]■■■ ゲームオーバー：{reasonLabel} ■■■[/b][/font_size][/color]");
			AppendLog(newDefeatReason == DefeatReason.Bankruptcy
				? "[color=red]所持金マイナスが4週連続で解消されませんでした。[/color]"
				: "[color=red]脅威度が100%に到達し、街の治安が崩壊しました。[/color]");
			_nextWeekButton.Disabled = true; // ゲームオーバー：これ以上週を進められない
		}

		_state.WeekNumber++;

		RefreshAll();

		// 新春採用試験（2年目以降の新年第1週のみ）。ポップアップが閉じるまで次週へ進めさせない（→ 03 §9）。
		// ゲームオーバー後は採用試験も発生させない。
		if (_state.DefeatReason == null && _recruitmentSystem.IsRecruitmentWeek(_state.WeekNumber))
		{
			_nextWeekButton.Disabled = true;
			_recruitmentPopup.Open(_state, _recruitmentSystem);
		}
	}

	/// <summary>「施設投資」ボタン。施設投資ポップアップを開く（採用試験とは異なり、いつでも自由に開閉できる）。</summary>
	private void OnFacilityButtonPressed()
	{
		_facilityPopup.Open(_state, _facilitySystem);
	}

	/// <summary>施設投資ポップアップが閉じた時のコールバック。着工・所持金の変化を反映する。</summary>
	private void OnFacilityPopupClosed()
	{
		RefreshAll();
	}

	private void LogResult(int weekNumber, Quest quest, WeekResolutionResult result)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"[b]第{weekNumber}週：{quest.Name}[/b]");
		sb.AppendLine($"遭遇: {result.Encounter} / 結果: {result.Outcome} (Ratio={result.Ratio:F2})");
		sb.AppendLine(result.QuestAchieved
			? $"達成！報酬 {result.RewardGold} G"
			: "任務失敗。報酬なし。");

		foreach (var kv in result.HpLostByAdventurer)
		{
			var adv = _state.Adventurers.FirstOrDefault(a => a.Id == kv.Key);
			if (adv != null)
				sb.AppendLine($" - {adv.Name}: HP -{kv.Value}（残りHP {adv.CurrentHP}/{adv.MaxHP}）");
		}

		AppendLog(sb.ToString());
	}

	/// <summary>
	/// 今週の成長トリガー（→ 03 §3.1〜3.4）で実際にステータスが伸びた者を週報ログに報告する。
	/// 見逃さないよう、色（黄）＋太字＋大きめフォントサイズで目立たせる（→ ユーザー要望）。
	/// </summary>
	private void LogGrowthEvents(List<GrowthEvent> events)
	{
		foreach (var e in events)
		{
			AppendLog(
				$"[color=yellow][font_size=20][b]▲ {e.Adventurer.Name} の {e.Stat} が上昇！ {e.Before} → {e.After}[/b][/font_size][/color]");
		}
	}

	/// <summary>
	/// 討伐クエストの解決に伴う脅威度の増減を週報ログに報告する（→ 03 §4.4）。
	/// 討伐クエスト以外（脅威度に影響しない）やクランプで実質変化が無かった場合は何も表示しない。
	/// </summary>
	private void LogThreatChange(Quest quest, int threatDelta)
	{
		if (threatDelta == 0) return;

		string reason = threatDelta < 0 ? "達成" : "失敗";
		string sign = threatDelta > 0 ? "+" : "";
		AppendLog($"[color=orange]討伐クエスト「{quest.Name}」{reason}により脅威度{sign}{threatDelta}%。[/color]");
	}

	/// <summary>
	/// 戦死した冒険者を週報ログに報告する（→ 03 §4.3・§4.3.1）。
	/// 見逃さないよう赤・太字で目立たせる。氏名はPartyから引く
	/// （戦死者はGameState.Adventurersから既に除外済みのため）。
	/// </summary>
	private void LogFallenAdventurers(int weekNumber, Party party, HashSet<Guid> fallenIds)
	{
		foreach (var id in fallenIds)
		{
			var fallen = party.Members.FirstOrDefault(m => m.Id == id);
			if (fallen != null)
				AppendLog($"[color=red][b]† {fallen.Name} が戦死しました（第{weekNumber}週）。[/b][/color]");
		}
	}

	/// <summary>
	/// 契約交渉の状況を週報ログに報告する（→ 03 §5.2）。
	/// 警告中の全員に残り猶予週数を毎週リマインドし、契約解除された者を報告する。
	/// </summary>
	private void LogNegotiationStatus(List<Adventurer> terminated)
	{
		foreach (var a in _state.Adventurers)
		{
			if (!a.NeedsNegotiation) continue;
			int remaining = Math.Max(0, SatisfactionBalance.NegotiationGraceWeeks - a.NegotiationWeeksElapsed);
			AppendLog($"[color=orange][b]⚠ {a.Name} が契約に不満（満足度{a.Satisfaction}）。" +
				$"あと{remaining}週以内に昇給かボーナスで対応しないと退団する。[/b][/color]");
		}

		foreach (var a in terminated)
		{
			AppendLog($"[color=red][b]✕ {a.Name} が契約を解除し、他都市へ移籍した。[/b][/color]");
		}
	}

	/// <summary>
	/// 「昇給する」ボタン（→ 03 §5.2）。表示中の冒険者の週給を1.5倍に引き上げる
	/// （倍率選択UIは未実装のため、仕様の下限=最小限の昇給で固定。→ 03 §5.2）。
	/// </summary>
	private void OnRaiseWagePressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		_satisfactionSystem.RaiseWage(target, 1.5);
		AppendLog($"[color=lime]{target.Name} の週給を {target.WeeklyWage}G に引き上げた。[/color]");
		RefreshAll();
	}

	/// <summary>「ボーナスを払う」ボタン（→ 03 §5.2）。表示中の冒険者に一時金を支給する。</summary>
	private void OnPayBonusPressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		int bonus = target.WeeklyWage * SatisfactionBalance.BonusWeeksEquivalent;
		_satisfactionSystem.PayBonus(_state, target);
		AppendLog($"[color=lime]{target.Name} にボーナス {bonus}G を支給した。[/color]");
		RefreshAll();
	}

	/// <summary>
	/// 「前衛⇔後衛を切り替える」ボタン（→ 03 §4.2）。配置は職業で固定されないため
	/// 全職業で常に切り替え可能（TrySetPlacementは常に成功する）。
	/// </summary>
	private void OnPlacementTogglePressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		var newPlacement = target.Placement == Placement.Front ? Placement.Back : Placement.Front;
		if (target.TrySetPlacement(newPlacement))
		{
			AppendLog($"[color=lime]{target.Name} の配置を{PlacementLabel(newPlacement)}に変更した。[/color]");
			RefreshAll();
		}
	}

	private Adventurer CurrentDetailAdventurer() =>
		_detailAdventurerId.HasValue
			? _state.Adventurers.FirstOrDefault(a => a.Id == _detailAdventurerId.Value)
			: null;

	private void AppendLog(string bbcodeText)
	{
		_resultLog.AppendText(bbcodeText + "\n");
	}

	private void RefreshAll()
	{
		_weekLabel.Text = $"週: {_state.WeekNumber}";
		_goldLabel.Text = $"所持金: {_state.Gold} G";
		_rankLabel.Text = $"ギルド格付け: {_state.GuildRank}ランク（名声 {_state.Reputation}）";
		_threatLabel.Text = $"脅威度: {_state.ThreatLevel}%";

		_questList.Clear();
		foreach (var q in _state.AvailableQuests)
		{
			_questList.AddItem($"[{q.Rank}] {q.Name}（{QuestTypeLabel(q.QuestType)} / 難易度{q.Difficulty} / " +
				$"規模{ScaleLabel(q.Scale)}・{q.DurationWeeks}週 / 報酬{q.RewardGold}G / 期限あと{q.DeadlineWeeks}週）");
		}

		_adventurerList.Clear();
		foreach (var a in _state.Adventurers)
		{
			string status = a.Injury == InjurySeverity.Severe
				? $"【重傷・出撃不可・回復まで{a.InjuryWeeksRemaining}週】"
				: a.IsDispatched
					? $"【派遣中・残り{GetDispatchWeeksRemaining(a)}週】"
					: "";
			_adventurerList.AddItem($"{a.Name}（{a.JobClass}・{PlacementLabel(a.Placement)}） HP{a.CurrentHP}/{a.MaxHP}　総合PA{a.TotalPA:F1}　{a.Age}歳 {status}");
		}

		RefreshAdventurerDetail();
	}

	/// <summary>
	/// ステータス詳細パネルを、直近にクリックされた冒険者（居なければ先頭）の最新の値で再描画する。
	/// 週送り直後もパネルの表示対象を維持するため RefreshAll から毎回呼び出す。
	/// </summary>
	private void RefreshAdventurerDetail()
	{
		var target = _detailAdventurerId.HasValue
			? _state.Adventurers.FirstOrDefault(a => a.Id == _detailAdventurerId.Value)
			: null;
		target ??= _state.Adventurers.FirstOrDefault();

		if (target != null)
			ShowAdventurerDetail(target);
	}

	/// <summary>冒険者1名分のステータス詳細（仕様書 03 §2）を詳細パネルに表示する。</summary>
	private void ShowAdventurerDetail(Adventurer a)
	{
		_detailAdventurerId = a.Id;

		var sb = new StringBuilder();
		sb.AppendLine($"[b]{a.Name}[/b]（{a.JobClass}） {a.Age}歳・{AgeBandLabel(a.AgeBand)}");
		sb.AppendLine($"配置: {PlacementLabel(a.Placement)}");
		sb.AppendLine($"HP {a.CurrentHP}/{a.MaxHP}　満足度 {a.Satisfaction}/100");
		sb.AppendLine(InjuryLabel(a));
		if (a.TraitIds.Count > 0)
			sb.AppendLine($"特性: {string.Join("、", a.TraitIds.Select(TraitLabel))}");
		if (a.NeedsNegotiation)
		{
			int remaining = Math.Max(0, SatisfactionBalance.NegotiationGraceWeeks - a.NegotiationWeeksElapsed);
			sb.AppendLine($"[color=orange]⚠ 契約交渉中（あと{remaining}週で対応しないと退団）[/color]");
		}
		sb.AppendLine();
		sb.AppendLine("[b]能力値（実効値 / 潜在能力PA）[/b]");
		sb.AppendLine($"STR {a.STR} / {a.PA_STR}　　VIT {a.VIT} / {a.PA_VIT}　　AGI {a.AGI} / {a.PA_AGI}");
		sb.AppendLine($"DEX {a.DEX} / {a.PA_DEX}　　MND {a.MND} / {a.PA_MND}　　INT {a.INT} / {a.PA_INT}");
		sb.AppendLine($"LDR {a.LDR} / {a.PA_LDR}");
		sb.AppendLine($"総合PA: {a.TotalPA:F1}");
		sb.AppendLine();
		sb.AppendLine($"週給: {a.WeeklyWage} G");

		_adventurerDetailLabel.Clear();
		_adventurerDetailLabel.AppendText(sb.ToString());

		// 配置は職業で固定されないため、常に切り替え可能（→ 03 §4.2）。
		_placementButton.Text = a.Placement == Placement.Front ? "後衛に変更する" : "前衛に変更する";
	}

	private static string PlacementLabel(Placement placement) => placement switch
	{
		Placement.Front => "前衛",
		Placement.Back => "後衛",
		_ => placement.ToString()
	};

	private static string ScaleLabel(QuestScale scale) => scale switch
	{
		QuestScale.Small => "小",
		QuestScale.Medium => "中",
		QuestScale.Large => "大",
		_ => scale.ToString()
	};

	private static string QuestTypeLabel(QuestType type) => type switch
	{
		QuestType.Subjugation => "討伐",
		QuestType.Exploration => "調査・探索",
		QuestType.Escort => "護衛",
		_ => type.ToString()
	};

	private static string FacilityLabel(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "宿舎",
		FacilityType.Infirmary => "医務室",
		FacilityType.TrainingGround => "訓練場・道場",
		FacilityType.WarRoom => "作戦資料室",
		FacilityType.Tavern => "ギルド酒場",
		_ => type.ToString()
	};

	/// <summary>指定した冒険者が派遣中の案件の残り週数を返す（派遣中でなければ0）。</summary>
	private int GetDispatchWeeksRemaining(Adventurer a)
	{
		foreach (var dispatch in _state.ActiveDispatches)
			if (dispatch.Party.Members.Contains(a))
				return dispatch.WeeksRemaining;
		return 0;
	}

	private static string AgeBandLabel(AgeBand band) => band switch
	{
		AgeBand.GrowthPeriod => "成長期",
		AgeBand.PrimePeriod => "全盛期",
		AgeBand.MaturePeriod => "円熟期",
		AgeBand.LimitPeriod => "限界期",
		_ => band.ToString()
	};

	private static string InjuryLabel(Adventurer a) => a.Injury switch
	{
		InjurySeverity.None => "負傷: なし",
		InjurySeverity.Light => $"負傷: 軽傷（全治まで{a.InjuryWeeksRemaining}週）",
		InjurySeverity.Severe => $"[color=red]負傷: 重傷・出撃不可（全治まで{a.InjuryWeeksRemaining}週）[/color]",
		_ => a.Injury.ToString()
	};

	/// <summary>特性IDの表示名を返す（→ 03 §5.3）。カタログに無いIDはそのまま表示する（防御的フォールバック）。</summary>
	private static string TraitLabel(string traitId) => TraitCatalog.FindById(traitId)?.DisplayName ?? traitId;
}
