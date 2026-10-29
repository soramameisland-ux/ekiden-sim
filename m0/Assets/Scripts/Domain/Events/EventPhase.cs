using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Rng;
using Ekiden.Domain.Team;
using Ekiden.Domain.Training;

namespace Ekiden.Domain.Events
{
    /// <summary>その週に発生したイベント 1 件。</summary>
    public sealed class FiredEvent
    {
        public EventDef Def;
        public Runner R, P;             // team は両方 null、runner は P が null
        public ChoiceDef Choice;        // 解決したあとに入る
    }

    /// <summary>発生の選択の結果。</summary>
    public sealed class EventSelection
    {
        public readonly List<FiredEvent> Fired = new List<FiredEvent>();
        public bool CappedByWeek;       // 週の上限(K_EV_WEEK)で打ち切った
    }

    /// <summary>選択肢の選び方。プレイヤーが選ぶときは、画面の側がこれを実装する(選択肢の乱数は引かない。06 §7.5)。</summary>
    public interface IChoicePolicy
    {
        ChoiceDef Choose(EventDef e, IReadOnlyList<ChoiceDef> valid, Pcg32 stream);
    }

    /// <summary>おまかせ: default の先頭から、条件を満たす最初の選択肢(06 §7.4)。</summary>
    public sealed class DefaultChoicePolicy : IChoicePolicy
    {
        public ChoiceDef Choose(EventDef e, IReadOnlyList<ChoiceDef> valid, Pcg32 stream)
        {
            foreach (string d in e.Default)
                foreach (ChoiceDef c in valid)
                    if (c.Id == d) return c;
            return valid[0];
        }
    }

    /// <summary>検算用: 選べる選択肢を一様に選ぶ(乱数を 1 回引く)。</summary>
    public sealed class UniformChoicePolicy : IChoicePolicy
    {
        public ChoiceDef Choose(EventDef e, IReadOnlyList<ChoiceDef> valid, Pcg32 stream)
        {
            return valid[Math.Min(valid.Count - 1, (int)(stream.Uniform() * valid.Count))];
        }
    }

    /// <summary>週ごとのイベントの発生と解決(06 §7)。発生の判定は <see cref="Select"/>、選択と効果は <see cref="Resolve"/>。</summary>
    public sealed class EventPhase
    {
        private readonly EventCatalog _catalog;
        private readonly IRngFactory _rngs;
        private readonly IEventWorld _world;
        private readonly double _scale;

        /// <param name="scale">発生確率の倍率。本番は 1.0(検算でだけ変える)。</param>
        public EventPhase(EventCatalog catalog, IRngFactory rngs, IEventWorld world, double scale = 1.0)
        {
            _catalog = catalog;
            _rngs = rngs;
            _world = world;
            _scale = scale;
        }

        public EventContext NewContext(TeamState s, RaceInfo? race)
        {
            return new EventContext(s, _world, race);
        }

