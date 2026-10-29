using System;
using System.Collections.Generic;
using Ekiden.App;
using Ekiden.Domain.Game;
using Ekiden.Domain.Text;
using Ekiden.Domain.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ekiden.Unity.UI
{
    /// <summary>画面の基底。<see cref="Id"/> は 08a の画面 ID(`scr.home` など)で、題は <c>{Id}.title</c>。</summary>
    public abstract class ScreenBase
    {
        public abstract string Id { get; }
        public UiContext C { get; internal set; }
        protected ScreenRouter R { get { return C.Router; } }
        protected GameSession S { get { return C.S; } }

        /// <summary>ヘッダの『戻る』(B / Esc)を出すか。進行の画面(flow)は出さない。</summary>
        public virtual bool CanGoBack { get { return true; } }

        /// <summary>ヘッダの要約(日付・資金)を出すか(タイトルなどプレイの外は出さない)。</summary>
        public virtual bool ShowSummary { get { return true; } }

        public abstract void Build(Page p);

        /// <summary>おまかせの自動進行(U5)で、この画面の既定の操作(推奨の適用 → 続ける)。</summary>
        public virtual void AutoDefault() { R.Continue(); }

        /// <summary>おまかせ(パッドの Y)。既定は何もしない。</summary>
        public virtual void Omakase() { }
    }

    /// <summary>重なる表示(SC-36・37・38・40・41・42)。</summary>
    public sealed class DialogSpec
    {
        public string Id;                                    // dlg.confirm など
        public string Title;
        public readonly List<string> Body = new List<string>();
        public readonly List<(string text, Action act, bool primary, bool enabled, string reason)> Buttons = new List<(string, Action, bool, bool, string)>();
        public int DefaultButton;
        public Action Cancel;                                // B / Esc(null なら閉じるだけ)
        public Action<VisualElement> Extra;                  // 本文の下に足す要素

        public DialogSpec Button(string text, Action act, bool primary = false, bool enabled = true, string reason = null)
        {
            Buttons.Add((text, act, primary, enabled, reason));
            return this;
        }
    }

    /// <summary>
    /// 画面の入れ替え(08 §5)と進行(08 §6)。『続ける』は <see cref="Continue"/> = <c>GameHost.Advance()</c> で、返ってきた停止(<see cref="StopKind"/>)を画面に対応づける(<see cref="ShowStop"/>)。
    /// フォーカスは画面ごとの <see cref="FocusGrid{T}"/>(08a の grid)。
    /// </summary>
    public sealed class ScreenRouter
    {
        private readonly UiContext _c;
        private readonly VisualElement _root, _content, _footer, _headerLeft, _headerRight, _dialogLayer;
        private readonly Label _title, _summary, _toast;
        private readonly Stack<ScreenBase> _stack = new Stack<ScreenBase>();
        private Page _page;
        private FocusGrid<VisualElement> _dialogGrid;
        private DialogSpec _dialog;
        private float _toastUntil;
        private readonly Queue<DialogSpec> _dialogQueue = new Queue<DialogSpec>();

        public ScreenBase Current { get; private set; }
        public Stop CurrentStop { get; private set; }
        public string CurrentDialog { get { return _dialog != null ? _dialog.Id : null; } }
        public string LastToast { get; private set; }
        public int TextScale { get; private set; } = 100;

        /// <summary>画面が開くたび(画面 ID)。自動進行の記録・スクリーンショットに使う。</summary>
        public event Action<string> Shown;

        public ScreenRouter(UiContext c, VisualElement root)
        {
            _c = c;
            c.Router = this;
            _root = root.Q("root") ?? root;
            _content = root.Q("content");
            _footer = root.Q("footer");
            _headerLeft = root.Q("header-left");
            _headerRight = root.Q("header-right");
            _title = root.Q<Label>("header-title");
            _summary = root.Q<Label>("header-summary");
            _toast = root.Q<Label>("toast");
            _dialogLayer = root.Q("dialog-layer");
            _content.style.flexDirection = FlexDirection.Column;
        }

        public UiContext Context { get { return _c; } }

        // ------------------------------------------------------------ 画面
        /// <summary>画面を出す。push = 戻れる(一覧 → 詳細など)。push しなければ、戻り先の積みを捨てる。</summary>
        public void Show(ScreenBase s, bool push = false)
        {
            if (!push) _stack.Clear();
            else if (Current != null) _stack.Push(Current);
            s.C = _c;
            Current = s;
            Rebuild();
        }

        public bool CanBack { get { return _stack.Count > 0 || (Current != null && Current.CanGoBack && Current.Id != "scr.home" && Current.Id != "scr.title"); } }

        public void Back()
        {
            if (_dialog != null) { CancelDialog(); return; }
            if (_stack.Count > 0) { Current = _stack.Pop(); Rebuild(); return; }
            if (Current == null || !Current.CanGoBack) return;
            if (_c.App.Host != null) Home();
            else ShowTitle();
        }

        /// <summary>いまの画面を作り直す(操作のあとの再表示)。フォーカスは同じ領域・同じ位置に戻す(08 F3)。</summary>
        public void Rebuild()
        {
            string region = _page != null ? _page.Grid.CurrentRegion : null;
            int index = -1;
            if (_page != null && _page.Grid.Current != null)
            {
                var r = _page.Grid.Find(region);
                if (r != null) index = r.Items.IndexOf(_page.Grid.Current);
            }
            bool same = _page != null && _lastBuilt == Current;
            _content.Clear();
            _footer.Clear();
            _headerLeft.Clear();
            _headerRight.Clear();
            _page = new Page(_c, _content, _footer, null);
            var header = new Region(_headerLeft, _page.AddGridRegion("header", true));
            if (CanBack) header.Button(_c.T("ui.back"), Back, "btn-small");
            _title.text = _c.T(Current.Id + ".title");
            _summary.text = Current.ShowSummary && _c.App.Host != null ? Summary() : "";
            try { Current.Build(_page); }
            catch (Exception e)
            {
                Debug.LogException(e);
                _content.Add(Ui.L(_c.T("ui.error.screen") + "\n" + e.Message, "bad"));
            }
            if (_c.App.Host != null && Current.ShowSummary)
            {
                var right = new Region(_headerRight, header.Grid);
                right.Button(_c.T("ui.pause"), OpenPause, "btn-small");
            }
            if (same && region != null)
            {
                var r = _page.Grid.Find(region);
                if (r != null && r.Items.Count > 0) { _page.Grid.Sync(r.Items[Math.Max(0, Math.Min(index, r.Items.Count - 1))]); Focus(_page.Grid.Current); }
                else Focus(_page.Grid.FocusDefault(_page.DefaultFocus));
            }
            else Focus(_page.Grid.FocusDefault(_page.DefaultFocus));
            _lastBuilt = Current;
            if (Shown != null) Shown(Current.Id);
        }

        private ScreenBase _lastBuilt;

        private string Summary()
        {
            HomeView h = _c.S.Home();
            return _c.School(h.SchoolNameKey) + "  " + _c.T("ui.date", "year", h.Year.ToString(), "month", h.CalendarMonth.ToString(), "week", h.WeekOfMonth.ToString())
                + "  " + _c.T("ui.funds", "funds", Fmt.Money(h.Funds));
        }

        public void ShowTitle() { _c.App.Close(); Show(new TitleScreen()); }

        public void Home() { CloseAllDialogs(); Show(new HomeScreen()); }

        // ------------------------------------------------------------ 進行
        /// <summary>『続ける』: 次の停止まで進め、停止に応じた画面を出す。</summary>
        public void Continue()
        {
            CloseAllDialogs();
            Stop s = _c.Host.Advance();
            ShowStop(s);
        }

        /// <summary>停止(08 §6.3)→ 画面。</summary>
        public void ShowStop(Stop s)
        {
            CurrentStop = s;
            switch (s.Kind)
            {
                case StopKind.AnnualStart: Show(new AnnualScreen { Flow = true }); break;
                case StopKind.MonthPlan: Show(new PlanScreen { Flow = true }); break;
                case StopKind.Entry: Show(new EntryScreen { Kind = s.Race.Value, Flow = true }); break;
                case StopKind.QualifierEntry: Show(new QualifierEntryScreen()); break;
                case StopKind.Order: Show(new OrderScreen { Kind = s.Race.Value, Flow = true }); break;
                case StopKind.RaceDayPrep: Show(new RaceDayScreen { Day = s.RaceDay }); break;
                case StopKind.RaceDay:
                    if (s.Qualifier) Show(new QualifierResultScreen());
                    else Show(new ResultScreen { Outcome = s.Week != null ? s.Week.Race : null, Own = s.Week != null ? s.Week.OwnRace : null });
                    break;
                case StopKind.Event: Home(); Dialogs.Event(this, s); break;
                case StopKind.Injury: Home(); Dialogs.Injury(this, s.RunnerId); break;
                case StopKind.Warn: Home(); Dialogs.Warn(this); break;
                case StopKind.MonthReport: Show(new MonthReportScreen { Report = s.Report }); break;
                case StopKind.YearEndSettle: Show(new YearSummaryScreen()); break;
                case StopKind.YearEndTurnover: Show(new GraduationScreen()); break;
                default: Home(); break;
            }
        }

        // ------------------------------------------------------------ ダイアログ
        public void OpenDialog(DialogSpec d)
        {
            if (_dialog != null) { _dialogQueue.Enqueue(d); return; }
            _dialog = d;
            _dialogLayer.Clear();
            _dialogLayer.RemoveFromClassList("hidden");
            VisualElement box = Ui.Box("dialog col");
            if (!string.IsNullOrEmpty(d.Title)) box.Add(Ui.L(d.Title, "title"));
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.style.maxHeight = 220;
            foreach (string line in d.Body) sv.Add(Ui.L(line, "dialog-body"));
            if (d.Extra != null) d.Extra(sv.contentContainer);
            box.Add(sv);
            VisualElement buttons = Ui.Box("dialog-buttons");
            box.Add(buttons);
            _dialogGrid = new FocusGrid<VisualElement>();
            _dialogGrid.NewRow();
            var reg = new Region(buttons, _dialogGrid.AddRegion("buttons", true));
            var made = new List<VisualElement>();
            foreach (var b in d.Buttons)
            {
                var act = b.act;
                Button btn = b.enabled
                    ? reg.Button(b.text, () => { CloseDialog(); if (act != null) act(); }, b.primary ? "btn-primary" : null)
                    : reg.Blocked(b.text, b.reason, Toast, b.primary ? "btn-primary" : null);
                made.Add(btn);
            }
            _dialogLayer.Add(box);
            if (made.Count > 0)
            {
                VisualElement f = made[Math.Max(0, Math.Min(d.DefaultButton, made.Count - 1))];
                _dialogGrid.Sync(f);
                Focus(f);
            }
            if (Shown != null) Shown(d.Id);
        }

        public void CloseDialog()
        {
            _dialog = null;
            _dialogGrid = null;
            _dialogLayer.Clear();
            _dialogLayer.AddToClassList("hidden");
            if (_dialogQueue.Count > 0) OpenDialog(_dialogQueue.Dequeue());
            else if (_page != null) Focus(_page.Grid.Current);
        }

        private void CloseAllDialogs()
        {
            _dialogQueue.Clear();
            if (_dialog != null) CloseDialog();
        }

        private void CancelDialog()
        {
            DialogSpec d = _dialog;
            CloseDialog();
            if (d.Cancel != null) d.Cancel();
        }

        /// <summary>自動進行: ダイアログの既定のボタン。</summary>
        public void PressDialogDefault()
        {
            if (_dialog == null) return;
            DialogSpec d = _dialog;
            int i = Math.Max(0, Math.Min(d.DefaultButton, d.Buttons.Count - 1));
            CloseDialog();
            if (d.Buttons.Count > 0 && d.Buttons[i].enabled && d.Buttons[i].act != null) d.Buttons[i].act();
        }

        public bool DialogOpen { get { return _dialog != null; } }

        public void Confirm(string body, Action yes, bool irreversible = false)
        {
            var d = new DialogSpec { Id = "dlg.confirm", Title = _c.T("dlg.confirm.title") };
            d.Body.Add(body);
            d.Button(_c.T("ui.cancel"), null);
            d.Button(_c.T("ui.ok"), yes, true);
            d.DefaultButton = irreversible ? 0 : 1;                       // 08 F2: 取り消せない操作は『戻る』が既定
            OpenDialog(d);
        }

        public void Notify(string title, string body, Action ok = null)
        {
            var d = new DialogSpec { Id = "dlg.notify", Title = title };
            d.Body.Add(body);
            d.Button(_c.T("ui.ok"), ok, true);
            d.Cancel = ok;
            OpenDialog(d);
        }

        /// <summary>操作の結果。確認が要るときは警告を見せて、確認ずみの操作をもう一度呼ぶ(08 §7.6)。</summary>
        public void Run(Func<bool, OpResult> op, Action after)
        {
            OpResult r = op(false);
            if (r.Ok) { after(); return; }
            if (r.NeedsConfirm)
            {
                Confirm(string.Join("\n", OpText.Warnings(r, _c.Loc)), () => { if (_c.Check(op(true))) after(); });
                return;
            }
            _c.Check(r);
        }

        public void OpenPause() { Dialogs.Pause(this); }

        // ------------------------------------------------------------ トースト・文字の倍率
        public void Toast(string text)
        {
            LastToast = text;
            _toast.text = text;
            _toast.RemoveFromClassList("hidden");
            _toastUntil = Time.unscaledTime + 3f;
        }

        public void Tick()
        {
            if (_toastUntil > 0 && Time.unscaledTime > _toastUntil) { _toast.AddToClassList("hidden"); _toastUntil = 0; }
            for (int i = _waits.Count - 1; i >= 0; i--)
            {
                if (!_waits[i].cond()) continue;
                Action then = _waits[i].then;
                _waits.RemoveAt(i);
                then();
            }
        }

        private readonly List<(Func<bool> cond, Action then)> _waits = new List<(Func<bool>, Action)>();

        /// <summary>裏の処理(世界の生成など)が終わったら、メインスレッドで then を呼ぶ。</summary>
        public void WaitFor(Func<bool> cond, Action then) { _waits.Add((cond, then)); }

        public bool Busy { get { return _waits.Count > 0; } }

        /// <summary>文字の倍率(08 §2 K_UI_TEXT_SCALE。本文だけを変える)。</summary>
        public void SetTextScale(int pct)
        {
            TextScale = pct;
            _root.RemoveFromClassList("scale-125");
            _root.RemoveFromClassList("scale-150");
            if (pct == 125) _root.AddToClassList("scale-125");
            if (pct == 150) _root.AddToClassList("scale-150");
        }

        // ------------------------------------------------------------ 入力(十字キー・A・B・メニュー・Y)
        public FocusGrid<VisualElement> ActiveGrid { get { return _dialogGrid ?? (_page != null ? _page.Grid : null); } }

        public void Navigate(NavDir d)
        {
            FocusGrid<VisualElement> g = ActiveGrid;
            if (g == null) return;
            if (g.Current == null) g.FocusDefault(null);
            else
            {
                VisualElement f = _root.panel != null ? _root.panel.focusController.focusedElement as VisualElement : null;
                if (f != null) g.Sync(f);                                         // マウスで動かしたフォーカスに合わせる
                g.Move(d);
            }
            Focus(g.Current);
        }

        /// <summary>A / Enter: フォーカスしている項目を選ぶ(ボタンの動作。無効表示の項目は理由を出す)。</summary>
        public void Submit()
        {
            FocusGrid<VisualElement> g = ActiveGrid;
            if (g == null || g.Current == null) return;
            var act = g.Current.userData as Action;
            if (act != null && g.Current.enabledInHierarchy) act();
        }

        public void Omakase() { if (_dialog == null && Current != null) Current.Omakase(); }

        private static void Focus(VisualElement e)
        {
            if (e == null) return;
            e.Focus();
            var sv = e.GetFirstAncestorOfType<ScrollView>();
            if (sv != null) e.schedule.Execute(() => { if (e.panel != null && sv.panel != null) sv.ScrollTo(e); });     // レイアウトのあと(組み立ての直後は ScrollTo できない)
        }

        public string FocusedRegion { get { return ActiveGrid != null ? ActiveGrid.CurrentRegion : null; } }
        public VisualElement Focused { get { return ActiveGrid != null ? ActiveGrid.Current : null; } }
    }
}
