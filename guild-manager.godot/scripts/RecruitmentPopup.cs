using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 新春採用試験（仕様書 03 §2.4）のモーダルポップアップ。仕様書 03 §9 参照。
///
/// 年1回・新年第1週（2年目以降）にのみ MainDashboard から Open() で呼び出される。
/// 採用処理（採用する/見送るの意思決定）が完了するまで閉じられない
/// （Escape・外側クリック等で意図せず閉じようとした場合は PopupHide を検知して
/// 即座に再表示し、強制的にモーダルとして機能させる）。
///
/// 「採用する」は選択中の1名を雇用してポップアップを開いたままにする（複数採用可、→ 03 §2.4）。
/// 「見送る」は残り全員を見送ってポップアップを閉じる。
/// </summary>
public partial class RecruitmentPopup : PopupPanel
{
	private ItemList _candidateList = null!;
	private Button _hireButton = null!;
	private Button _declineButton = null!;
	private Label _statusLabel = null!;

	private GameState _state = null!;
	private RecruitmentSystem _recruitmentSystem = null!;
	private List<RecruitmentOffer> _candidates = new();
	private bool _decided;

	/// <summary>採用処理が完了してポップアップが閉じた（＝次週へ進めてよい）ことを通知する。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_candidateList = GetNode<ItemList>("%CandidateList");
		_hireButton = GetNode<Button>("%HireButton");
		_declineButton = GetNode<Button>("%DeclineButton");
		_statusLabel = GetNode<Label>("%StatusLabel");

		_hireButton.Pressed += OnHirePressed;
		_declineButton.Pressed += OnDeclinePressed;
		PopupHide += OnPopupHide;
	}

	/// <summary>
	/// 採用試験を開始する。候補者を生成し、モーダルとして表示する。
	/// </summary>
	/// <param name="candidateCount">
	/// 提示する候補者数。省略時（null）は通常の新春採用試験と同じ人数
	/// （RecruitmentSystem.GenerateCandidatesの既定＝RecruitmentBalance.CandidateCount）。
	/// 第1週チュートリアル採用試験ではRecruitmentBalance.TutorialCandidateCountを渡す
	/// （→ MainDashboard.StartNewGame）。
	/// </param>
	public void Open(GameState state, RecruitmentSystem recruitmentSystem, int? candidateCount = null)
	{
		_state = state;
		_recruitmentSystem = recruitmentSystem;
		_candidates = recruitmentSystem.GenerateCandidates(state, GetScoutMasterBonus(), candidateCount);
		_decided = false;

		RefreshList();
		PopupCentered();
	}

	/// <summary>
	/// 既に生成済みの応募一覧をそのまま提示する（→ コアシステム刷新仕様 Phase 4：
	/// ランク昇格試験の突破時に GuildProgressionSystem が生成した新人を表示する）。
	/// ここで再生成してしまうと、週報ログに出した人数・顔ぶれと実際の提示内容が
	/// 食い違うため、生成済みのインスタンスを受け取る形にしている。
	/// </summary>
	public void Open(GameState state, RecruitmentSystem recruitmentSystem, IReadOnlyList<RecruitmentOffer> offers)
	{
		_state = state;
		_recruitmentSystem = recruitmentSystem;
		_candidates = new List<RecruitmentOffer>(offers);
		_decided = false;

		RefreshList();
		PopupCentered();
	}

	/// <summary>
	/// スカウト顧問が任命されていれば、そのボーナスを返す（未任命なら0。→ 03 §7.3）。
	/// v1.4改訂：冒険者支援室がLv0（未建設）の間はボーナスを発生させない（防御的チェック。
	/// 通常はAdvisorSystem.TryAssignScoutMaster側のガードにより、Lv0のままAssignedScoutMasterが
	/// 設定されることは無い）。
	/// </summary>
	private double GetScoutMasterBonus()
	{
		if (_state.GetFacilityLevel(FacilityType.RecruitmentOffice) < 1) return 0;
		if (_state.AssignedScoutMaster == null) return 0;

		var scoutMaster = _state.RetiredAdventurers.FirstOrDefault(a => a.Id == _state.AssignedScoutMaster.Value);
		return scoutMaster == null ? 0 : AdvisorSystem.GetScoutMasterBonus(scoutMaster);
	}

	private void RefreshList()
	{
		_candidateList.Clear();
		foreach (var offer in _candidates)
		{
			var c = offer.Candidate;
			_candidateList.AddItem(
				$"{c.Name}（{c.JobClass}）{c.Age}歳 総合PA{c.TotalPA:F0} 契約金{offer.SigningBonus}G 週給{c.WeeklyWage}G");
		}

		_statusLabel.Text = $"現役枠の空き: {_recruitmentSystem.GetOpenSlotCount(_state)}　所持金: {_state.Gold} G";
	}

	private void OnHirePressed()
	{
		var selected = _candidateList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "採用する候補者を選択してください。";
			return;
		}

		var offer = _candidates[selected[0]];

		if (_recruitmentSystem.GetOpenSlotCount(_state) <= 0)
		{
			_statusLabel.Text = "現役枠がいっぱいで採用できません（→ 03 §6：施設拡張は未実装）。";
			return;
		}
		if (_state.Gold < offer.SigningBonus)
		{
			_statusLabel.Text = $"契約金 {offer.SigningBonus}G が足りません（所持金 {_state.Gold}G）。";
			return;
		}

		_recruitmentSystem.TryHire(_state, offer);
		_candidates.RemoveAt(selected[0]);
		RefreshList();

		// 候補者を使い切った、または枠が埋まったら自動的に締める（それ以上は選びようがないため）。
		if (_candidates.Count == 0 || _recruitmentSystem.GetOpenSlotCount(_state) <= 0)
			FinishAndClose();
	}

	private void OnDeclinePressed() => FinishAndClose();

	private void FinishAndClose()
	{
		_decided = true;
		Hide();
	}

	/// <summary>
	/// Escape・外側クリックなど、ボタン以外の経路でポップアップが閉じようとした場合のフック。
	/// 意思決定（採用/見送る）が済んでいなければ、閉じさせずに即座に再表示する。
	///
	/// 注意：以前は CallDeferred(nameof(PopupCentered)) としていたが、文字列で呼ぶ遅延呼び出しは
	/// エンジン側のメソッド名（"popup_centered"）で解決されるため、C#名の "PopupCentered" では
	/// 「Method not found」で必ず失敗していた。その結果、決定前に外側クリック等で閉じると
	/// ポップアップが消えたまま Closed も通知されず、「次週へ」が二度と押せなくなっていた。
	/// 名前解決に頼らないよう、ラムダを Callable にして遅延呼び出しする。
	/// </summary>
	private void OnPopupHide()
	{
		if (!_decided)
		{
			Callable.From(() => PopupCentered()).CallDeferred();
			return;
		}

		Closed.Invoke();
	}
}