        /// <summary>この週に発生するイベントを決める(06 §7.2 の前半)。状態は変えない。</summary>
        public EventSelection Select(TeamState s, RaceInfo? race)
        {
            var res = new EventSelection();
            int yw = Calendar.FloorMod(s.AbsWeek, Calendar.WeeksPerYear);
            int month = yw / 4, wk = yw % 4;
            int year = Calendar.FloorDiv(s.AbsWeek, Calendar.WeeksPerYear);
            int thisMonth = year * 12 + month;
            Pcg32 stream = _rngs.Stream(RngRegistry.LabelEvent, s.AbsWeek, 0);
            var used = new HashSet<int>();                  // 今週すでに当事者になった選手(1人1週に1件まで)
            var ctx = NewContext(s, race);
            var subjects = new List<KeyValuePair<Runner, Runner>>();

            foreach (EventDef e in _catalog.ByPriority)
            {
                if (res.Fired.Count >= EventConstants.K_EV_WEEK)
                {
                    res.CappedByWeek = true;
                    break;
                }
                if (e.Phase == EventPhaseKind.Race && !race.HasValue) continue;
                if (e.Months != null && Array.IndexOf(e.Months, month) < 0) continue;
                if (e.Weeks != null && Array.IndexOf(e.Weeks, wk) < 0) continue;
                int ycount;
                s.YearCounts.TryGetValue(e.Id, out ycount);
                if (e.MaxYear.HasValue && ycount >= e.MaxYear.Value) continue;
                if (!e.Exempt && s.YearTotal >= EventConstants.K_EV_YEAR) continue;

                subjects.Clear();
                if (e.Scope == EventScope.Team)
                {
                    EventRecord rec;
                    int last = s.Fired.TryGetValue(e.Id, out rec) ? rec.Last : -9999;
                    if (s.AbsWeek - last >= e.Cooldown && (e.CondExpr == null || e.CondExpr.Test(ctx.Bind(null, null))))
                        subjects.Add(new KeyValuePair<Runner, Runner>(null, null));
                }
                else if (e.Scope == EventScope.Runner)
                {
                    foreach (Runner r in s.Runners)
                    {
                        if (Blocked(r, e, used, thisMonth)) continue;
                        if (e.Once == OnceKind.Runner && r.EventOnce.Contains(e.Id)) continue;
                        int lastR;
                        if (!r.EventCooldown.TryGetValue(e.Id, out lastR)) lastR = -9999;
                        if (s.AbsWeek - lastR < e.Cooldown) continue;
                        if (e.CondExpr.Test(ctx.Bind(r, null))) subjects.Add(new KeyValuePair<Runner, Runner>(r, null));
                    }
                }
                else
                {
                    Dictionary<long, int> cd;
                    s.PairCooldown.TryGetValue(e.Id, out cd);
                    foreach (Runner r in s.Runners)
                    {
                        if (Blocked(r, e, used, thisMonth)) continue;
                        foreach (Runner pp in s.Runners)
                        {
                            if (ReferenceEquals(pp, r) || Blocked(pp, e, used, thisMonth)) continue;
                            int lastP;
                            if (cd == null || !cd.TryGetValue(BondTable.Key(r.Id, pp.Id), out lastP)) lastP = -9999;
                            if (s.AbsWeek - lastP < e.Cooldown) continue;
                            if (e.CondExpr.Test(ctx.Bind(r, pp))) subjects.Add(new KeyValuePair<Runner, Runner>(r, pp));
                        }
                    }
                }
                if (subjects.Count == 0) continue;

                double u1 = stream.Uniform(), u2 = stream.Uniform();       // 候補のあるイベントだけ、必ず2回引く
                if (!(u1 < Math.Min(1.0, e.P * _scale))) continue;
                KeyValuePair<Runner, Runner> pick = subjects[Math.Min(subjects.Count - 1, (int)(u2 * subjects.Count))];
                res.Fired.Add(new FiredEvent { Def = e, R = pick.Key, P = pick.Value });
                if (pick.Key != null) used.Add(pick.Key.Id);
                if (pick.Value != null) used.Add(pick.Value.Id);
            }
            return res;
        }

        /// <summary>1人1ヶ月の制限(exempt でない通常のイベントと覚醒に適用)と、今週すでに当事者になった選手を除く(06 §7.3)。</summary>
        private static bool Blocked(Runner r, EventDef e, HashSet<int> used, int thisMonth)
        {
            if (used.Contains(r.Id)) return true;
            return (!e.Exempt || e.Category == EventCategory.Awakening) && r.LastEventMonth == thisMonth;
        }

        /// <summary>いま選べる選択肢(req を満たすもの)を定義の順に返す。</summary>
        public List<ChoiceDef> ValidChoices(TeamState s, FiredEvent f, RaceInfo? race)
        {
            EventContext ctx = NewContext(s, race).Bind(f.R, f.P);
            var valid = new List<ChoiceDef>();
            foreach (ChoiceDef c in f.Def.Choices)
                if (c.ReqExpr == null || c.ReqExpr.Test(ctx)) valid.Add(c);
            return valid;
        }

        /// <summary>k 番目に発生したイベントを解決する(06 §7.2 の後半): 選択 → 効果 → 記録。</summary>
        public void Resolve(TeamState s, FiredEvent f, int k, RaceInfo? race, IChoicePolicy policy)
        {
            Pcg32 estream = _rngs.Stream(RngRegistry.LabelEvent, s.AbsWeek, 1000 + k);
            List<ChoiceDef> valid = ValidChoices(s, f, race);
            f.Choice = policy.Choose(f.Def, valid, estream);
            EffectApplier.Apply(s, _catalog, f.Def, f.Choice, f.R, f.P, estream);
            Record(s, f);
            s.LogEvent(f.Def.Id, s.AbsWeek, f.Choice.Id);
        }

