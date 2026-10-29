using System.Linq;
using Ekiden.App;
using Ekiden.Domain.Game;
using Ekiden.Domain.Views;
using Ekiden.Domain.Text;

namespace Ekiden.Unity.UI
{
    /// <summary>割り込みのダイアログ(08a G)。表示は View、決定は命令(<c>Flow.ChooseEvent</c> など)。</summary>
    public static class Dialogs
    {
        /// <summary>SC-36 イベント。選択肢(おまかせの既定に印)と効果の要約(R-15 = App の EffectText)。選ぶと続ける。</summary>
        public static void Event(ScreenRouter r, Stop s)
        {
            UiContext c = r.Context;
            EventDialogView v = c.S.EventDialog(s.Event);
            var d = new DialogSpec { Id = "dlg.event", Title = c.T(v.TitleKey) };
            if (v.Runner != null) d.Body.Add(c.T("dlg.event.who", "runner", UiContext.Name(v.Runner)) + (v.Partner != null ? " / " + UiContext.Name(v.Partner) : ""));
            if (v.Awakening && v.SkillId != null) d.Body.Add(c.T("dlg.event.awaken", "skill", c.T(v.SkillId + ".name")));
            int def = 0;
            for (int i = 0; i < v.Choices.Count; i++)
            {
                ChoiceView ch = v.Choices[i];
                string effects = EffectText.Summary(ch, c.Loc) ?? "";
                string label = c.T(ch.LabelKey) + (ch.IsDefault ? " " + c.T("dlg.event.default") : "");
                d.Body.Add("・" + label + (effects.Length > 0 ? ":" + effects : ""));
                string id = ch.Id;
                d.Button(c.T(ch.LabelKey), () => { if (c.Check(c.Host.Flow.ChooseEvent(id))) r.Continue(); }, ch.IsDefault, ch.Enabled, c.T("dlg.event.unavailable"));
                if (ch.IsDefault) def = i;
            }
            if (v.Choices.Count == 0) d.Button(c.T("ui.ok"), r.Continue, true);
            d.DefaultButton = def;
            d.Cancel = () => r.Home();
            r.OpenDialog(d);
        }

        /// <summary>SC-42 故障の知らせ。</summary>
        public static void Injury(ScreenRouter r, int runnerId)
        {
            UiContext c = r.Context;
            InjuryDialogView v = c.S.InjuryDialog(runnerId);
            var d = new DialogSpec { Id = "dlg.injury", Title = c.T("dlg.injury.title") };
            d.Body.Add(c.T("dlg.injury.body", "runner", UiContext.Name(v.Row), "severity", c.T("injury." + v.Severity.ToString().ToLowerInvariant() + ".name"), "weeks", v.WeeksLeft.ToString()));
            if (v.NextRace.HasValue)
                d.Body.Add(c.T(v.MissesRace ? "dlg.injury.misses" : "dlg.injury.in_time", "race", c.RaceName(v.NextRace.Value), "weeks", v.WeeksUntilRace.ToString()));
            if (v.InOrder && v.Section.HasValue) d.Body.Add(c.T("dlg.injury.in_order", "section", (v.Section.Value + 1).ToString()));
            if (v.Substitutes.Count > 0) d.Body.Add(c.T("dlg.injury.subs", "names", string.Join("・", v.Substitutes.Take(3).Select(x => UiContext.Name(x.Row)))));
            d.Button(c.T("dlg.injury.runner"), () => r.Show(new RunnerScreen { RunnerId = runnerId }, true));
            d.Button(c.T("ui.continue"), r.Continue, true);
            d.DefaultButton = 1;
            r.OpenDialog(d);
        }

        /// <summary>SC-41 コンディション警告。</summary>
        public static void Warn(ScreenRouter r)
        {
            UiContext c = r.Context;
            ConditionWarnView v = c.S.ConditionWarning();
            var d = new DialogSpec { Id = "dlg.warn", Title = c.T("dlg.warn.title") };
            d.Body.Add(c.T("dlg.warn.body", "n", v.Rows.Count.ToString(), "threshold", v.Threshold.ToString()));
            foreach (WarnRowView w in v.Rows.Take(8))
                d.Body.Add(c.T("dlg.warn.row", "runner", UiContext.Name(w.Row), "now", w.Fatigue.ToString(), "next", w.NextFatigue.ToString(), "pct", Fmt.Pct(w.InjuryNext)));
            d.Button(c.T("dlg.warn.plan"), () => r.Show(new PlanScreen(), true));
            d.Button(c.T("ui.continue"), r.Continue, true);
            d.DefaultButton = 1;
            r.OpenDialog(d);
        }

        /// <summary>SC-40 ポーズ: 再開・セーブ・文字の大きさ・観戦モード・タイトルへ。</summary>
        public static void Pause(ScreenRouter r)
        {
            UiContext c = r.Context;
            if (c.App.Host == null) return;
            var d = new DialogSpec { Id = "dlg.pause", Title = c.T("dlg.pause.title") };
            d.Body.Add(c.T("dlg.pause.play", "time", Fmt.Time(c.Host.PlaySeconds * 10L)));
            bool canSave = c.Host.Flow.CanSave || c.Host.Flow.MidWeek;
            d.Button(c.T("dlg.pause.resume"), null, true);
            d.Button(c.T("dlg.pause.save"), () =>
            {
                if (c.Host.SaveManual()) r.Toast(c.T("dlg.pause.saved"));
                else r.Toast(c.T("dlg.pause.save_failed"));
            }, false, canSave, c.T("dlg.pause.cannot_save"));
            int next = r.TextScale == 100 ? 125 : r.TextScale == 125 ? 150 : 100;
            d.Button(c.T("dlg.pause.text_scale", "pct", next.ToString()), () => { r.SetTextScale(next); r.Rebuild(); });
            bool full = c.S.State.Settings.SpectateMode == "Full";
            d.Button(c.T(full ? "dlg.pause.spectate_digest" : "dlg.pause.spectate_full"), () => { c.Check(c.S.SetSpectateMode(full ? "Digest" : "Full")); r.Toast(c.T(full ? "dlg.pause.spectate_digest_on" : "dlg.pause.spectate_full_on")); });
            d.Button(c.T("dlg.pause.title_screen"), () => r.Confirm(c.T("dlg.pause.title_confirm"), r.ShowTitle, true));
            d.DefaultButton = 0;
            r.OpenDialog(d);
        }
    }
}
