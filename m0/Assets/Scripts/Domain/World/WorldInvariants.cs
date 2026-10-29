using System;
using System.Collections.Generic;
using Ekiden.Domain.Tickets;

namespace Ekiden.Domain.World
{
    /// <summary>世界の状態の不変条件(02 §5 の世界に関する部分 + 05 の範囲)。年度の切れ目で確かめる。M0 の 300 年度の安定(09 §16.3・§17.3)の『不変条件の違反なし』の中身。違反の説明を返す(なければ空)。</summary>
    public static class WorldInvariants
    {
        /// <summary>名声ポイントの履歴の上限(L-45): 履歴は 0.6 × 履歴 + 0.4 × その年度の名声ポイントで動く。1 年度に取れる名声ポイントの最大は、3 大会とも優勝(3 × (K_FAME_ENTRY + K_FAME_TOP))+ 三冠の 30 = 67.5。
        /// 毎年それを取り続けた極限が 67.5(02 §4.3 の『0〜40』はシナリオの上書きの入力の範囲で、動きの範囲ではない)。</summary>
        public const double FameHistMax = 3 * (WorldK.K_FAME_ENTRY + WorldK.K_FAME_TOP) + 30.0;

        public static List<string> Check(WorldSim w, PlayerAgent agent)
        {
            var errs = new List<string>();
            if (w.Schools.Count != WorldSim.SchoolCount) errs.Add("W-1 学校の数 " + w.Schools.Count);
            int players = 0;
            for (int i = 0; i < w.Schools.Count; i++)
            {
                School s = w.Schools[i];
                string t = "学校" + s.Id + " ";
                if (s.Id != i) errs.Add("W-1 " + t + "ID が並び順と違う");
                if (s.IsPlayer) players++;
                if (!(s.Prestige >= 0 && s.Prestige <= 1000)) errs.Add("W-2 " + t + "名声 " + s.Prestige);
                if (!(s.Hist >= 0 && s.Hist <= FameHistMax)) errs.Add("W-2 " + t + "名声ポイントの履歴 " + s.Hist);
                if (!s.IsPlayer)
                {
                    if (!(s.Base >= WorldK.BaseMin && s.Base <= WorldK.BaseMax)) errs.Add("W-2 " + t + "基準名声 " + s.Base);
                    if (!(Math.Abs(s.CoachQ) <= WorldK.CoachMax)) errs.Add("W-2 " + t + "指導体制 " + s.CoachQ);
                }
                if (s.Roster.Count > WorldK.RosterMax) errs.Add("W-3 " + t + "名簿 " + s.Roster.Count + " 人(上限 " + WorldK.RosterMax + ")");
                var perGrade = new int[5];
                foreach (WorldRunner r in s.Roster)
                {
                    if (r.Grade < 1 || r.Grade > 4) { errs.Add("W-3 " + t + "学年 " + r.Grade); continue; }
                    perGrade[r.Grade]++;
                    if (r.SchoolId != s.Id) errs.Add("W-3 " + t + "選手の所属 " + r.SchoolId);
                    if (!(r.A0 >= WorldK.PoolAMin && r.A0 <= WorldK.PoolAMax)) errs.Add("W-4 " + t + "A0 " + r.A0);
                    if (!(r.Gap >= WorldK.PoolGapMin && r.Gap <= WorldK.PoolGapMax) && !r.Ticket) errs.Add("W-4 " + t + "gap " + r.Gap);
                    if (!(r.Prog >= 0 && r.Prog <= 0.95)) errs.Add("W-4 " + t + "prog " + r.Prog);
                    if (!r.Overseas && (r.PrefectureId < 1 || r.PrefectureId > 47) && !r.Ticket) errs.Add("W-4 " + t + "出身県 " + r.PrefectureId);
                }
                for (int g = 1; g <= 4; g++) if (perGrade[g] > WorldK.QuotaNormal) errs.Add("W-3 " + t + g + " 年生 " + perGrade[g] + " 人(定員 " + WorldK.QuotaNormal + ")");
                if (errs.Count > 40) { errs.Add("…(以下省略)"); return errs; }
            }
            if (players > 1) errs.Add("W-1 自校が " + players + " 校");
            if (agent != null)
            {
                School p = agent.School;
                if (!p.IsPlayer) errs.Add("W-5 自校の旗");
                if (!(p.Funds >= 0)) errs.Add("W-5 資金 " + p.Funds);
                int[] lv = { p.Fac.Coach, p.Fac.Trainer, p.Fac.Nutri, p.Fac.Dorm, p.Fac.Track, p.Fac.Hill, p.Fac.Gym, p.Fac.Medical, p.Fac.Scout };
                foreach (int l in lv) if (l < 1 || l > Training.TrainingMaster.FacMax) errs.Add("W-5 施設レベル " + l);
                if (p.ScoutLv != p.Fac.Scout) errs.Add("W-5 スカウトのレベル");
                foreach (KeyValuePair<int, int> kv in p.Ob) if (kv.Value < 0) errs.Add("W-5 OB の人数");
                int active = 0;
                foreach (WorldRunner r in p.Roster) if (r.Ticket) active++;
                int received = 0, used = 0;
                foreach (TicketLogEntry e in agent.Tickets.Log) { if (e.Delta > 0) received += e.Delta; else used -= e.Delta; }
                foreach (string m in agent.Tickets.CheckInvariants(active, received, used)) errs.Add("W-6 券 " + m);
                int prev = 0;
                foreach (PlayerYearStats st in agent.Stats)
                {
                    if (prev != 0 && st.Year != prev + 1) errs.Add("W-7 記録の年度が連続でない");
                    prev = st.Year;
                }
            }
            return errs;
        }
    }
}
