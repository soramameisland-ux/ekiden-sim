using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;

namespace Ekiden.Domain.Events
{
    /// <summary>イベント定義の検証(06 §13 V-01〜V-10)。違反の文言のリストを返す(空なら適合)。文言は参照モデルと同じ。</summary>
    public static class EventValidator
    {
        public static List<string> Validate(IEnumerable<EventDef> events)
        {
            var errs = new List<string>();
            var ids = new HashSet<string>();
            foreach (EventDef e in events)
            {
                int roles = e.Roles;
                string eid = e.Id;
                if (!ids.Add(eid)) errs.Add("V-01 " + eid + ": IDの重複");
                CheckExpr(errs, "V-02 " + eid, e.Cond, roles);
                if (e.Choices.Length == 0) errs.Add("V-03 " + eid + ": 選択肢がない");
                var cids = new HashSet<string>();
                bool dup = false;
                foreach (ChoiceDef c in e.Choices) if (!cids.Add(c.Id)) dup = true;
                if (dup) errs.Add("V-03 " + eid + ": 選択肢IDの重複");
                bool badDefault = e.Default.Length == 0;
                foreach (string d in e.Default) if (!cids.Contains(d)) badDefault = true;
                if (badDefault) errs.Add("V-04 " + eid + ": 既定の選択肢が不正");
                else
                {
                    string last = e.Default[e.Default.Length - 1];
                    foreach (ChoiceDef c in e.Choices)
                        if (c.Id == last && !string.IsNullOrEmpty(c.Req)) errs.Add("V-04 " + eid + ": 既定の最後の選択肢に条件がある(必ず選べる選択肢が要る)");
                }
                if (!(0.0 < e.P && e.P <= 1.0)) errs.Add("V-05 " + eid + ": 発生確率が範囲外");
                foreach (ChoiceDef c in e.Choices)
                {
                    string at = eid + "/" + c.Id;
                    if (!string.IsNullOrEmpty(c.Req)) CheckExpr(errs, "V-02 " + at, c.Req, roles);
                    var xpTotal = new Dictionary<Target, int>();
                    foreach (Effect fx in c.Fx)
                    {
                        if (fx.To == Target.P && e.Scope != EventScope.Pair) errs.Add("V-06 " + at + ": pair でないイベントで p を使っている");
                        if (fx.Op == EffectOp.Bond && e.Scope != EventScope.Pair) errs.Add("V-06 " + at + ": bond は pair のイベントだけ");
                        if (fx.Op == EffectOp.Xp)
                        {
                            if (fx.Xu > EventConstants.K_XP_MAX) errs.Add("V-07 " + at + ": XP が上限を超える");
                            int t;
                            xpTotal.TryGetValue(fx.To, out t);
                            xpTotal[fx.To] = t + fx.Xu;
                        }
                        if (fx.Op == EffectOp.Mot && Math.Abs(fx.D) > 1) errs.Add("V-07 " + at + ": やる気の変化は ±1 まで");
                        if (fx.Op == EffectOp.Fat && Math.Abs(fx.D) > 20) errs.Add("V-07 " + at + ": 疲労の変化は ±20 まで");
                        if (fx.Op == EffectOp.Funds && !(EventConstants.K_FUNDS_MIN <= fx.D && fx.D <= EventConstants.K_FUNDS_MAX)) errs.Add("V-07 " + at + ": 資金の変化が範囲外");
                        if (fx.Op == EffectOp.Mod)
                        {
                            double lo, hi;
                            EventConstants.ModCap(fx.Kind, out lo, out hi);
                            if (!(lo <= fx.Value && fx.Value <= hi)) errs.Add("V-08 " + at + ": 補正の値が範囲外");
                            if (fx.Weeks > EventConstants.K_MOD_WEEKS_MAX) errs.Add("V-08 " + at + ": 補正の期間が長すぎる");
                        }
                    }
                    foreach (int v in xpTotal.Values)
                        if (v > 2 * EventConstants.K_XP_MAX) { errs.Add("V-07 " + at + ": 1つの選択肢の XP 合計が多すぎる"); break; }
                }
                if (e.Scope == EventScope.Pair && e.Once == OnceKind.Runner) errs.Add("V-09 " + eid + ": pair に once=runner は使えない");
                if (e.Phase == EventPhaseKind.Race && e.Months != null && e.Months.Length > 0) errs.Add("V-10 " + eid + ": race フェーズのイベントに月の指定は不要");
            }
            return errs;
        }

        private static void CheckExpr(List<string> errs, string prefix, string src, int roles)
        {
            if (string.IsNullOrEmpty(src)) return;
            CompiledExpr ex;
            string err;
            if (!DslCompiler.TryCompile(src, roles, out ex, out err)) errs.Add(prefix + ": " + err);
        }
    }
}
