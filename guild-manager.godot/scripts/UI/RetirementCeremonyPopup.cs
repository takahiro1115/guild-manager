#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 引退式（2026年10月・大会と育成の栄光 段2、→ docs/検討中_大会と育成の栄光.md §4.1）。満期で引退した者を1人ずつ1枚で見せ、「次へ」でめくる（ユーザー判断）。
/// 年度末の月は大会の結果の小窓と月報のあいだに、早期引退は引退させたときに開く。
/// 1枚＝顔・二つ名・8年の歩み（年表から大きな出来事）・見立てと本当の素質・ピークの能力・勝ち鞍・殿堂入り・アルベールの言葉。
/// 記録の少ない者は短い版。中身は Core（HonorSystem.BuildCeremony）が作る。シーンは使わずコードで組む。
/// </summary>
public partial class RetirementCeremonyPopup : Window
{
	/// <summary>全員を見終えて閉じた。</summary>
	public event Action Closed = delegate { };

	private TextureRect _portrait = null!;
	private RichTextLabel _text = null!;
	private Button _nextButton = null!;
	private readonly List<RetirementCeremony> _ceremonies = new();
	private int _index;

	public override void _Ready()
	{
		Title = "🌸 引退式";
		Exclusive = true;
		Size = new Vector2I(920, 720);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += Finish;

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(panel);
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 18);
		panel.AddChild(margin);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 10);
		margin.AddChild(root);

		var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 18);
		_portrait = new TextureRect
		{
			CustomMinimumSize = new Vector2(220, 280),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
		};
		body.AddChild(_portrait);
		var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(_text);
		body.AddChild(scroll);
		root.AddChild(body);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		_nextButton = new Button { CustomMinimumSize = new Vector2(200, 38) };
		_nextButton.Pressed += OnNext;
		buttons.AddChild(_nextButton);
		root.AddChild(buttons);
	}

	/// <summary>引退した者の引退式を順に見せる。誰もいなければ何もせず Closed を呼ぶ。</summary>
	public void ShowCeremonies(GameState state, IEnumerable<Adventurer> retirees)
	{
		_ceremonies.Clear();
		_ceremonies.AddRange(retirees.Select(a => HonorSystem.BuildCeremony(state, a)));
		_index = 0;
		if (_ceremonies.Count == 0)
		{
			Closed.Invoke();
			return;
		}
		Render();
		PopupCentered(Size);
		_nextButton.GrabFocus();
	}

	private void OnNext()
	{
		if (_index + 1 < _ceremonies.Count)
		{
			_index++;
			Render();
		}
		else
		{
			Finish();
		}
	}

	private void Finish()
	{
		Hide();
		Closed.Invoke();
	}

	private void Render()
	{
		var c = _ceremonies[_index];
		_portrait.Texture = AdventurerPanel.LoadPortraitTexture(c.PortraitId);
		_nextButton.Text = _index + 1 < _ceremonies.Count ? $"次へ（{_index + 1}/{_ceremonies.Count}）" : "見送る";

		var sb = new StringBuilder();
		sb.Append($"[font_size=24]{c.Name}[/font_size]");
		if (c.Title.Length > 0)
			sb.Append($"　[color=gold]「{c.Title}」[/color]");
		sb.Append($"\n在籍{c.YearsActive}年。今日、ギルドを巣立つ。\n");
		if (c.HallOfFame)
			sb.Append("[color=gold][font_size=20]🏛 殿堂入り[/font_size][/color]\n");

		if (!c.IsShort)
		{
			sb.Append("\n[font_size=18]📖 歩み[/font_size]\n");
			foreach (var e in c.Highlights)
				sb.Append($"[color=gray]{GameCalendar.FormatMonth(e.Week)}[/color]　{e.Text}\n");

			if (c.Wins.Count > 0)
				sb.Append($"\n[font_size=18]🏆 勝ち鞍[/font_size]\n{string.Join("\n", c.Wins)}\n");

			sb.Append("\n[font_size=18]📈 ピークの能力[/font_size]\n");
			sb.Append(string.Join("　", c.PeakStats.Select(kv => $"{kv.Key} {kv.Value}")) + "\n");
		}
		if (c.JoinEstimate.Length > 0)
			sb.Append($"副官の見立ては {c.JoinEstimate}、本当の素質は [color=khaki]{c.TrueRank}[/color] だった。\n");

		if (c.AlbertLine.Length > 0)
			sb.Append($"\n[color=violet]{c.AlbertLine}[/color]\n[right][color=gray]――アルベール[/color][/right]");
		_text.Text = sb.ToString();
	}
}
