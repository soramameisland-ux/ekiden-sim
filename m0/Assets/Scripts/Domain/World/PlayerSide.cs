using System.Collections.Generic;
using Ekiden.Domain.Race;
using Ekiden.Domain.Rng;

namespace Ekiden.Domain.World
{
    /// <summary>世界の年度処理(<see cref="WorldSim"/>)に差し込む、自校の側(05 §9・04 §11)。
    /// 検算用の抽象(<see cref="PlayerAgent"/>)と、実体の選手を持つゲーム本体(<c>GameSession</c>)が実装する。世界の更新(他校・名声・入学の割り当て)は同じ道を通る。</summary>
    public interface IPlayerSide
    {
        School School { get; }

        /// <summary>年度の最初: 大会に使う自校の選手(10 能力・特殊能力・調子など)。</summary>
        List<RaceRunner> RaceRoster(double lv, IRngFactory rngs);

        /// <summary>予選会に出す自校のチーム。null なら他校と同じ決め方(登録の抽選 → 長距離の上位 12 人 → 調子は抽選)にする。</summary>
        QualifierTeam PrepareQualifier(int qualId, List<RaceRunner> roster, IRngFactory rngs);

        /// <summary>第一〜第三戦に出す自校のチーム。null = 棄権(出走人数に足りない。04 §4.1 E1)。</summary>
        RaceTeam PrepareRace(RaceKind kind, int raceId, Env[] envs, List<RaceRunner> roster, IRngFactory rngs);

        /// <summary>3 大会と名声ポイントの集計のあと、年度末の更新の前。</summary>
        void AfterRaces(WorldSim world, int year, YearResult res);

        /// <summary>年度末の資金・施設の強化(成長の確定の前。施設レベルが成長に効く)。</summary>
        void Finance();

        /// <summary>成長の確定のあと、4 年生が卒業するとき(学年を上げる前)。抽象の自校の名簿の 4 年生が渡される(実体の選手を持つ側は空)。</summary>
        void Graduating(IReadOnlyList<WorldRunner> graduates);

        /// <summary>特別招聘券(07)。招聘する選手を返す(なければ null)。呼ぶのは卒業・学年の繰り上げのあと。</summary>
        WorldRunner TicketStep(int year, YearResult res, IRngFactory rngs);

        /// <summary>入学の割り当てで使う自校の「来年度の名簿の力の 10 番目」と在籍数。null = 抽象の名簿から求める(検算用の抽象の自校)。</summary>
        double? PlayerP10 { get; }
        int? PlayerRosterCount { get; }

        /// <summary>入学の割り当てへの自校の提示(おまかせのスカウト。05 §7.3)。</summary>
        PlayerOffer Offer(WorldSim world, IReadOnlyList<WorldRunner> pool, IRngFactory rngs);

        /// <summary>割り当てで自校に入学する候補と、招聘の選手を受け取る。戻り値は、抽象の自校の名簿 (<see cref="School.Roster"/>) に足す選手(実体の選手を持つ側は、自分で <c>Runner</c> に展開して空を返す)。</summary>
        List<WorldRunner> Intake(List<WorldRunner> allocated, WorldRunner ticket);
    }

    /// <summary>大会 1 つの結果(出走したチームと、順位順の結果)。</summary>
    public sealed class RaceOutcome
    {
        public RaceKind Kind;
        public int RaceId;
        public List<RaceTeam> Teams;
        public List<TeamResult> Results;
        public List<RecordUpdate> Records = new List<RecordUpdate>();      // この大会で更新された記録(区間の順、最後に大会記録)
    }

    /// <summary>1 年度の途中の状態(出場権・各校の大会用の選手・ここまでの結果)。<see cref="WorldSim.BeginYear"/> が作る。年度の途中でセーブするときは、<see cref="Result"/> だけを保存し、
    /// 読み込みでは <see cref="WorldSim.BeginYear"/> をやり直して <see cref="Result"/> を戻す(出場権・選手は年度の途中では変わらない)。</summary>
    public sealed class YearState
    {
        public int Year;
        public YearResult Result;
        public IPlayerSide Player;
        public List<int> FallIds, NationalIds, Seeds;
        public Dictionary<int, List<RaceRunner>> Rosters;
        public bool QualifierDone;
        public bool Settled;                                   // 年度末の前半(精算まで)が済んだ。ここから後半へ進める
        public List<QualifierResult> QualifierResults;     // 予選会の全校の結果(順位順)

        /// <summary>その大会に出る学校の ID(予選会のあとでないと第三戦は決まらない)。</summary>
        public List<int> Entrants(RaceKind kind)
        {
            if (kind == RaceKind.Fall) return FallIds;
            if (kind == RaceKind.National) return NationalIds;
            var ids = new List<int>(Seeds);
            ids.AddRange(Result.Qualified);
            return ids;
        }
    }
}
