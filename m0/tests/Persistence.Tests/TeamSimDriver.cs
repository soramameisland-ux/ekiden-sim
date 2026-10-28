using System;
using System.Collections.Generic;
using System.Linq;
using Calendar = Ekiden.Domain.Team.Calendar;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Numerics;
using Ekiden.Domain.Recruit;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Team;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;

namespace Ekiden.Persistence.Tests
{
    /// <summary>試験用の自校の週ループ(M1 の本物のループの前の、保存の検査のための最小版)。新規ゲーム(40 人)→ 週ごとに 暦の更新・補正の期限・派生値・イベント・週の解決 → 大会週の疲労と成績 → 年度末の卒業(OB)と入学。
    /// 規則は 03・06 の関数をそのまま呼ぶ。乱数は鍵つき(label 4 を大会の結果に流用)。ここで作る状態は、保存の往復と『保存 → 読み込み → 続行』のバイト一致の入力になる。</summary>
    internal sealed class TeamSimDriver
    {
        private readonly EventCatalog _cat = EventCatalogData.Create();
        private readonly TrainingMaster _m = TrainingMaster.CreateDefault();
        private readonly WeekResolver _res;

        public TeamSimDriver() { _res = new WeekResolver(_m); }

        private static readonly string[] SkillPool = { "skill.hold_lead", "skill.pack_runner", "skill.big_stage", "skill.iron_legs", "skill.summer_boy" };
        private static readonly string[] TraitPool = { "Serious", "Competitive", "MyPace", "Passionate", "Careful", "Cheerful", "Quiet", "Caring" };

        public TeamState NewGame(ulong seed, int schoolPref = 13)
        {
            var s = new TeamState { Seed = seed, SchoolPref = schoolPref, Funds = 1500, Prestige = 300, Morale = 60, CoachRank = 1, Fac = FacilityLevels.AllTwo };
            s.Scenario = new ScenarioRef { Id = "sc.mid", Difficulty = "Mid", PlayerSchoolId = 0 };
            for (int i = 0; i < 4; i++) s.StaffNames[i] = "スタッフ" + (i + 1);
            s.NextStaffId = 5;
            s.Tickets.Earn(TicketMilestones.WinFall, 0);                         // 券を 2 枚持って始める(招聘の宣言 → 入学を通す)
            s.Tickets.Earn(TicketMilestones.WinNational, 0);
            var rngs = new KeyedRngFactory(seed);
            for (int g = 1; g <= 4; g++)
                for (int k = 0; k < 10; k++) AddRunner(s, rngs, g, (1 - g) * Calendar.WeeksPerYear);
            return s;
        }

        private void AddRunner(TeamState s, IRngFactory rngs, int grade, int enrollWeek)
        {
            int id = s.NextRunnerId++;
            Pcg32 g = rngs.Stream(7, 99, id);
            var r = new Runner { Id = id, Grade = grade, EnrollWeek = enrollWeek, Fatigue = g.RandInt(5, 40), Motivation = g.RandInt(2, 5), GrowthType = (GrowthType)g.RandInt(0, 2), LookSeed = g.U32() };
            for (int i = 0; i < StatInfo.Count; i++)
            {
                r.Stats[i] = Math.Max(30, Math.Min(80, (int)Math.Round(g.Normal(46 + 4 * grade, 5))));
                r.Potential[i] = Math.Min(100, r.Stats[i] + g.RandInt(5, 30));
            }
            Style style = Individuality.GenerateStyle(rngs, r.LookSeed, Profile.Balanced);
            Gift[] gifts = Individuality.GenerateGifts(rngs, r.LookSeed);
            Individuality.AddGiftPotential(r.Potential, gifts);
            r.SetIndividuality(style, gifts);
            r.GiftRevealed = g.Uniform() < 0.3;
            if (g.Uniform() < 0.25) r.Skills.Add(SkillPool[g.RandInt(0, SkillPool.Length - 1)]);
            if (g.Uniform() < 0.03) { r.Overseas = true; r.Country = g.RandInt(1, 24); }
            else { r.Pref = g.RandInt(1, 47); r.HighSchool = g.RandInt(1, 216); }
            r.DeriveDistance(s.SchoolPref);
            r.FamilyName = "姓" + id; r.GivenName = "名" + id; r.FamilyKana = "せい" + id; r.GivenKana = "めい" + id;
            r.Look = new Appearance { HairStyle = g.RandInt(0, 9), HairColor = g.RandInt(0, 11), Face = g.RandInt(0, 4), Body = g.RandInt(0, 2), SkinTone = g.RandInt(0, 5) };
            if (g.Uniform() < 0.1) r.LookOverride = new AppearanceOverride { HairColor = g.RandInt(0, 11) };
            for (int t = g.RandInt(1, 2); t > 0; t--) { string tr = TraitPool[g.RandInt(0, TraitPool.Length - 1)]; if (!r.Traits.Contains(tr)) r.Traits.Add(tr); }
            s.Runners.Add(r);
            s.InitBonds(r);
        }

