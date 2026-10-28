using System;
using System.Collections.Generic;
using Ekiden.Domain.Race;

namespace Ekiden.Domain.World
{
    /// <summary>区間記録(04 §8.3。02 §4.8 の <c>course_records</c>)。他校の走者は ID を持たない(<see cref="RunnerId"/> = 0)。</summary>
    public sealed class CourseRecord
    {
        public int SchoolId, RunnerId, TimeDs, Year;
    }

    /// <summary>大会記録(総合タイム。02 §4.8 の <c>team_records</c>)。</summary>
    public sealed class TeamRecord
    {
        public int SchoolId, TotalDs, Year;
    }

    /// <summary>大会の順位表の 1 行(02 §4.8。他校は順位・合計タイムだけ残す = 09 §5.2)。順位は並びの順(1 始まり)。</summary>
    public sealed class RaceRow
    {
        public int SchoolId, TotalDs;
        public bool Kuriage;
    }

    /// <summary>過去の大会 1 つの順位表(<c>race_records</c>)。優勝校は先頭の行。</summary>
    public sealed class RaceEntry
    {
        public int Year;
        public RaceKind Kind;
        public readonly List<RaceRow> Rows = new List<RaceRow>();
        public int WinnerId { get { return Rows.Count > 0 ? Rows[0].SchoolId : -1; } }
    }

    /// <summary>
    /// 世界の履歴(02 §4.8・§4.9 の <c>history</c>): 大会ごとの順位表(歴代の優勝校・順位)と、区間記録・大会記録。他校も含む全チームの結果から更新する。
    /// 予熱の年度(自校なしで先に回す)の大会も入るので、ゲームの開始時にすでに履歴と記録がある。
    /// 記録の規則(04 §8.3 を実装するときの決め): 最初の 1 回は基準として黙って入れる(『更新』ではない)。以後は**厳密に速いとき**だけ更新(同タイムは更新でない)。
    /// 区間記録の保持者は、その区間の最速タイムのチームのうち順位の先頭(同タイムでも保持者は 1 人)。大会記録は繰り上げのないチームの最速(= 優勝校)。予選会は含めない。
    /// </summary>
    public sealed class RaceHistory
    {
        public readonly List<RaceEntry> Races = new List<RaceEntry>();
        public readonly CourseRecord[][] Course = new CourseRecord[3][];       // [大会][区間]。まだなければ null
        public readonly TeamRecord[] Team = new TeamRecord[3];

        /// <summary>大会の結果を履歴に加え、記録を更新する。更新があったもの(区間の順、最後に大会記録)を返す。<paramref name="teams"/> は走者の ID を引くのに使う(なくてもよい)。</summary>
        public List<RecordUpdate> Record(int year, RaceKind kind, IReadOnlyList<TeamResult> results, IReadOnlyList<RaceTeam> teams)
        {
            if (results == null || results.Count == 0) return new List<RecordUpdate>();
            var entry = new RaceEntry { Year = year, Kind = kind };
            foreach (TeamResult t in results) entry.Rows.Add(new RaceRow { SchoolId = t.Id, TotalDs = t.TotalDs, Kuriage = t.Kuriage });
            Races.Add(entry);
            return Evaluate(year, kind, results, teams, true);
        }

        /// <summary>この結果で更新される記録を、履歴を変えずに返す(当日のレース進行の見込み用。<see cref="Record"/> の結果と同じ)。</summary>
        public List<RecordUpdate> Peek(int year, RaceKind kind, IReadOnlyList<TeamResult> results, IReadOnlyList<RaceTeam> teams)
        {
            if (results == null || results.Count == 0) return new List<RecordUpdate>();
            return Evaluate(year, kind, results, teams, false);
        }

        private List<RecordUpdate> Evaluate(int year, RaceKind kind, IReadOnlyList<TeamResult> results, IReadOnlyList<RaceTeam> teams, bool apply)
        {
            var updates = new List<RecordUpdate>();
            int k = (int)kind;
            int n = results[0].Sections.Length;
            CourseRecord[] course = Course[k];
            if (course == null && apply) course = Course[k] = new CourseRecord[n];
            for (int si = 0; si < n; si++)
            {
                TeamResult holder = results[0];
                foreach (TeamResult t in results) if (t.Sections[si] < holder.Sections[si]) holder = t;      // 同タイムは順位の先頭
                CourseRecord old = course != null ? course[si] : null;
                if (old != null && holder.Sections[si] >= old.TimeDs) continue;
                int runner = 0;
                if (teams != null)
                    foreach (RaceTeam rt in teams) if (rt.Id == holder.Id && rt.Lineup != null && si < rt.Lineup.Length) runner = rt.Lineup[si].RunnerId;
                if (apply) course[si] = new CourseRecord { SchoolId = holder.Id, RunnerId = runner, TimeDs = holder.Sections[si], Year = year };
                if (old != null) updates.Add(new RecordUpdate { Kind = kind, Section = si, SchoolId = holder.Id, RunnerId = runner, TimeDs = holder.Sections[si], Year = year, PrevSchoolId = old.SchoolId, PrevTimeDs = old.TimeDs, PrevYear = old.Year });
            }
            TeamResult winner = results[0];
            if (!winner.Kuriage)
            {
                TeamRecord oldTeam = Team[k];
                if (oldTeam == null || winner.TotalDs < oldTeam.TotalDs)
                {
                    if (apply) Team[k] = new TeamRecord { SchoolId = winner.Id, TotalDs = winner.TotalDs, Year = year };
                    if (oldTeam != null) updates.Add(new RecordUpdate { Kind = kind, Section = -1, SchoolId = winner.Id, TimeDs = winner.TotalDs, Year = year, PrevSchoolId = oldTeam.SchoolId, PrevTimeDs = oldTeam.TotalDs, PrevYear = oldTeam.Year });
                }
            }
            return updates;
        }
    }
}