        private static void Record(TeamState s, FiredEvent f)
        {
            EventDef e = f.Def;
            int yw = Calendar.FloorMod(s.AbsWeek, Calendar.WeeksPerYear);
            int thisMonth = Calendar.FloorDiv(s.AbsWeek, Calendar.WeeksPerYear) * 12 + yw / 4;
            EventRecord rec;
            if (!s.Fired.TryGetValue(e.Id, out rec)) s.Fired[e.Id] = rec = new EventRecord();
            rec.Count++;
            rec.Last = s.AbsWeek;
            int c;
            s.YearCounts.TryGetValue(e.Id, out c);
            s.YearCounts[e.Id] = c + 1;
            if (!e.Exempt) s.YearTotal++;
            Runner[] who = { f.R, f.P };
            foreach (Runner r in who)
            {
                if (r == null) continue;
                r.LastEventMonth = thisMonth;
                r.EventCooldown[e.Id] = s.AbsWeek;
                if (e.Once == OnceKind.Runner) r.EventOnce.Add(e.Id);
            }
            if (e.Scope == EventScope.Pair)
            {
                Dictionary<long, int> cd;
                if (!s.PairCooldown.TryGetValue(e.Id, out cd)) s.PairCooldown[e.Id] = cd = new Dictionary<long, int>();
                cd[BondTable.Key(f.R.Id, f.P.Id)] = s.AbsWeek;
            }
        }

        /// <summary>発生の判定から解決までを続けて行う(おまかせ・検算用)。</summary>
        public List<FiredEvent> Run(TeamState s, RaceInfo? race, IChoicePolicy policy)
        {
            EventSelection sel = Select(s, race);
            for (int k = 0; k < sel.Fired.Count; k++) Resolve(s, sel.Fired[k], k, race, policy);
            return sel.Fired;
        }
    }

    /// <summary>効果の適用(06 §5・§8)。乱数は estream だけを、定義の順に引く。</summary>
    public static class EffectApplier
    {
        public static void Apply(TeamState s, EventCatalog catalog, EventDef ev, ChoiceDef choice, Runner r, Runner p, Pcg32 stream)
        {
            foreach (Effect fx in choice.Fx)
            {
                List<Runner> targets = null;
                if (fx.Op == EffectOp.Mot || fx.Op == EffectOp.Fat || fx.Op == EffectOp.Xp)
                {
                    if (fx.To != Target.R && fx.To != Target.P)
                    {
                        targets = Targets(s, fx.To, r, p);
                        if (fx.P.HasValue)
                        {
                            var draws = new double[targets.Count];
                            for (int i = 0; i < draws.Length; i++) draws[i] = stream.Uniform();
                            var kept = new List<Runner>();
                            for (int i = 0; i < draws.Length; i++) if (draws[i] < fx.P.Value) kept.Add(targets[i]);
                            targets = kept;
                        }
                    }
                    else
                    {
                        if (fx.P.HasValue && !(stream.Uniform() < fx.P.Value)) continue;
                        targets = Targets(s, fx.To, r, p);
                    }
                }
                switch (fx.Op)
                {
                    case EffectOp.Mot:
                        foreach (Runner x in targets) x.Motivation = Math.Max(1, Math.Min(5, x.Motivation + fx.D));
                        break;
                    case EffectOp.Fat:
                        foreach (Runner x in targets) x.Fatigue = Math.Max(0, Math.Min(100, x.Fatigue + fx.D));
                        break;
                    case EffectOp.Xp:
                        foreach (Runner x in targets)
                        {
                            int i;
                            if (fx.StatMode == XpStatMode.Weakest)
                            {
                                i = 0;
                                for (int k = 1; k < StatInfo.Count; k++) if (x.Stats[k] < x.Stats[i]) i = k;
                            }
                            else if (fx.StatMode == XpStatMode.Random) i = stream.RandInt(0, 9);
                            else i = fx.Stat;
                            Progression.ApplyXp(x, i, fx.Xu);
                        }
                        break;
                    case EffectOp.Bond:
                        s.AddBond(r, p, fx.D);
                        break;
                    case EffectOp.Bondg:
                        {
                            List<Runner> grp = Targets(s, fx.To, r, p);
                            for (int a = 0; a < grp.Count; a++)
                                for (int b = a + 1; b < grp.Count; b++) s.AddBond(grp[a], grp[b], fx.D);
                            break;
                        }
                    case EffectOp.Inj:
                        if (r.InjurySeverity == InjurySeverity.None)
                        {
                            int lo, hi;
                            InjuryDuration(fx.Sev, out lo, out hi);
                            r.InjurySeverity = (InjurySeverity)(int)fx.Sev;
                            r.InjuryWeeksLeft = stream.RandInt(lo, hi);
                            r.InjuryAge = 0;
                        }
                        break;
                    case EffectOp.Skill:
                        {
                            if (fx.P.HasValue && !(stream.Uniform() < fx.P.Value)) break;
                            string sid = EventConstants.SkillPrefix + fx.SkillId;
                            if (r.Skills.Contains(sid) || r.Skills.Count >= EventConstants.MaxSkills) break;
                            bool excluded = false;
                            foreach (string o in catalog.ExclusiveWith(fx.SkillId))
                                if (r.Skills.Contains(EventConstants.SkillPrefix + o)) excluded = true;
                            if (!excluded) r.Skills.Add(sid);
                            break;
                        }
                    case EffectOp.Funds:
                        s.Funds = Math.Max(0, s.Funds + fx.D);
                        break;
                    case EffectOp.Scout:
                        s.ScoutPts += fx.D;
                        break;
                    case EffectOp.Guide:
                        s.GuidanceLeft = Math.Max(0, s.GuidanceLeft + fx.D);
                        break;
                    case EffectOp.Mod:
                        {
                            int tgt = fx.To == Target.Team ? TimedModifier.TeamTarget : r.Id;
                            s.Mods.RemoveAll(m => m.Kind == fx.Kind && m.To == tgt && m.Src == ev.Id);
                            s.Mods.Add(new TimedModifier { Kind = fx.Kind, To = tgt, Value = fx.Value, Until = s.AbsWeek + fx.Weeks, Src = ev.Id });
                            break;
                        }
                    case EffectOp.Captain:
                        SetCaptain(s, fx.Select);
                        break;
                    case EffectOp.Bench:
                        s.Bench.Add(r.Id);
                        break;
                    case EffectOp.Entry:
                        s.Forced.Add(r.Id);
                        break;
                    case EffectOp.ObRole:
                        GiveObRole(s, ev, fx);
                        break;
                }
            }
        }

