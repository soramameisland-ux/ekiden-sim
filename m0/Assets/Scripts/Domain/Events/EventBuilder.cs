using System;
using Ekiden.Domain.Entities;

namespace Ekiden.Domain.Events
{
    /// <summary>イベントのカタログ(EventCatalog.g.cs。verify/gen_events_cs.py が events_06.py から作る)を書き下すための短い記法。</summary>
    internal static class B
    {
        public static Effect Mot(Target to, int d, double? p = null) { return new Effect { Op = EffectOp.Mot, To = to, D = d, P = p }; }

        public static Effect Fat(Target to, int d, double? p = null) { return new Effect { Op = EffectOp.Fat, To = to, D = d, P = p }; }

        /// <param name="stat">能力名(SPD〜DUR)・weakest・random。</param>
        public static Effect Xp(Target to, string stat, int xu, double? p = null)
        {
            var fx = new Effect { Op = EffectOp.Xp, To = to, Xu = xu, P = p };
            if (stat == "weakest") fx.StatMode = XpStatMode.Weakest;
            else if (stat == "random") fx.StatMode = XpStatMode.Random;
            else fx.Stat = (int)(Stat)Enum.Parse(typeof(Stat), stat);
            return fx;
        }

        public static Effect Bond(int d) { return new Effect { Op = EffectOp.Bond, To = Target.R, D = d }; }

        public static Effect Bondg(Target to, int d) { return new Effect { Op = EffectOp.Bondg, To = to, D = d }; }

        public static Effect Inj(Target to, InjurySeverityKind sev) { return new Effect { Op = EffectOp.Inj, To = to, Sev = sev }; }

        public static Effect Skill(Target to, string id, double? p = null) { return new Effect { Op = EffectOp.Skill, To = to, SkillId = id, P = p }; }

        public static Effect Funds(int d) { return new Effect { Op = EffectOp.Funds, D = d }; }

        public static Effect Scout(int d) { return new Effect { Op = EffectOp.Scout, D = d }; }

        public static Effect Guide(int d) { return new Effect { Op = EffectOp.Guide, D = d }; }

        public static Effect Mod(ModKind kind, Target to, double value, int weeks) { return new Effect { Op = EffectOp.Mod, Kind = kind, To = to, Value = value, Weeks = weeks }; }

        public static Effect Captain(CaptainSelect select) { return new Effect { Op = EffectOp.Captain, Select = select }; }

        public static Effect Bench() { return new Effect { Op = EffectOp.Bench, To = Target.R }; }

        public static Effect Entry() { return new Effect { Op = EffectOp.Entry, To = Target.R }; }

        public static Effect ObRole(ObRole role, int weeks) { return new Effect { Op = EffectOp.ObRole, Role = role, Weeks = weeks }; }

        public static ChoiceDef C(string id, string label, Effect[] fx = null, string req = null)
        {
            return new ChoiceDef { Id = id, Label = label, Req = req, Fx = fx ?? new Effect[0] };
        }

        public static EventDef E(string id, EventCategory cat, string name, EventScope scope, string cond, double p, ChoiceDef[] choices, string[] def,
            int[] months = null, int[] weeks = null, EventPhaseKind phase = EventPhaseKind.Normal, int cooldown = 0, OnceKind once = OnceKind.None,
            int? maxYear = null, int priority = 50, bool exempt = false, string hint = "")
        {
            return new EventDef
            {
                Id = id, Category = cat, Name = name, Scope = scope, Cond = cond, P = p, Choices = choices, Default = def, Months = months, Weeks = weeks,
                Phase = phase, Cooldown = cooldown, Once = once, MaxYear = maxYear, Priority = priority, Exempt = exempt, Hint = hint,
            };
        }
    }
}
