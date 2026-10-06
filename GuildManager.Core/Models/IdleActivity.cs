namespace GuildManager.Core.Models
{
    /// <summary>
    /// 待機中の過ごし方（2026年10月・§0.73、→ Systems.IdleActivitySystem）。冒険者ごとに一度決めておけば続く。
    /// 出撃しておらず・訓練施設にも入っておらず・HPが最大HP×TrainingBalance.IdleActivityHpRatio を超え・負傷も毒も無い週だけ、
    /// 決めた過ごし方をする。それ以外の週は静養（HPの自然回復）。
    /// セーブは数値で残る。旧セーブには無く、Help（研究を手伝う）で読まれる。
    /// </summary>
    public enum IdleActivity
    {
        /// <summary>アルベールの研究を手伝う：研究の手伝い（→ GameState.ResearchCredit）が貯まり、機嫌が少し上がる。HPは静養と同じく回復する。</summary>
        Help = 0,

        /// <summary>自主練：能力が少し伸びることがある（伸ばす能力は Adventurer.SelfTrainingStat、無ければ職業の伸び方）。HPを少し使う（静養の回復は受けるので、回復が少し遅くなる）。</summary>
        SelfTraining = 1,
    }
}
