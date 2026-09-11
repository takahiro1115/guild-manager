using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	private LevelingSystem _levelingSystem = null!;

	private Label _weekLabel = null!;
	private Label _goldLabel = null!;
	private ItemList _questList = null!;
	private ItemList _adventurerList = null!;
	private RichTextLabel _adventurerDetailLabel = null!;
	private RichTextLabel _resultLog = null!;
	private Button _nextWeekButton = null!;

	/// <summary>ステータス詳細パネルに表示中の冒険者。週送り後もこの人物の表示を維持する。</summary>
	private Guid? _detailAdventurerId;

	public override void _Ready()
	{
		_weekLabel = GetNode<Label>("%WeekLabel");
		_goldLabel = GetNode<Label>("%GoldLabel");
		_questList = GetNode<ItemList>("%QuestList");
		_adventurerList = GetNode<ItemList>("%AdventurerList");
		_adventurerDetailLabel = GetNode<RichTextLabel>("%AdventurerDetailLabel");
		_resultLog = GetNode<RichTextLabel>("%ResultLog");
		_nextWeekButton = GetNode<Button>("%NextWeekButton");

		_adventurerList.SelectMode = ItemList.SelectModeEnum.Multi;
		_adventurerList.MultiSelected += OnAdventurerMultiSelected;
		_adventurerList.ItemClicked += OnAdventurerItemClicked;
		_nextWeekButton.Pressed += OnNextWeekPressed;

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
		_levelingSystem = new LevelingSystem(new SeededRng(7));

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
	/// ・重傷（出撃不可）の冒険者は選択させない（→ 03 §3.6：本来は毎週回復するが未実装のため応急処置）
	/// </summary>
	private void OnAdventurerMultiSelected(long index, bool selected)
	{
		if (!selected) return;

		var adventurer = _state.Adventurers[(int)index];
		if (!adventurer.IsAvailable)
		{
			_adventurerList.Deselect((int)index);
			AppendLog($"[color=gray]{adventurer.Name} は重傷のため出撃できません。[/color]");
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
	/// 「次週へ」の本体。
	/// パーティを選んでいれば遠征を解決し、選んでいなければ「休養」として扱う。
	/// どちらの場合も、週給引き落とし・負傷回復・週数の進行は必ず行う
	/// （→ 03 §3.6：全員負傷中でも時間を進めて回復を待てるようにするため）。
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

			var levelsBeforeQuest = party.Members.ToDictionary(m => m.Id, m => m.Level);

			var result = _questResolver.Resolve(party, quest);
			_economySystem.ApplyReward(_state, result.RewardGold);
			_levelingSystem.AwardExperience(party, quest, result); // → 03 §3.8：レベルアップ制度
			LogResult(thisWeek, quest, result);
			LogLevelUps(party, levelsBeforeQuest);
		}
		else
		{
			AppendLog($"[color=gray]第{thisWeek}週：今週は誰も出撃せず、静養に努めた。[/color]");
		}

		// 出撃の有無にかかわらず、時間は必ず進む。
		_economySystem.ApplyWeeklyWages(_state);
		_injuryRecoverySystem.ProcessWeeklyRecovery(_state);
		_agingSystem.ProcessWeeklyAging(_state); // → 03 §3：加齢・成長・衰微モデル
		_state.WeekNumber++;

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
	/// 今回の遠征でレベルが上がった参加者を週報ログに追記する（→ 03 §3.8）。
	/// ダウンして無報酬だった者は Level が変わらないので、ここには出てこない。
	/// </summary>
	private void LogLevelUps(Party party, Dictionary<Guid, int> levelsBeforeQuest)
	{
		foreach (var member in party.Members)
		{
			if (levelsBeforeQuest.TryGetValue(member.Id, out int before) && member.Level > before)
				AppendLog($"[color=yellow]★ {member.Name} はレベル{before}→{member.Level}に上がった！[/color]");
		}
	}

	private void AppendLog(string bbcodeText)
	{
		_resultLog.AppendText(bbcodeText + "\n");
	}

	private void RefreshAll()
	{
		_weekLabel.Text = $"週: {_state.WeekNumber}";
		_goldLabel.Text = $"所持金: {_state.Gold} G";

		_questList.Clear();
		foreach (var q in _state.AvailableQuests)
		{
			_questList.AddItem($"[{q.Rank}] {q.Name}（難易度{q.Difficulty} / 報酬{q.RewardGold}G）");
		}

		_adventurerList.Clear();
		foreach (var a in _state.Adventurers)
		{
			string status = a.Injury == InjurySeverity.Severe
				? $"【重傷・出撃不可・回復まで{a.InjuryWeeksRemaining}週】"
				: "";
			_adventurerList.AddItem($"{a.Name}（{a.JobClass}） HP{a.CurrentHP}/{a.MaxHP} {status}");
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
		sb.AppendLine($"[b]{a.Name}[/b]（{a.JobClass}） {a.Age}歳・{AgeBandLabel(a.AgeBand)}　Lv {a.Level} {LevelLabel(a)}");
		sb.AppendLine($"HP {a.CurrentHP}/{a.MaxHP}　疲労度 {a.Fatigue}/100　満足度 {a.Satisfaction}/100");
		sb.AppendLine(InjuryLabel(a));
		sb.AppendLine();
		sb.AppendLine("[b]能力値（実効値 / 潜在能力PA）[/b]");
		sb.AppendLine($"STR {a.STR} / {a.PA_STR}　　AGI {a.AGI} / {a.PA_AGI}　　END {a.END} / {a.PA_END}");
		sb.AppendLine($"MAG {a.MAG} / {a.PA_MAG}　　SCT {a.SCT} / {a.PA_SCT}　　LDR {a.LDR} / {a.PA_LDR}");
		sb.AppendLine($"総合PA: {a.TotalPA:F1}");
		sb.AppendLine();
		sb.AppendLine($"週給: {a.WeeklyWage} G");

		_adventurerDetailLabel.Clear();
		_adventurerDetailLabel.AppendText(sb.ToString());
	}

	private static string AgeBandLabel(AgeBand band) => band switch
	{
		AgeBand.GrowthPeriod => "成長期",
		AgeBand.PrimePeriod => "全盛期",
		AgeBand.MaturePeriod => "円熟期",
		AgeBand.LimitPeriod => "限界期",
		_ => band.ToString()
	};

	/// <summary>Lv表示の補足（EXPゲージ、または上限到達表示）。→ 03 §3.8</summary>
	private static string LevelLabel(Adventurer a)
	{
		int required = LevelingSystem.GetExperienceRequiredForNextLevel(a.Level);
		return required == 0 ? "（MAX）" : $"（EXP {a.Experience}/{required}）";
	}

	private static string InjuryLabel(Adventurer a) => a.Injury switch
	{
		InjurySeverity.None => "負傷: なし",
		InjurySeverity.Light => $"負傷: 軽傷（全治まで{a.InjuryWeeksRemaining}週）",
		InjurySeverity.Severe => $"[color=red]負傷: 重傷・出撃不可（全治まで{a.InjuryWeeksRemaining}週）[/color]",
		_ => a.Injury.ToString()
	};
}