        private WeekMods ModsFor(TeamState s, Runner r)
        {
            var wm = WeekMods.None;
            foreach (TimedModifier m in s.Mods)
            {
                if (m.Until <= s.AbsWeek || (m.To != TimedModifier.TeamTarget && m.To != r.Id)) continue;
                switch (m.Kind)
                {
                    case ModKind.EMult: wm.EMult *= m.Value; break;
                    case ModKind.RAdd: wm.RAdd += m.Value; break;
                    case ModKind.InjMult: wm.InjMult *= m.Value; break;
                    case ModKind.FormAdd: wm.FormAdd += m.Value; break;
                }
            }
            return wm;
        }

        /// <summary>1 週を進める。呼ぶのは週の境目(保存はこの前後で行う)。</summary>
        public void Step(TeamState s)
        {
            var rngs = new KeyedRngFactory(s.Seed);
            int yw = Calendar.FloorMod(s.AbsWeek, Calendar.WeeksPerYear);
            int month = yw / 4;
            int year = Calendar.FloorDiv(s.AbsWeek, Calendar.WeeksPerYear) + 1;
            if (yw == 0) s.RollYear();
            if (yw % 4 == 0)
            {
                s.GuidanceLeft = 4;
                s.Talked.Clear();
                s.IndividualMenus.Clear();
                string theme = PlanRules.AutoPlan(s.Plan.Focus, month);
                for (int g = 0; g < 3; g++) s.MonthlyPlan[g] = theme;
                s.ScoutPts += month >= 2 && month <= 10 ? 4 : 0;
            }
            s.ExpireTimed();
            s.Refresh();
            var phase = new EventPhase(_cat, rngs, new ApproxEventWorld(s.Seed));
            phase.Run(s, null, new DefaultChoicePolicy());
            foreach (Runner r in s.Runners) if (r.IsInjured) s.IndividualMenus.Remove(r.Id);     // 故障した選手の指定は破棄(I-20)
            // 個別指導の例: 月の最初の週に、力の最も低い健康な選手 1 人に調整メニューを指定する(月末まで)
            if (yw % 4 == 0 && s.GuidanceLeft > 0)
            {
                Runner t = s.Runners.Where(x => !x.IsInjured).OrderBy(x => TeamState.LongP(x)).ThenBy(x => x.Id).FirstOrDefault();
                if (t != null)
                {
                    s.IndividualMenus[t.Id] = new IndividualMenu { UntilAbsWeek = s.AbsWeek + 4, Menu = new MenuSpec("ks.sharpen", "ks.pace_run", "mil.low") };
                    s.GuidanceLeft--;
                }
            }
            // 面談(1 人 月 1 回。指導ポイント 1): 月の 2 週目に、やる気の最も低い健康な選手と話す
            if (yw % 4 == 1 && s.GuidanceLeft > 0)
            {
                Runner t = s.Runners.Where(x => !x.IsInjured && !s.Talked.Contains(x.Id)).OrderBy(x => x.Motivation).ThenBy(x => x.Id).FirstOrDefault();
                if (t != null) { s.Talked.Add(t.Id); s.GuidanceLeft--; }
            }
            // 次の大会の欠場・第三戦の追加登録(04 §4・§5.3): 大会の前の週に指定し、大会のあとで消す
            if (yw == 21) s.Bench.Add(s.Runners.OrderByDescending(x => x.PRank).First().Id);
            if (yw == 32) s.Forced.Add(s.Runners.OrderByDescending(x => x.PRank).Skip(1).First().Id);
            foreach (Runner r in s.Runners)
            {
                string theme = s.MonthlyPlan[r.Group];
                string[] plan12 = Enumerable.Repeat(theme, 12).ToArray();
                bool camp;
                Menu menu = YearPlanner.WeekMenu(_m, yw, plan12, YearPlanner.Races, true, out camp);
                bool individual = false;
                IndividualMenu im;
                if (s.IndividualMenus.TryGetValue(r.Id, out im) && !r.IsInjured)
                {
                    menu = new Menu(im.Menu.Key1, im.Menu.Key2, im.Menu.Mileage);
                    individual = true;
                }
                WeekReport rep = _res.Resolve(r, menu, s.Fac, month, rngs, r.Id, s.AbsWeek, camp: camp, safety: true, individual: individual, bondAvg: r.BondAvg, mods: ModsFor(s, r));
                if (rep.NewInjury.HasValue)
                {
                    r.InjuryAge = 0;
                    r.AddCtr(Counters.Injuries, 1);
                    r.Counters[Counters.InjuryFree] = 0;
                    s.IndividualMenus.Remove(r.Id);                                           // 故障した選手の指定は破棄(I-20)
                }
                else
                {
                    if (r.InjuryAge < 99) r.InjuryAge++;
                    if (!r.IsInjured) r.AddCtr(Counters.InjuryFree, 1);
                }
                if (menu.Key1 == "ks.hill" || menu.Key2 == "ks.hill") r.AddCtr(Counters.HillWeeks, 1);
            }
            // 大会週: 疲労と成績
            double km;
            if (YearPlanner.Races.TryGetValue(yw, out km))
            {
                Pcg32 g = rngs.Stream(RngRegistry.LabelRace, s.AbsWeek, 777);
                int k = Array.IndexOf(Calendar.RaceWeeks, yw);
                s.Rank[k] = g.RandInt(1, 12);
                s.YearPoints += Math.Max(0, 12 - s.Rank[k]);
                s.Prestige = Q6.Round(Math.Max(0, Math.Min(1000, s.Prestige + (7 - s.Rank[k]) * 3.5)));
                foreach (Runner r in s.Runners)
                {
                    r.Fatigue = Math.Min(100, r.Fatigue + Rounding.HalfUp(km * TrainingMaster.K_RACE_FATIGUE_PER_KM));
                    if (r.Regular) { r.Races++; if (g.Uniform() < 0.1) r.SectionWins++; if (s.Rank[k] == 1) r.RelayWins++; }
                }
            }
            if (yw == 25) s.Bench.Clear();
            if (yw == 36) s.Forced.Clear();
            if (yw == 20 && s.Tickets.CanDeclare(year, month, s.Runners.Count(x => x.RecruitSource == RecruitSource.Ticket)))
                s.Tickets.Declare(year, year % 2 == 0 ? TicketKind.Intl : TicketKind.Rookie);       // 6 月ごろ宣言 → 年度末に入学
            if (yw == Calendar.WeeksPerYear - 1) YearEnd(s, rngs, year);
            s.AbsWeek++;
        }

