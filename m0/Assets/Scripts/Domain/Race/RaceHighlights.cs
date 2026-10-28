using System;
using System.Collections.Generic;
using System.Linq;

namespace Ekiden.Domain.Race
{
    /// <summary>見どころの種類(04 §9.3)。宣言の順が、同じ重要度の中での並びの最後の決め手になる。</summary>
    public enum HighlightKind { CourseRecord, TeamRecord, LeadChange, GapMove, Brake, Stall, Boost, SectionPrize, RankMove }

    /// <summary>見どころ 1 件(区間 1 つ・チーム 1 つ・種類 1 つ)。</summary>
    public sealed class Highlight
    {
        public int Section;
        public HighlightKind Kind;
        public int TeamId;
        public bool IsOwn;
        public int OtherTeamId = -1;         // LeadChange: それまでの首位 / CourseRecord・TeamRecord: それまでの記録の保持校
        public double Magnitude;             // 大きいほど目立つ。LeadChange: 新しい首位と前の首位の差(ds)/ GapMove: 先頭との差が動いた量(ds)/ Brake・Stall・Boost: 時間の割合 / CourseRecord・TeamRecord: 前の記録より縮めた量(ds)/ SectionPrize: 区間 2 位との差(ds)/ RankMove: 動いた順位の数
        public int Delta;                    // GapMove: 先頭との差の変化(ds。正 = 離れた)/ RankMove: 順位の変化(正 = 上がった)

        /// <summary>重要度(1 が最も重要)。記録(04 §9.3 の ④ のうち)は稀で最も目立つので ① と同じ、あとは ① 首位交代 ② 差の変化 ③ ブレーキ・失速・好走 ④ 区間賞 ⑤ 順位の変動 の順。</summary>
        public int Tier { get { return TierOf(Kind); } }

        public static int TierOf(HighlightKind k)
        {
            switch (k)
            {
                case HighlightKind.CourseRecord:
                case HighlightKind.TeamRecord:
                case HighlightKind.LeadChange: return 1;
                case HighlightKind.GapMove: return 2;
                case HighlightKind.Brake:
                case HighlightKind.Stall:
                case HighlightKind.Boost: return 3;
                case HighlightKind.SectionPrize: return 4;
                default: return 5;
            }
        }
    }

    /// <summary>
    /// 見どころの判定(04 §9.3)。大会の結果から決まる表示の規則で、乱数は引かず、結果にも影響しない。判定は区間 s の終わりまでの記録だけを使う(途中経過で未来が漏れない)。
    /// <list type="number">
    /// <item><description>首位交代: 中継所の首位(累積が最小。同じならチーム ID の小さい方)が前の中継所と違う(区間 2 以降)</description></item>
    /// <item><description>差の変化: 先頭との差が前の中継所に対して <see cref="RaceConstants.HighlightGapDs"/> 以上動いた、首位以外の注目校(1 区は差が 0 から)。自校のほかは、動きの最も大きい 1 校だけ</description></item>
    /// <item><description>ブレーキ・失速・好走: その区間で起きた注目校</description></item>
    /// <item><description>区間賞: 区間タイムが最小(同タイムは全員。注目校でなくてもよい)</description></item>
    /// <item><description>順位の変動: 中継所の順位が前の中継所より <see cref="RaceConstants.HighlightRankMove"/> 以上動いた注目校(区間 2 以降)</description></item>
    /// </list>
    /// 区間記録・大会記録の更新(<see cref="RecordUpdate"/>。大会記録は最終区間のゴールで)は、最も重要(首位交代と同じ。注目校でなくてもよい)。区間記録を出した区間では、同じチームの区間賞は出さない(記録が区間賞を含む)。
    /// <b>注目校</b> = 自校 と、その中継所の前後どちらかで上位 <see cref="RaceConstants.HighlightFocusTop"/> 校に入っていたチーム(20 校の全チームの動きを並べると、差の変化・失速だけで区間ごとの枠が埋まる)。
    /// 重要度は上の順。同じ重要度の中では 自校 → (記録は区間賞より先)→ 大きさ → 種類の宣言順 → 区間の若い方 → チーム ID の小さい方。
    /// ダイジェストは区間ごとに上位 <see cref="RaceConstants.HighlightPerSection"/> 件、大会の見どころは全体の上位 <see cref="RaceConstants.HighlightResultMax"/> 件
    /// (同じ種類は <see cref="RaceConstants.HighlightPerKind"/> 件まで。時間順に並べる)。
    /// </summary>
    public static class RaceHighlights
    {
        private static bool IsRecord(HighlightKind k) { return k == HighlightKind.CourseRecord || k == HighlightKind.TeamRecord; }

