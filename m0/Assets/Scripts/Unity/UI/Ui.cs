using System;
using System.Collections.Generic;
using Ekiden.App;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Domain.Views;
using Ekiden.Platform;
using UnityEngine.UIElements;

namespace Ekiden.Unity.UI
{
    /// <summary>画面が使う外のもの(App・文字列・ルーター)。画面は Views と命令だけを使い、規則を書かない(08 原則 10)。</summary>
    public sealed class UiContext
    {
        public GameApp App;
        public ILocalization Loc;
        public ScreenRouter Router;

        public GameHost Host { get { return App.Host; } }
        public GameSession S { get { return App.Host.Session; } }

        /// <summary>文字列の表から引く(ja.json。差し込みは『名前, 値』の交互)。</summary>
        public string T(string key, params string[] args) { return Loc.Get(key, args); }

        public string RaceName(RaceKind k) { return T("race." + k.ToString().ToLowerInvariant() + ".name"); }

        public string School(string nameKey) { return string.IsNullOrEmpty(nameKey) ? "" : T(nameKey); }

        public static string Name(RunnerRowView r) { return r == null ? "" : r.FamilyName + " " + r.GivenName; }

        /// <summary>操作の結果(08 F4)。失敗の理由は OpText で文にしてトーストに出す。成功なら true。</summary>
        public bool Check(OpResult r)
        {
            if (r == null || r.Ok) return true;
            Router.Toast(OpText.Reason(r, Loc));
            return false;
        }
    }

    /// <summary>要素を作る小さな部品(見た目は USS のクラス)。</summary>
    public static class Ui
    {
        public static Label L(string text, string cls = null)
        {
            var l = new Label(text);
            l.AddToClassList("label");
            if (cls != null) foreach (string c in cls.Split(' ')) l.AddToClassList(c);
            if (text != null && text.IndexOf('⟦') >= 0) l.AddToClassList("dev-missing");
            return l;
        }

        public static Button B(string text, Action onClick, string cls = null)
        {
            var b = new Button(onClick) { text = text };
            if (cls != null) foreach (string c in cls.Split(' ')) b.AddToClassList(c);
            return b;
        }

        public static VisualElement Box(string cls)
        {
            var v = new VisualElement();
            foreach (string c in cls.Split(' ')) v.AddToClassList(c);
            return v;
        }

        /// <summary>読むだけの行(24 u)。セルは (文字, 幅) の組。幅 0 は伸びる。</summary>
        public static VisualElement Line(bool own, params (string text, float width)[] cells)
        {
            VisualElement row = Box("line");
            if (own) row.AddToClassList("own");
            foreach ((string text, float width) in cells)
            {
                Label l = L(text, "cell");
                if (width > 0) { l.style.width = width; l.style.flexShrink = 0; }
                else l.style.flexGrow = 1;
                row.Add(l);
            }
            return row;
        }

        /// <summary>疲労のゲージ(色 + 数字。08 §4.2)。</summary>
        public static VisualElement Gauge(int value)
        {
            VisualElement row = Box("row");
            VisualElement g = Box("gauge");
            g.style.width = 60; g.style.flexGrow = 0;
            VisualElement fill = Box("gauge-fill");
            fill.style.width = Length.Percent(Math.Max(0, Math.Min(100, value)));
            fill.style.backgroundColor = value >= 85 ? new UnityEngine.Color(0.86f, 0.31f, 0.27f) : value >= 70 ? new UnityEngine.Color(0.9f, 0.55f, 0.2f) : value >= 40 ? new UnityEngine.Color(0.9f, 0.67f, 0.2f) : new UnityEngine.Color(0.3f, 0.69f, 0.43f);
            g.Add(fill);
            row.Add(g);
            row.Add(L(value.ToString(), "sub"));
            return row;
        }
    }

    /// <summary>画面の領域(08a の grid の 1 つ)。中の操作できる項目が十字キーの移動先になる。</summary>
    public sealed class Region
    {
        public readonly VisualElement El;
        internal readonly FocusGrid<VisualElement>.Region Grid;

