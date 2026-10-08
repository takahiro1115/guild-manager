#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 「🏆 大会」の画面（2026年10月・§0.82、→ docs/検討中_大会と育成の栄光.md §3）。大迷宮の画面とは分ける。
///
/// 左ペイン＝今月の大会（月に0〜3つ）。大会ごとに、出場者の選択（候補ごとに部門の強さ・今のHP・勝てそうかの目安）と、
/// 大会の月の過ごし方（休養／追い込み）を決める。1人1か月1大会。決められるのは月のはじめだけ（訓練の割り振りと同じ）。
/// 年のはじめに訓練所が1つも開いていなければ、副官の救済の提案（3つから1つを選んで開く）もここに出す。
/// 右ペイン＝1年の暦（定例のG1・招待・今年の結果）と、ギルドの戦績・歴代のG1の優勝者。
/// 式や資格の判定はすべて Core（TournamentSystem・FacilityUnlockSystem）から取る。シーンは使わずコードで組む。
/// </summary>
public partial class TournamentPanel : HBoxContainer
{
	private GameState _state = null!;

	private RichTextLabel _statusLabel = null!;
	private VBoxContainer _rescueBox = null!;
	private VBoxContainer _eventList = null!;
	private RichTextLabel _calendarLabel = null!;
	private RichTextLabel _historyLabel = null!;
	private VBoxContainer _hallBox = null!;

	/// <summary>お知らせへの追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>出場の変更でゲーム状態が変わったことを通知する（MainDashboard が全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	/// <summary>戦績の頁を開く依頼（殿堂の名前を押したとき。大会と育成の栄光 段2）。</summary>
	public event Action<Adventurer> HonorRecordRequested = delegate { };

	public TournamentPanel()
	{
		Name = "TournamentTab";
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		AddThemeConstantOverride("separation", 8);
		BuildLayout();
	}

	// ==================== 画面の組み立て ====================

	private void BuildLayout()
	{
		// 左：今月の大会
		var left = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3f };
		var leftBox = new VBoxContainer();
		leftBox.AddThemeConstantOverride("separation", 8);
		left.AddChild(leftBox);