        /// <summary>重要度の比較。小さいほど先(より重要)。</summary>
        public static int Compare(Highlight a, Highlight b)
        {
            int c = a.Tier.CompareTo(b.Tier);
            if (c != 0) return c;
            c = b.IsOwn.CompareTo(a.IsOwn);
            if (c != 0) return c;
            c = IsRecord(b.Kind).CompareTo(IsRecord(a.Kind));               // 同じ重要度の中では、記録が先
            if (c != 0) return c;
            c = b.Magnitude.CompareTo(a.Magnitude);
            if (c != 0) return c;
            c = a.Kind.CompareTo(b.Kind);
            if (c != 0) return c;
            c = a.Section.CompareTo(b.Section);
            if (c != 0) return c;
            return a.TeamId.CompareTo(b.TeamId);
        }

        /// <summary>区間 0 〜 <paramref name="through"/> − 1 の候補すべてを、区間の順・同じ区間では重要度の順に返す。<paramref name="through"/> が負なら全区間。<paramref name="ownId"/> は自校(なければ −1)。
        /// <paramref name="records"/> はこの大会で更新された記録(なければ null)。</summary>
        public static List<Highlight> Detect(IReadOnlyList<TeamResult> results, int ownId, int through = -1, IReadOnlyList<RecordUpdate> records = null)
        {
            var cand = new List<Highlight>();
            if (results == null || results.Count == 0) return cand;
            int n = results[0].Cum.Length;
            if (through < 0 || through > n) through = n;
            int m = results.Count;
            var leader = new int[through];                       // 中継所の首位の添字
            var minCum = new int[through];
            var rank = new int[m, Math.Max(1, through)];         // 中継所の順位(1 始まり。累積 → チーム ID)
            for (int s = 0; s < through; s++)
            {
                var order = Enumerable.Range(0, m).ToList();
                RaceSimulator.StableSort(order, (x, y) => results[x].Cum[s] != results[y].Cum[s] ? results[x].Cum[s].CompareTo(results[y].Cum[s]) : results[x].Id.CompareTo(results[y].Id));
                leader[s] = order[0];
                minCum[s] = results[order[0]].Cum[s];
                for (int i = 0; i < m; i++) rank[order[i], s] = i + 1;
            }
            for (int s = 0; s < through; s++)
            {
                if (s > 0 && leader[s] != leader[s - 1])
                {
                    TeamResult now = results[leader[s]], prev = results[leader[s - 1]];
                    cand.Add(Make(s, HighlightKind.LeadChange, now.Id, ownId, prev.Cum[s] - now.Cum[s], 0, prev.Id));
                }
                int bestSec = int.MaxValue, second = int.MaxValue;
                foreach (TeamResult t in results)
                {
                    if (t.Sections[s] < bestSec) { second = bestSec; bestSec = t.Sections[s]; }
                    else if (t.Sections[s] > bestSec && t.Sections[s] < second) second = t.Sections[s];
                }
                Highlight bestOtherGap = null;                    // 自校のほかの『差の変化』は、動きの最も大きい 1 校だけ
                for (int i = 0; i < m; i++)
                {
                    TeamResult t = results[i];
                    bool focus = t.Id == ownId || rank[i, s] <= RaceConstants.HighlightFocusTop || (s > 0 && rank[i, s - 1] <= RaceConstants.HighlightFocusTop);
                    if (focus)
                    {
                        int gap = t.Cum[s] - minCum[s];
                        int gapPrev = s > 0 ? t.Cum[s - 1] - minCum[s - 1] : 0;
                        int move = gap - gapPrev;
                        if (i != leader[s] && Math.Abs(move) >= RaceConstants.HighlightGapDs)
                        {
                            Highlight g = Make(s, HighlightKind.GapMove, t.Id, ownId, Math.Abs(move), move);
                            if (g.IsOwn) cand.Add(g);
                            else if (bestOtherGap == null || Compare(g, bestOtherGap) < 0) bestOtherGap = g;
                        }
                        foreach (RaceEvent e in t.Events)
                        {
                            if (e.Section != s) continue;
                            if (e.Kind == "brake") cand.Add(Make(s, HighlightKind.Brake, t.Id, ownId, e.Value, 0));
                            else if (e.Kind == "stall") cand.Add(Make(s, HighlightKind.Stall, t.Id, ownId, e.Value, 0));
                        }
                        if (t.Boost != null && t.Boost[s]) cand.Add(Make(s, HighlightKind.Boost, t.Id, ownId, -RaceConstants.BoostAdj, 0));
                        if (s > 0)
                        {
                            int up = rank[i, s - 1] - rank[i, s];
                            if (Math.Abs(up) >= RaceConstants.HighlightRankMove) cand.Add(Make(s, HighlightKind.RankMove, t.Id, ownId, Math.Abs(up), up));
                        }
                    }
                    if (t.Sections[s] == bestSec && !HasCourseRecord(records, s, t.Id)) cand.Add(Make(s, HighlightKind.SectionPrize, t.Id, ownId, second == int.MaxValue ? 0 : second - bestSec, 0));
                }
                if (bestOtherGap != null) cand.Add(bestOtherGap);
            }
            if (records != null)
                foreach (RecordUpdate u in records)
                {
                    int section = u.Section >= 0 ? u.Section : n - 1;            // 大会記録は最終区間の中継所(ゴール)で
                    if (section >= through) continue;
                    cand.Add(Make(section, u.Section >= 0 ? HighlightKind.CourseRecord : HighlightKind.TeamRecord, u.SchoolId, ownId, u.PrevTimeDs - u.TimeDs, 0, u.PrevSchoolId));
                }
            RaceSimulator.StableSort(cand, (a, b) => a.Section != b.Section ? a.Section.CompareTo(b.Section) : Compare(a, b));
            return cand;
        }

