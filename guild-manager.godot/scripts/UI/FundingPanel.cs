#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 「💰 資金」の画面（§0.115。テストプレイの手直し：スポンサー契約が施設タブにあって施設が開くまで見られず、条件も分かりにくかった）。
/// 最初から開いている。上から、1か月の収支の見込み・借金（商人ギルド・イザベラの融資）・スポンサー契約。
/// スポンサー契約は、結んでいる契約の進み具合（バー・残り週・ほかの期限との重なり）と、届いた話を「結ぶと／届けば／届かなければ」の3行で見せ、
/// ここでも結ぶ・断るができる（月報からもできる）。値と結べない理由・借りられない理由はすべて Core（FundingSystem）から取る。
/// シーンは持たず、コードで組む（依頼の画面と同じ）。
/// </summary>
public partial class FundingPanel : MarginContainer
{
	private readonly VBoxContainer _box;
	private GameState _state = null!;

	/// <summary>借りる・返す・結ぶ・断るでゲーム状態が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

	public FundingPanel()
	{
		Name = "FundingTab";
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			AddThemeConstantOverride($"margin_{side}", 12);
		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_box.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_box);
		AddChild(scroll);
	}

	/// <summary>最新のゲーム状態で画面を描き直す（MainDashboard.RefreshAll から毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		foreach (var child in _box.GetChildren())
		{
			_box.RemoveChild(child);
			child.QueueFree();
		}
		_box.AddChild(Card(new Color("#d4a017"), BuildOutlook()));
		_box.AddChild(Card(new Color("#60a5fa"), BuildSponsors()));
		_box.AddChild(Card(new Color("#9ca3af"), BuildLoans()));
	}

	// ==================== 収支の見込み ====================

	private Control BuildOutlook()
	{
		var o = FundingSystem.Outlook(_state);
		var tier = MasterMoodSystem.GetTier(_state.MasterMood);
		string net = o.Net >= 0 ? $"[color=lime]+{o.Net} G[/color]" : $"[color=orange]{o.Net} G[/color]";
		string text = $"[font_size=18][b]💰 1か月の収支の見込み[/b][/font_size]　所持金 [b]{_state.Gold} G[/b]\n" +
			$"入り：アルベールの内職の売上 [color=lime]+{o.SideJob} G[/color][color=gray]（いまの機嫌「{MainDashboard.MoodTierLabel(tier)}」で×{MasterMoodSystem.GetSideJobMultiplier(tier):0.0}）[/color]\n" +
			$"出：{Costs(o)}\n" +
			$"差し引き：{net} / 月";
		if (FundingSystem.MonthsOfRunway(_state, o) is int months)
			text += $"　[color=orange]このままだと約 {months} か月で底をつく[/color]";
		text += "\n[color=gray]依頼・主の撃破の報酬、大会の賞金、遺物・素材の売却は入れていない。[/color]";
		if (o.Severance > 0)
			text += $"\n[color=khaki]年度末（冬の終わり）に満期の子の退職金：約 {o.Severance} G[/color]";
		return Rich(text);
	}

	/// <summary>出ていくもの（0 G の項目は出さない）。</summary>
	private static string Costs(FundingSystem.MonthlyOutlook o)
	{
		var parts = new List<string> { $"週給 −{o.Wages} G" };
		if (o.Stipends > 0) parts.Add($"引退者の手当 −{o.Stipends} G");
		if (o.Upkeep > 0) parts.Add($"施設の維持費 −{o.Upkeep} G");
		if (o.LoanPayments > 0) parts.Add($"借金の返済 −{o.LoanPayments} G");
		return string.Join("・", parts);
	}

	// ==================== スポンサー契約 ====================

	private Control BuildSponsors()
	{
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 6);
		box.AddChild(Rich("[font_size=18][b]🤝 スポンサー契約[/b][/font_size]　[color=gray]結べるのは1件まで。前金がすぐ入り、期限までにノルマに届けば成功報酬、届かなければ違約金。話は春と秋のはじめに届き、結ばなければ季節の終わりに流れる（罰なし）[/color]"));

		if (FundingSystem.ActiveContract(_state) is { } c)
		{
			int progress = FundingSystem.Progress(_state, c);
			box.AddChild(Rich($"[b]{c.SponsorName}（{(c.Bold ? "強気" : "控えめ")}）[/b]：{FundingSystem.QuotaLabel(c)}"));
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, c.Target), Value = Math.Min(progress, c.Target), ShowPercentage = false, CustomMinimumSize = new Vector2(260, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };
			bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color("#2a3140") });
			bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color("#60a5fa") });
			row.AddChild(bar);
			row.AddChild(new Label { Text = $"いま {progress}/{c.Target}　期限 {Deadline(c)}（あと {FundingSystem.WeeksLeft(_state, c)} 週）" });
			box.AddChild(row);
			box.AddChild(Rich($"届けば：成功報酬 [color=lime]+{c.Bonus} G[/color]・機嫌 +{FundingBalance.SponsorSuccessMood}\n" +
				$"届かなければ：違約金 [color=orange]−{c.Penalty} G[/color]・機嫌 {FundingBalance.SponsorFailureMood}"));
			AddClashes(box, c);
		}
		var offers = FundingSystem.Offers(_state);
		foreach (var offer in offers)
			box.AddChild(BuildOffer(offer));
		if (FundingSystem.ActiveContract(_state) == null && offers.Count == 0)
			box.AddChild(Rich($"[color=gray]結んでいる契約も、届いている話も無い（{(_state.WeekNumber < FundingBalance.SponsorFirstWeek ? "1年目の秋から、" : "")}春と秋のはじめに届く）。[/color]"));
		return box;
	}

	private Control BuildOffer(SponsorContract offer)
	{
		var panel = new PanelContainer();
		var style = new StyleBoxFlat { BgColor = new Color("#1f2633"), BorderColor = offer.Bold ? new Color("#f97316") : new Color("#60a5fa") };
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.SetContentMarginAll(8);
		panel.AddThemeStyleboxOverride("panel", style);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 4);
		panel.AddChild(box);
		box.AddChild(Rich($"[b]{(offer.Bold ? "【強気】" : "【控えめ】")}{offer.SponsorName}[/b]：{FundingSystem.QuotaLabel(offer)}" +
			$"　[color=gray]いま 0/{offer.Target}（結んだときから数える）・期限 {Deadline(offer)}（あと {FundingSystem.WeeksLeft(_state, offer)} 週）[/color]"));
		box.AddChild(Rich(OfferTerms(offer)));
		AddClashes(box, offer);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		string? block = FundingSystem.SignBlockReason(_state, offer);
		var sign = new Button { Text = $"結ぶ（前金 {offer.Advance} G）", Disabled = block != null, TooltipText = block ?? "" };
		var decline = new Button { Text = "断る" };
		sign.Pressed += () =>
		{
			if (FundingSystem.Sign(_state, offer)) StateChanged.Invoke();
		};
		decline.Pressed += () =>
		{
			if (FundingSystem.Decline(_state, offer)) StateChanged.Invoke();
		};
		row.AddChild(sign);
		row.AddChild(decline);
		box.AddChild(row);
		return panel;
	}

	/// <summary>届いた話の損得の3行（結ぶと／届けば／届かなければ）。月報のスポンサー契約の欄でも使う。</summary>
	public static string OfferTerms(SponsorContract offer) =>
		$"結ぶと：前金 [color=lime]+{offer.Advance} G[/color]（すぐ入る）\n" +
		$"届けば：成功報酬 [color=lime]+{offer.Bonus} G[/color]・機嫌 +{FundingBalance.SponsorSuccessMood}\n" +
		$"届かなければ：違約金 [color=orange]−{offer.Penalty} G[/color]・機嫌 {FundingBalance.SponsorFailureMood}";

	/// <summary>期限の月に重なるもの（依頼の期限・年度末の退職金）を ⚠ で出す。</summary>
	private void AddClashes(VBoxContainer box, SponsorContract c)
	{
		var clashes = FundingSystem.DeadlineClashes(_state, c);
		if (clashes.Count > 0)
			box.AddChild(Rich($"[color=orange]⚠ 期限の月（{GameCalendar.FormatMonth(c.DeadlineWeek)}）は、{string.Join("・", clashes)}とも重なる[/color]"));
	}

	private static string Deadline(SponsorContract c) => $"{GameCalendar.FormatMonth(c.DeadlineWeek)} 第{GameCalendar.WeekOfMonth(c.DeadlineWeek)}週";

	// ==================== 借金（§0.104。施設の画面から移した） ====================

	private Control BuildLoans()
	{
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 4);
		box.AddChild(Rich("[font_size=18][b]🏦 借金[/b][/font_size]　[color=gray]借金は月の最後の週に維持費と一緒に返す（払えなければ所持金がマイナスになり、破産の数えに入る）[/color]"));

		foreach (var loan in _state.Loans)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			row.AddChild(new Label
			{
				Text = $"・{FundingSystem.LenderLabel(loan.Lender)}への借金：残り {loan.Remaining} G（月 {Math.Min(loan.MonthlyPayment, loan.Remaining)} G ずつ）" +
					(loan.Lender == LoanLender.Merchant ? "。返し終わるまでマスターの機嫌が毎週下がる" : ""),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			});
			int early = FundingSystem.EarlyRepayment(loan);
			var repay = new Button { Text = $"一括で返す（{early} G）", Disabled = _state.Gold < early, SizeFlagsVertical = SizeFlags.ShrinkCenter };
			repay.TooltipText = _state.Gold < early ? "お金が足りない" : "残りの手数料はかからない";
			var target = loan;
			repay.Pressed += () =>
			{
				if (FundingSystem.RepayEarly(_state, target))
					StateChanged.Invoke();
			};
			row.AddChild(repay);
			box.AddChild(row);
		}

		// 商人ギルドの融資（いつでも・高利）
		var merchantRow = new HBoxContainer();
		merchantRow.AddThemeConstantOverride("separation", 8);
		merchantRow.AddChild(new Label { Text = $"商人ギルド（手数料{FundingBalance.MerchantLoanFee:P0}・{FundingBalance.MerchantLoanMonths}か月）：" });
		foreach (int amount in FundingBalance.MerchantLoanAmounts)
			merchantRow.AddChild(LoanButton(LoanLender.Merchant, amount, FundingSystem.MerchantLoanBlockReason(_state, amount)));
		box.AddChild(merchantRow);

		// イザベラの融資（来訪のあと・低利・年1回）
		var isabellaRow = new HBoxContainer();
		isabellaRow.AddThemeConstantOverride("separation", 8);
		int limit = FundingSystem.IsabellaLoanLimit(_state);
		isabellaRow.AddChild(new Label { Text = $"イザベラ（手数料{FundingBalance.IsabellaLoanFee:P0}・{FundingBalance.IsabellaLoanMonths}か月・年1回）：" });
		isabellaRow.AddChild(LoanButton(LoanLender.Isabella, limit, FundingSystem.IsabellaLoanBlockReason(_state, limit)));
		box.AddChild(isabellaRow);
		return box;
	}

	private Button LoanButton(LoanLender lender, int amount, string? block)
	{
		var button = new Button { Text = $"{amount} G 借りる", Disabled = block != null };
		button.TooltipText = block ?? $"返す額 {FundingSystem.TotalDue(lender, amount)} G（月 {FundingSystem.MonthlyPayment(lender, amount)} G × {FundingSystem.Months(lender)}か月）";
		button.Pressed += () => OnBorrowPressed(lender, amount);
		return button;
	}

	/// <summary>借りる前に、返す額と月々の返済を確かめの小窓で見せる。</summary>
	private void OnBorrowPressed(LoanLender lender, int amount)
	{
		string text = $"{FundingSystem.LenderLabel(lender)}から {amount} G 借りる。\n\n" +
			$"返す額：{FundingSystem.TotalDue(lender, amount)} G（月の最後の週に {FundingSystem.MonthlyPayment(lender, amount)} G ずつ、{FundingSystem.Months(lender)}か月）\n" +
			(lender == LoanLender.Merchant ? $"[color=orange]返し終わるまで、マスターの機嫌が毎週{FundingBalance.MerchantLoanMoodLoss}下がる。[/color]\n" : "") +
			"一括で返せば、残りの手数料はかからない。";
		ChoreConfirm.Show(this, "💰 借りる", text, "借りる", () =>
		{
			if (FundingSystem.Borrow(_state, lender, amount))
				StateChanged.Invoke();
		});
	}

	// ==================== 部品 ====================

	private static PanelContainer Card(Color accent, Control content)
	{
		var panel = new PanelContainer();
		var style = new StyleBoxFlat { BgColor = new Color("#161a22"), BorderColor = accent };
		style.BorderWidthLeft = 4;
		style.SetCornerRadiusAll(4);
		style.SetContentMarginAll(10);
		panel.AddThemeStyleboxOverride("panel", style);
		panel.AddChild(content);
		return panel;
	}

	private static RichTextLabel Rich(string bbcode) => new()
	{
		BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = bbcode,
		SizeFlagsHorizontal = SizeFlags.ExpandFill,
	};
}
