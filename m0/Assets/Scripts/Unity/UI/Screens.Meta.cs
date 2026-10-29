using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Ekiden.App;
using Ekiden.Domain.Text;
using Ekiden.Domain.World;
using UnityEngine.UIElements;

namespace Ekiden.Unity.UI
{
    internal static class SlotText
    {
        public static string School(UiContext c, SlotSummary s)
        {
            string k = s.SchoolNameKey ?? "";
            return k.StartsWith("school.", StringComparison.Ordinal) ? c.T(k) : k;
        }

        public static string Describe(UiContext c, SlotSummary s)
        {
            if (s.Broken) return c.T("scr.load.broken");
            if (!s.HasData) return c.T("scr.load.empty");
            return c.T("scr.load.row", "school", School(c, s), "scenario", c.T(s.ScenarioId + ".name"), "year", s.Year.ToString(), "month", (((s.MonthIndex + 3) % 12) + 1).ToString(),
                "play", Fmt.Time(s.PlaySeconds * 10L), "saved", DateTimeOffset.FromUnixTimeSeconds(s.SavedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }
    }

    /// <summary>SC-01 タイトル。</summary>
    public sealed class TitleScreen : ScreenBase
    {
        public override string Id { get { return "scr.title"; } }
        public override bool CanGoBack { get { return false; } }
        public override bool ShowSummary { get { return false; } }

        public override void Build(Page p)
        {
            p.NewRow();
            Region info = p.Region("logo", width: 300);
            info.Text(C.T("scr.title.name"), "big");
            info.Text(C.T("scr.title.version", "version", UnityEngine.Application.version), "sub");
            List<SlotSummary> slots = C.App.Slots();
            SlotSummary last = slots.Where(s => s.HasData && !s.Broken).OrderByDescending(s => Math.Max(s.SavedAt, s.ManualSavedAt)).FirstOrDefault();
            Region menu = p.Region("menu");
            p.DefaultFocus = "menu";
            if (last != null) menu.Button(C.T("scr.title.continue") + "\n" + SlotText.Describe(C, last), () => Open(last.Slot), "btn-row btn-primary");
            else menu.Blocked(C.T("scr.title.continue"), C.T("scr.title.no_save"), R.Toast, "btn-row");
            menu.Button(C.T("scr.title.new"), () => R.Show(new NewGameScreen(), true), "btn-row");
            if (slots.Any(s => s.HasData)) menu.Button(C.T("scr.title.load"), () => R.Show(new LoadScreen(), true), "btn-row");
            else menu.Blocked(C.T("scr.title.load"), C.T("scr.title.no_save"), R.Toast, "btn-row");
            foreach (string k in new[] { "achv", "store", "settings", "credits" })
                menu.Blocked(C.T("scr.title." + k), C.T("ui.not_in_m1"), R.Toast, "btn-row");
        }

        private void Open(int slot) { LoadScreen.OpenSlot(R, slot); }

        public override void AutoDefault() { R.Show(new NewGameScreen(), true); }
    }

    /// <summary>SC-03 セーブの選択。</summary>
    public sealed class LoadScreen : ScreenBase
    {
        public override string Id { get { return "scr.load"; } }
        public override bool ShowSummary { get { return false; } }

        public static void OpenSlot(ScreenRouter r, int slot)
        {
            OpenResult res = r.Context.App.Open(slot);
            if (res.Host == null) { r.Notify(r.Context.T("scr.load.failed.title"), r.Context.T("scr.load.failed")); return; }
            r.Home();
            foreach (HostNotice n in res.Notices) r.Toast(r.Context.T("scr.load.notice." + n.Kind.ToString().ToLowerInvariant()));
        }

        public override void Build(Page p)
        {
            p.NewRow();
            Region list = p.Region("slots", scroll: true);
            p.DefaultFocus = "slots";
            foreach (SlotSummary s in C.App.Slots())
            {
                string text = C.T("scr.load.slot", "n", s.Slot.ToString()) + "  " + SlotText.Describe(C, s);
                if (s.HasData && !s.Broken) list.Button(text, () => OpenSlot(R, s.Slot), "btn-row");
                else list.Blocked(text, s.Broken ? C.T("scr.load.broken") : C.T("scr.load.empty"), R.Toast, "btn-row");
            }
            p.Actions.Button(C.T("ui.back"), R.Back);
        }
    }

    /// <summary>SC-02 新規ゲーム: ①開始校 ②監督名 ③保存先のスロット(R-13)。世界の生成は裏のスレッドで行い、所要時間を測る(09 §14.2・§15.5)。</summary>
    public sealed class NewGameScreen : ScreenBase
    {
        public override string Id { get { return "scr.newgame"; } }
        public override bool ShowSummary { get { return false; } }

        private static readonly Difficulty[] Order = { Difficulty.Elite, Difficulty.Mid, Difficulty.Low, Difficulty.Club };
        private Difficulty _d = Difficulty.Mid;
        private string _manager = "";
        private int _slot = 1;
        private int _step;
        private Task<GameHost> _task;
        private Stopwatch _sw;

        /// <summary>最後に測った世界の生成(新規ゲームの開始)の秒数。</summary>
        public static double LastGenerationSeconds = -1;

        public override void Build(Page p)
        {
            p.NewRow(false);
            Region steps = p.Region("steps", horizontal: true);
            for (int i = 0; i < 3; i++) steps.Text((i == _step ? "▶ " : "　") + C.T("scr.newgame.step" + (i + 1)), i == _step ? "bold" : "sub");
            p.NewRow();
            Region body = p.Region("body", horizontal: _step == 0, scroll: _step == 2);
            p.DefaultFocus = "body";
            if (_task != null)
            {
                body.Text(C.T("scr.newgame.generating"), "title");
                body.Text(C.T("scr.newgame.generating_note"), "sub");
                return;
            }
            if (_step == 0)
            {
                foreach (Difficulty d in Order)
                {
                    string key = d.ToString().ToLowerInvariant();
                    Button b = body.Button(C.T("sc." + key + ".name") + "\n" + C.T("scr.newgame.card." + key), () => { _d = d; R.Rebuild(); }, "btn-card");
                    if (d == _d) b.AddToClassList("selected");
                }
                body.El.parent.Add(Ui.L(C.T("scr.newgame.note." + _d.ToString().ToLowerInvariant()), "sub"));
            }
            else if (_step == 1)
            {
                body.Text(C.T("scr.newgame.manager"));
                var tf = new TextField { maxLength = 8, value = _manager };
                tf.RegisterValueChangedCallback(e => _manager = e.newValue);
                body.Focusable(tf);
                body.Add(tf);
                body.Text(C.T("scr.newgame.manager_note"), "sub");
            }
            else
            {
                body.Text(C.T("scr.newgame.slot"));
                foreach (SlotSummary s in C.App.Slots())
                {
                    int slot = s.Slot;
                    Button b = body.Button(C.T("scr.load.slot", "n", s.Slot.ToString()) + "  " + SlotText.Describe(C, s), () => { _slot = slot; R.Rebuild(); }, "btn-row");
                    if (slot == _slot) b.AddToClassList("selected");
                }
            }
            p.Actions.Button(C.T("ui.back"), () => { if (_step == 0) R.Back(); else { _step--; R.Rebuild(); } });
            if (_step < 2) p.Primary(C.T("ui.next"), () => { _step++; R.Rebuild(); });
            else p.Primary(C.T("scr.newgame.start"), Start);
        }

        private void Start()
        {
            SlotSummary s = C.App.Slots().FirstOrDefault(x => x.Slot == _slot);
            if (s != null && s.HasData) R.Confirm(C.T("scr.newgame.overwrite", "n", _slot.ToString()), Generate, true);
            else Generate();
        }

        private void Generate()
        {
            string name = string.IsNullOrWhiteSpace(_manager) ? C.T("scr.newgame.manager_default") : _manager.Trim();
            Difficulty d = _d;
            int slot = _slot;
            GameApp app = C.App;
            ulong seed = app.NewSeed();
            _sw = Stopwatch.StartNew();
            _task = Task.Run(() => app.NewGame(slot, d, name, seed));                 // 世界の生成(予熱 15 年度)は裏で。完成してから差し替える(09 §14.2)
            R.Rebuild();
            R.WaitFor(() => _task.IsCompleted, () =>
            {
                LastGenerationSeconds = _sw.Elapsed.TotalSeconds;
                if (_task.IsFaulted) { UnityEngine.Debug.LogException(_task.Exception); _task = null; R.Notify(C.T("scr.newgame.failed.title"), C.T("scr.newgame.failed")); R.Rebuild(); return; }
                UnityEngine.Debug.Log("世界の生成と最初のセーブ: " + LastGenerationSeconds.ToString("F2") + " 秒");
                R.Home();
            });
        }

        public override void AutoDefault()
        {
            if (_task != null) return;
            if (_step < 2) { _step++; R.Rebuild(); return; }
            Generate();
        }
    }
}