        private static bool HasCourseRecord(IReadOnlyList<RecordUpdate> records, int section, int teamId)
        {
            if (records == null) return false;
            foreach (RecordUpdate u in records) if (u.Section == section && u.SchoolId == teamId) return true;
            return false;
        }

        private static Highlight Make(int section, HighlightKind kind, int teamId, int ownId, double magnitude, int delta, int other = -1)
        {
            return new Highlight { Section = section, Kind = kind, TeamId = teamId, IsOwn = teamId == ownId, Magnitude = magnitude, Delta = delta, OtherTeamId = other };
        }

        /// <summary>ダイジェスト: 区間ごとに重要度の高いものから最大 <see cref="RaceConstants.HighlightPerSection"/> 件(<see cref="Detect"/> の結果から)。区間の順・同じ区間では重要度の順。</summary>
        public static List<Highlight> Digest(IReadOnlyList<Highlight> detected)
        {
            var list = new List<Highlight>();
            int section = -1, taken = 0;
            foreach (Highlight h in detected)
            {
                if (h.Section != section) { section = h.Section; taken = 0; }
                if (taken < RaceConstants.HighlightPerSection) { list.Add(h); taken++; }
            }
            return list;
        }

        /// <summary>大会の見どころ: 全体で重要度の高い上位 <paramref name="max"/> 件(同じ種類は <see cref="RaceConstants.HighlightPerKind"/> 件まで)を、区間の順(同じ区間では重要度の順)に並べて返す。</summary>
        public static List<Highlight> Top(IReadOnlyList<Highlight> detected, int max = RaceConstants.HighlightResultMax)
        {
            var byPriority = new List<Highlight>(detected);
            RaceSimulator.StableSort(byPriority, Compare);
            var picked = new List<Highlight>();
            var perKind = new Dictionary<HighlightKind, int>();
            foreach (Highlight h in byPriority)
            {
                if (picked.Count >= max) break;
                perKind.TryGetValue(h.Kind, out int c);
                if (c >= RaceConstants.HighlightPerKind) continue;
                perKind[h.Kind] = c + 1;
                picked.Add(h);
            }
            RaceSimulator.StableSort(picked, (a, b) => a.Section != b.Section ? a.Section.CompareTo(b.Section) : Compare(a, b));
            return picked;
        }
    }
}