		leftBox.AddChild(new Label { Text = "🏆 今月の大会：出場者と、大会の月の過ごし方を決める（月のはじめだけ）" });
		_statusLabel = NewText();
		leftBox.AddChild(_statusLabel);
		_rescueBox = new VBoxContainer { Visible = false };
		_rescueBox.AddThemeConstantOverride("separation", 6);
		leftBox.AddChild(_rescueBox);

		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_eventList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_eventList.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_eventList);
		leftBox.AddChild(scroll);
		AddChild(left);

		// 右：1年の暦・戦績
		var right = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2f };
		var rightScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var rightBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		rightBox.AddThemeConstantOverride("separation", 12);
		_calendarLabel = NewText();
		_historyLabel = NewText();
		rightBox.AddChild(_calendarLabel);
		rightBox.AddChild(_historyLabel);
		// 殿堂（大会と育成の栄光 段2）：歴代を顔・二つ名・主な勝ち鞍で並べ、押すと戦績の頁を開く
		_hallBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_hallBox.AddThemeConstantOverride("separation", 6);
		rightBox.AddChild(_hallBox);
		rightScroll.AddChild(rightBox);
		right.AddChild(rightScroll);
		AddChild(right);

		UiStyles.ApplyPanelArt(left, innerMargin: 0);
		UiStyles.ApplyPanelArt(right, innerMargin: 0);
	}

	private static RichTextLabel NewText() =>
		new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };

	// ==================== 再描画 ====================

	public void Refresh(GameState state)
	{
		_state = state;
		TournamentSystem.EnsureSchedule(_state); // 新規ゲームの最初の月など、まだ暦が無ければ作る（同じ年なら何もしない）
		RefreshStatus();
		RefreshRescue();
		RefreshEvents();
		_calendarLabel.Text = BuildCalendar();
		_historyLabel.Text = BuildHistory();
		RefreshHall();
	}

	/// <summary>殿堂（大会と育成の栄光 段2）：殿堂入りした者を顔・二つ名・主な勝ち鞍で並べる。押すと戦績の頁。</summary>
	private void RefreshHall()
	{
		foreach (var child in _hallBox.GetChildren())
			child.QueueFree();
		_hallBox.AddChild(new Label { Text = "🏛 殿堂" });
		var hall = HonorSystem.HallOfFame(_state);
		if (hall.Count == 0)
		{
			var empty = NewText();
			empty.Text = $"[color=gray]まだ誰もいない。G1を{HonorBalance.HallOfFameG1Wins}勝・王都最強決定戦の優勝・伝説の称号のどれかを持って引退すると殿堂入りする。[/color]";
			_hallBox.AddChild(empty);
			return;
		}
		foreach (var a in hall)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			row.AddChild(new TextureRect
			{
				Texture = AdventurerPanel.LoadPortraitTexture(a.PortraitId),
				CustomMinimumSize = new Vector2(48, 60),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			});
			string title = HonorSystem.DisplayTitle(_state, a);
			var wins = a.TournamentRecords.Where(r => r.Placing == 1 && r.Grade == TournamentGrade.G1).Select(r => $"{r.Year}年目 {r.Name}").ToList();
			var button = new Button
			{
				Text = $"{a.HallOfFameYear}年目　{a.Name}" + (title.Length > 0 ? $"「{title}」" : "") + $"　{a.HallOfFameReason}",
				TooltipText = wins.Count > 0 ? "G1の勝ち鞍：" + string.Join("、", wins) : a.HallOfFameReason,
				Alignment = HorizontalAlignment.Left,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
			};
			var target = a;
			button.Pressed += () => HonorRecordRequested(target);
			row.AddChild(button);
			_hallBox.AddChild(row);
		}
	}

	private void RefreshStatus()
	{
		bool canChange = TournamentSystem.CanChangeEntries(_state);
		_statusLabel.Text =
			(canChange
				? "[color=lime]今は月のはじめ：出場者と過ごし方を変えられる。[/color]"
				: "[color=orange]出場者と過ごし方を変えられるのは月のはじめだけ（次の月のはじめに）。[/color]") +
			$"\n[color=gray]出場者はその月、迷宮に出ず訓練もしない（部隊は残りのメンバーで方針どおりに動く）。1試合ごとに最大HPの{TournamentBalance.HpCostPerMatch:P0}を使い、" +
			$"HPが減るほど弱くなる（強さ×{TournamentBalance.HpFactorBase:0.0#}〜1）。" +
			$"過ごし方＝休養：HPの回復×{TournamentBalance.RestRecoveryMultiplier:0.#}／追い込み：部門の能力の成長の抽選・毎週HP−{TournamentBalance.PushHpCost}。[/color]";
	}

	/// <summary>副官の救済の提案（2年目以降の年のはじめに訓練所が1つも開いていない、→ FacilityUnlockSystem.CheckRescue）。</summary>
	private void RefreshRescue()
	{
		foreach (var child in _rescueBox.GetChildren())
			child.QueueFree();
		_rescueBox.Visible = _state.PendingTrainingFacilityChoice;
		if (!_state.PendingTrainingFacilityChoice)
			return;

		var text = NewText();
		text.Text = "[color=gold][b]副官の提案[/b][/color]：「大会で名を上げるには、まず地力です。得意な子に合わせて、訓練所を1つ建てましょう」（1つ選ぶと、その訓練所を建てられるようになる）";
		_rescueBox.AddChild(text);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		foreach (var type in FacilityUnlockSystem.TrainingFacilities)
		{
			var button = new Button { Text = $"🏗 {FacilityUnlockSystem.FacilityName(type)}（{TournamentSystem.DisciplineLabel(FacilityUnlockSystem.DisciplineOf(type))}）を開く", CustomMinimumSize = new Vector2(0, 34) };
			var captured = type;
			button.Pressed += () =>
			{
				var notice = FacilityUnlockSystem.ChooseRescueFacility(_state, captured);
				if (notice == null) return;
				LogRequested.Invoke($"[color=gold][b]🏗 {FacilityUnlockSystem.FacilityName(notice.Facility)}を建てられるようになった。[/b][/color] {notice.Line}");
				StateChanged.Invoke();
			};
			row.AddChild(button);
		}
		_rescueBox.AddChild(row);
	}

	private void RefreshEvents()
	{
		foreach (var child in _eventList.GetChildren())
			child.QueueFree();

		var events = TournamentSystem.EventsThisMonth(_state);
		if (events.Count == 0)
		{
			var none = NewText();
			none.Text = "[color=gray]今月は大会が無い。右の暦で、次の大会と定例のG1を確かめておこう。[/color]";
			_eventList.AddChild(none);
			return;
		}
		foreach (var ev in events)
			_eventList.AddChild(BuildEventCard(ev));
	}

	// ==================== 大会のカード ====================

	private Control BuildEventCard(TournamentEvent ev)
	{
		var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 6);
		card.AddChild(box);
		UiStyles.ApplyPanelArt(card);

		var header = NewText();
		header.Text = EventHeader(ev);
		box.AddChild(header);

		if (ev.Result != null)
		{
			var result = NewText();
			result.Text = ResultText(ev);
			box.AddChild(result);
			return card;
		}

		foreach (var entry in TournamentSystem.EntriesOf(_state, ev))
			box.AddChild(BuildEntryRow(ev, entry));

		if (TournamentSystem.CanChangeEntries(_state))
			box.AddChild(ev.Discipline == TournamentDiscipline.Party ? BuildPartyPicker(ev) : BuildAdventurerPicker(ev));
		return card;
	}

	private string EventHeader(TournamentEvent ev)
	{
		var def = TournamentBalance.Find(ev.DefinitionId);
		var sb = new StringBuilder();
		sb.Append($"[b][color={GradeColor(ev.Grade)}]{ev.Name}[/color][/b]　{TournamentSystem.GradeLabel(ev.Grade)}・{TournamentSystem.DisciplineLabel(ev.Discipline)}・第{ev.Week}週");
		if (def != null)
			sb.Append($"\n[color=gray]賞金：優勝 {def.Prize1}G／準優勝 {def.Prize2}G／ベスト4 {def.Prize4}G　出場：{Qualification(ev)}[/color]");
		if (ev.SpecialReward == "unique")
			sb.Append("\n[color=gold]優勝のご褒美：固有の武具[/color]");
		else if (ev.SpecialReward == "halfcost")
			sb.Append("\n[color=gold]優勝のご褒美：次の改築費が半額[/color]");
		if (ev.InvitedAdventurerId is Guid invited)
			sb.Append($"\n[color=khaki]招待された者：{_state.Adventurers.FirstOrDefault(a => a.Id == invited)?.Name ?? "（もういない）"}[/color]");
		string? reward = RewardHint(ev);
		if (reward != null)
			sb.Append($"\n[color=lightgreen]{reward}[/color]");
		return sb.ToString();
	}

	/// <summary>出場の資格の説明（判定は Core の QualificationBlock）。</summary>
	private static string Qualification(TournamentEvent ev) => ev.Kind switch
	{
		TournamentKind.Rookie => "その年に加わった者",
		TournamentKind.Local => "誰でも",
		TournamentKind.Royal => "同じ部門のG3でベスト4以上（前の年から）",
		TournamentKind.Classic when ev.Discipline == TournamentDiscipline.Party => $"部隊（どこかで{TournamentBalance.PartyEntryMinFloor}Fのボスを倒していること）",
		TournamentKind.Classic => "同じ部門のG2でベスト4以上（前の年から）",
		TournamentKind.Final => "その年にG1で優勝、またはG2で2勝した者",
		_ => ev.InvitedAdventurerId != null ? "招待された者だけ" : "誰でも",
	};

	/// <summary>この大会の成績で開くかもしれない施設の次の条件（訓練所・作戦資料室・冒険者支援室）。</summary>
	private string? RewardHint(TournamentEvent ev)
	{
		IEnumerable<FacilityType> types = ev.Discipline switch
		{
			TournamentDiscipline.Party => new[] { FacilityType.WarRoom },
			TournamentDiscipline.Sword or TournamentDiscipline.Magic or TournamentDiscipline.Skill => new[] { FacilityUnlockSystem.FacilityOf(ev.Discipline) },
			_ => FacilityUnlockSystem.TrainingFacilities,
		};
		if (ev.Kind == TournamentKind.Rookie)
			types = types.Append(FacilityType.RecruitmentOffice);
		var hints = types.Select(t => (Type: t, Next: FacilityUnlockSystem.DescribeNext(_state, t)))
			.Where(x => x.Next != null).Select(x => $"{FacilityUnlockSystem.FacilityName(x.Type)}の{x.Next}").ToList();
		return hints.Count == 0 ? null : "🏗 施設が開く次の条件：" + string.Join("／", hints);
	}

	private Control BuildEntryRow(TournamentEvent ev, TournamentEntry entry)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		var label = NewText();
		label.Text = EntryText(ev, entry);
		row.AddChild(label);

		bool canChange = TournamentSystem.CanChangeEntries(_state);
		var prep = new OptionButton { CustomMinimumSize = new Vector2(150, 32), Disabled = !canChange };
		prep.AddItem("🛌 休養", (int)TournamentPrep.Rest);
		prep.AddItem("🔥 追い込み", (int)TournamentPrep.Push);
		prep.Selected = entry.Prep == TournamentPrep.Push ? 1 : 0;
		prep.TooltipText = $"休養：大会の月はHPの回復×{TournamentBalance.RestRecoveryMultiplier:0.#}（万全で臨む）\n" +
			$"追い込み：部門の能力に成長の抽選があるが、毎週HP−{TournamentBalance.PushHpCost}（疲れたまま臨む）";
		prep.ItemSelected += index =>
		{
			if (TournamentSystem.SetPrep(_state, entry, index == 1 ? TournamentPrep.Push : TournamentPrep.Rest))
				StateChanged.Invoke();
		};
		row.AddChild(prep);

		var withdraw = new Button { Text = "取り消す", CustomMinimumSize = new Vector2(100, 32), Disabled = !canChange };
		withdraw.Pressed += () =>
		{
			if (TournamentSystem.Withdraw(_state, entry))
				StateChanged.Invoke();
		};
		row.AddChild(withdraw);
		return row;
	}

	private string EntryText(TournamentEvent ev, TournamentEntry entry)
	{
		if (entry.AdventurerId is Guid id && _state.Adventurers.FirstOrDefault(a => a.Id == id) is Adventurer a)
			return $"[b]{a.Name}[/b]　{CandidateSummary(ev, a)}";
		if (entry.PartyId is Guid pid && _state.SavedParties.FirstOrDefault(p => p.Id == pid) is SavedParty party)
			return $"[b]{party.Name}[/b]　{PartySummary(ev, party)}";
		return "[color=gray]（もういない）[/color]";
	}

	/// <summary>候補の目安：「剣 132・HP 80%・対抗」。</summary>
	private static string CandidateSummary(TournamentEvent ev, Adventurer a)
	{
		var d = TournamentSystem.DisciplineFor(ev, a);
		double strength = TournamentSystem.MatchStrength(a, d);
		double chance = TournamentSystem.EstimateWinChance(ev, d, strength);
		int hp = a.MaxHP > 0 ? a.CurrentHP * 100 / a.MaxHP : 0;
		return $"{TournamentSystem.DisciplineLabel(d)} {strength:0}・HP {hp}%・{ChanceText(chance)}";
	}

	private string PartySummary(TournamentEvent ev, SavedParty party)
	{
		var members = TournamentSystem.PartyMembers(_state, party);
		double strength = TournamentSystem.PartyStrength(_state, members);
		double chance = TournamentSystem.EstimateWinChance(ev, TournamentDiscipline.Party, strength);
		return $"{members.Count}人・強さ {strength:0}・{ChanceText(chance)}";
	}

	private static string ChanceText(double chance)
	{
		string label = TournamentSystem.WinChanceLabel(chance);
		string color = label switch { "本命" => "lime", "対抗" => "khaki", "穴" => "orange", _ => "gray" };
		return $"[color={color}]{label}[/color]";
	}

	private Control BuildAdventurerPicker(TournamentEvent ev)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
		picker.AddItem("＋ 出場させる冒険者を選ぶ…");
		var candidates = _state.Adventurers
			.Where(a => TournamentSystem.EntryBlockReason(_state, ev, a) == null)
			.OrderByDescending(a => TournamentSystem.MatchStrength(a, TournamentSystem.DisciplineFor(ev, a)))
			.ToList();
		foreach (var a in candidates)
			picker.AddItem($"{a.Name}　{StripBbcode(CandidateSummary(ev, a))}");
		picker.Disabled = candidates.Count == 0;
		if (candidates.Count == 0)
			picker.SetItemText(0, $"出られる冒険者がいない（{BlockSummary(ev)}）");
		picker.ItemSelected += index =>
		{
			if (index <= 0) return;
			if (TournamentSystem.TryEnter(_state, ev, candidates[(int)index - 1], TournamentPrep.Rest))
				StateChanged.Invoke();
		};
		row.AddChild(picker);
		return row;
	}

	private Control BuildPartyPicker(TournamentEvent ev)
	{
		var row = new HBoxContainer();
		var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
		picker.AddItem("＋ 出場させる部隊を選ぶ…");
		var parties = _state.SavedParties.Where(p => TournamentSystem.PartyEntryBlockReason(_state, ev, p) == null).ToList();
		foreach (var p in parties)
			picker.AddItem($"{p.Name}　{StripBbcode(PartySummary(ev, p))}");
		picker.Disabled = parties.Count == 0;
		if (parties.Count == 0)
		{
			string reason = _state.SavedParties.Select(p => TournamentSystem.PartyEntryBlockReason(_state, ev, p)).FirstOrDefault(r => r != null)
				?? "部隊が無い";
			picker.SetItemText(0, $"出られる部隊が無い（{reason}）");
		}
		picker.ItemSelected += index =>
		{
			if (index <= 0) return;
			if (TournamentSystem.TryEnterParty(_state, ev, parties[(int)index - 1], TournamentPrep.Rest))
				StateChanged.Invoke();
		};
		row.AddChild(picker);
		return row;
	}

	/// <summary>誰も出られないときの、よくある理由（現役の冒険者の理由のうち最も多いもの）。</summary>
	private string BlockSummary(TournamentEvent ev) =>
		_state.Adventurers.Where(a => !a.IsRetired)
			.Select(a => TournamentSystem.EntryBlockReason(_state, ev, a)).OfType<string>()
			.GroupBy(r => r).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "冒険者がいない";

	private static string StripBbcode(string text) =>
		System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[^\]]+\]", "");

	private string ResultText(TournamentEvent ev)
	{
		var r = ev.Result!;
		var sb = new StringBuilder();
		sb.Append(r.WinnerIsOurs ? $"[color=gold][b]🏆 優勝：{r.WinnerName}！[/b][/color]" : $"優勝：{r.WinnerName}");
		foreach (var p in r.Placings.Where(p => !(p.Placing == 1 && r.WinnerIsOurs && p.Name == r.WinnerName)))
			sb.Append($"\n{p.Name}：{TournamentSystem.PlacingLabel(p.Placing)}{(p.Prize > 0 ? $"（賞金 {p.Prize}G）" : "")}");
		return sb.ToString();
	}

	// ==================== 右：暦と戦績 ====================

	private string BuildCalendar()
	{
		int year = GameCalendar.YearOf(_state.WeekNumber);
		int month = GameCalendar.MonthOfYear(_state.WeekNumber);
		var sb = new StringBuilder();
		sb.Append($"[b]📅 {year}年目の大会の暦[/b]　[color=gray]（定例のG1は毎年同じ月。地方・王都の大会は年ごとに変わる）[/color]");
		foreach (var ev in TournamentSystem.EventsOfYear(_state, year).OrderBy(e => e.Month).ThenBy(e => e.Week))
		{
			string when = $"{MonthLabel(ev.Month)} 第{ev.Week}週";
			string line = $"{when}　[color={GradeColor(ev.Grade)}]{ev.Name}[/color]（{TournamentSystem.GradeLabel(ev.Grade)}・{TournamentSystem.DisciplineLabel(ev.Discipline)}）";
			if (ev.Result != null)
				line += ev.Result.WinnerIsOurs ? $"　[color=gold]🏆 {ev.Result.WinnerName}[/color]"
					: ev.Result.Placings.Count > 0 ? $"　[color=gray]{string.Join("・", ev.Result.Placings.Select(p => $"{p.Name} {TournamentSystem.PlacingLabel(p.Placing)}"))}[/color]"
					: "　[color=gray]出場なし[/color]";
			else if (ev.Month == month)
				line = $"[b]▶ {line}[/b]";
			else if (ev.Month < month)
				line = $"[color=gray]{line}[/color]";
			sb.Append('\n').Append(line);
		}
		return sb.ToString();
	}

	private string BuildHistory()
	{
		var sb = new StringBuilder();
		sb.Append($"[b]📜 ギルドの戦績[/b]　入賞（ベスト4以上） {_state.TournamentPlacingsTotal}回・賞金の累計 {_state.TournamentPrizeTotal}G");
		var g1 = _state.TournamentEvents.Where(e => e.Result != null && e.Grade == TournamentGrade.G1)
			.OrderByDescending(e => e.Year).ThenByDescending(e => e.Month).Take(20).ToList();
		sb.Append("\n\n[b]👑 歴代のG1の優勝者[/b]");
		if (g1.Count == 0)
			sb.Append("\n[color=gray]まだG1は行われていない。[/color]");
		foreach (var ev in g1)
		{
			string winner = ev.Result!.WinnerIsOurs ? $"[color=gold]{ev.Result.WinnerName}（当ギルド）[/color]" : ev.Result.WinnerName;
			sb.Append($"\n{ev.Year}年目 {ev.Name}：{winner}");
		}
		return sb.ToString();
	}

	private static string MonthLabel(int monthOfYear)
	{
		var season = (Season)((monthOfYear - 1) / 3);
		return $"{GameCalendar.SeasonLabel(season)}{(monthOfYear - 1) % 3 + 1}の月";
	}

	private static string GradeColor(TournamentGrade grade) => grade switch
	{
		TournamentGrade.G1 => "gold",
		TournamentGrade.G2 => "lightskyblue",
		TournamentGrade.Special => "plum",
		_ => "white",
	};
}