        /// <summary>年度末(最後の週の処理のあと): 卒業(OB に)→ 学年を上げる → 招聘選手と新入生の入学 → 収支・券の節目。</summary>
        private void YearEnd(TeamState s, IRngFactory rngs, int year)
        {
            Pcg32 g = rngs.Stream(RngRegistry.LabelRace, s.AbsWeek, 888);
            foreach (Runner r in s.Runners.Where(x => x.Grade == 4).ToList())
            {
                s.Obs.Add(OBRecord.FromGraduate(r, year, year - 3, (CareerPath)g.RandInt(0, 3), "高校" + r.HighSchool, g.Uniform() < 0.05));
                s.Runners.Remove(r);
                s.IndividualMenus.Remove(r.Id); s.Talked.Remove(r.Id); s.Bench.Remove(r.Id); s.Forced.Remove(r.Id);
            }
            s.Bonds.RemoveAllNotIn(new HashSet<int>(s.Runners.Select(x => x.Id)));
            foreach (Runner r in s.Runners) r.Grade++;
            int nextEnroll = (Calendar.FloorDiv(s.AbsWeek, Calendar.WeeksPerYear) + 1) * Calendar.WeeksPerYear;
            if (s.Tickets.Pending != null)
            {
                PendingInvite p = s.Tickets.Pending;
                SpecialRecruit sr = SpecialRecruitGenerator.Generate(rngs, p.EnrollYear, p.Kind, p.Pref);
                Runner t = SpecialRecruitGenerator.ToRunner(sr, s.NextRunnerId++);
                t.EnrollWeek = nextEnroll;
                t.LookSeed = sr.LookSeed;
                t.FamilyName = "招聘" + t.Id; t.GivenName = "名" + t.Id; t.FamilyKana = "しょうへい"; t.GivenKana = "めい";
                if (p.Kind == TicketKind.Intl) t.Country = sr.CountryIndex + 1; else t.HighSchool = 1 + (int)(sr.HighSchoolU * 216);
                t.DeriveDistance(s.SchoolPref);
                s.Runners.Add(t);
                s.InitBonds(t);
                s.Tickets.ConfirmEnrollment();
            }
            for (int i = 0; i < 10; i++) AddRunner(s, rngs, 1, nextEnroll);
            s.Funds = Math.Max(0, s.Funds + 800 + s.YearPoints * 25 - 15 * s.Runners.Count);
            TicketMilestones.EarnAtYearEnd(s.Tickets, year, s.CoachRank, year);
            if (year % 4 == 0) s.CoachRank++;
        }
    }
}