        internal Region(VisualElement el, FocusGrid<VisualElement>.Region grid) { El = el; Grid = grid; }

        public Label Text(string text, string cls = null) { Label l = Ui.L(text, cls); El.Add(l); return l; }

        public T Add<T>(T e) where T : VisualElement { El.Add(e); return e; }

        /// <summary>操作できる項目(ボタン・行)を足す。</summary>
        public Button Button(string text, Action onClick, string cls = null, bool enabled = true)
        {
            Button b = Ui.B(text, onClick, cls);
            b.SetEnabled(enabled);
            b.userData = onClick;                                             // パッドの A / Enter(ScreenRouter.Submit)
            Focusable(b);
            El.Add(b);
            return b;
        }

        /// <summary>理由つきの無効(08 F4): 消さずに暗くし、選ぶと理由を出す。</summary>
        public Button Blocked(string text, string reason, Action<string> show, string cls = null)
        {
            Action why = () => show(reason);
            Button b = Ui.B(text, why, cls);
            b.userData = why;
            b.AddToClassList("blocked");
            b.style.color = new UnityEngine.Color(0.43f, 0.46f, 0.51f);
            Focusable(b);
            El.Add(b);
            return b;
        }

        public void Focusable(VisualElement e)
        {
            e.focusable = true;
            Grid.Items.Add(e);
        }
    }

    /// <summary>1 回の表示の組み立て。<see cref="NewRow"/> で grid の行を始め、<see cref="Region"/> で領域を左から足す。操作バーは <see cref="Actions"/>。</summary>
    public sealed class Page
    {
        private readonly VisualElement _content, _footer;
        private VisualElement _row;
        internal readonly FocusGrid<VisualElement> Grid = new FocusGrid<VisualElement>();
        private Region _actions;

        public UiContext C { get; }
        public string DefaultFocus = "actions";

        internal Page(UiContext c, VisualElement content, VisualElement footer, Region header)
        {
            C = c;
            _content = content;
            _footer = footer;
            Grid.NewRow();
            if (header != null) { }
        }

        internal FocusGrid<VisualElement>.Region AddGridRegion(string name, bool horizontal) { return Grid.AddRegion(name, horizontal); }

        /// <summary>grid の新しい行。grow = 内容の高さを分け合う。</summary>
        public void NewRow(bool grow = true)
        {
            Grid.NewRow();
            _row = Ui.Box("row");
            _row.style.alignItems = Align.Stretch;
            if (grow) { _row.style.flexGrow = 1; _row.style.flexBasis = 0; }
            _row.style.marginBottom = 4;
            _content.Add(_row);
        }

        /// <summary>いまの行に領域を足す。scroll = 縦にスクロールできる。width > 0 は固定幅。</summary>
        public Region Region(string name, bool scroll = false, bool horizontal = false, float width = 0, string title = null)
        {
            if (_row == null) NewRow();
            VisualElement box = Ui.Box("region col");
            if (width > 0) { box.style.width = width; box.style.flexGrow = 0; box.style.flexBasis = width; }
            if (title != null) box.Add(Ui.L(title, "region-title"));
            VisualElement inner;
            if (scroll)
            {
                var sv = new ScrollView(ScrollViewMode.Vertical);
                sv.AddToClassList("scroll");
                sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                box.Add(sv);
                inner = sv.contentContainer;
            }
            else
            {
                inner = horizontal ? Ui.Box("row wrap") : Ui.Box("col");
                box.Add(inner);
            }
            _row.Add(box);
            return new Region(inner, Grid.AddRegion(name, horizontal));
        }

        /// <summary>操作バー(フッタ。grid の最後の行)。左から『戻る系』『補助』『主たる操作(右端)』の順に足す。</summary>
        public Region Actions
        {
            get
            {
                if (_actions != null) return _actions;
                Grid.NewRow();
                _actions = new Region(_footer, Grid.AddRegion("actions", true));
                return _actions;
            }
        }

        /// <summary>主たる操作(右端・強調。パッドの A)。</summary>
        public Button Primary(string text, Action onClick, bool enabled = true) { return Actions.Button(text, onClick, "btn-primary", enabled); }
    }
}