        private static void InjuryDuration(InjurySeverityKind sev, out int lo, out int hi)
        {
            switch (sev)
            {
                case InjurySeverityKind.Minor: lo = 1; hi = 2; break;
                case InjurySeverityKind.Moderate: lo = 4; hi = 8; break;
                default: lo = 12; hi = 24; break;
            }
        }

        /// <summary>対象の選手(在籍の並びの順)。same_pref は r を含み、留学生は r だけ。</summary>
        public static List<Runner> Targets(TeamState s, Target to, Runner r, Runner p)
        {
            var list = new List<Runner>();
            switch (to)
            {
                case Target.R: list.Add(r); return list;
                case Target.P: list.Add(p); return list;
                case Target.SamePref:
                    foreach (Runner x in s.Runners) if (r.Pref != 0 && x.Pref == r.Pref) list.Add(x);
                    if (list.Count == 0) list.Add(r);
                    return list;
                case Target.Team: return list;
            }
            foreach (Runner x in s.Runners)
            {
                bool hit;
                switch (to)
                {
                    case Target.All: hit = true; break;
                    case Target.G1: hit = x.Grade == 1; break;
                    case Target.G2: hit = x.Grade == 2; break;
                    case Target.G3: hit = x.Grade == 3; break;
                    case Target.G4: hit = x.Grade == 4; break;
                    case Target.Top10: hit = x.PRank <= 10; break;
                    case Target.SameGroup: hit = x.Group == r.Group; break;
                    case Target.Far: hit = x.Far; break;
                    case Target.Near: hit = x.Near; break;
                    case Target.Overseas: hit = x.Overseas; break;
                    case Target.Captain: hit = x.Captain; break;
                    default: hit = false; break;
                }
                if (hit) list.Add(x);
            }
            return list;
        }

        private static void SetCaptain(TeamState s, CaptainSelect select)
        {
            var pool = new List<Runner>();
            foreach (Runner x in s.Runners) if (x.Grade >= 3) pool.Add(x);
            if (pool.Count == 0) pool.AddRange(s.Runners);
            foreach (Runner x in s.Runners) x.Captain = false;
            if (pool.Count == 0) return;
            Runner best = null;
            double bestKey = 0;
            foreach (Runner x in pool)
            {
                double key = select == CaptainSelect.TopBond ? x.BondAvg : select == CaptainSelect.TopP ? TeamState.LongP(x) : x.Stats[(int)Stat.MEN];
                if (best == null || key > bestKey) { best = x; bestKey = key; }      // 同点は先に並んでいる方
            }
            best.Captain = true;
        }

        private static void GiveObRole(TeamState s, EventDef ev, Effect fx)
        {
            ModKind kind;
            double value;
            ObRoles.ModOf(fx.Role, out kind, out value);
            OBRecord best = null;
            foreach (OBRecord o in s.Obs)
            {
                if (o.Role != ObRole.None || !(o.Corp || fx.Role != ObRole.Coach)) continue;
                if (best == null || o.Power > best.Power) best = o;
            }
            if (best == null) return;
            best.Role = fx.Role;
            best.Until = s.AbsWeek + fx.Weeks;
            s.Mods.Add(new TimedModifier { Kind = kind, To = TimedModifier.TeamTarget, Value = value, Until = s.AbsWeek + fx.Weeks, Src = ev.Id });
        }
    }
}
