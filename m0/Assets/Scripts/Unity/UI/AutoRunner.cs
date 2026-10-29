using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using Ekiden.Persistence.Snapshot;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Ekiden.Unity.UI
{
    /// <summary>
    /// 人が押さなくても画面を進める(M1 の U3・U5)。起動の引数 <c>-ekidenAuto [出力フォルダ]</c> で動く。
    /// ① 世界の生成の時間(09 §14.2)② 仮想のゲームパッドで十字キー・A・B(08 §3)③ 新規ゲーム → 1 年度を、各画面の既定の操作(おまかせ)だけで最後まで
    /// ④ 画面ごとのスクリーンショット ⑤ 重い画面の p95 フレーム時間・組み立て時間・メモリ(09 §15.5)⑥ 解像度・文字の倍率ごとの撮影
    /// ⑦ 途中で保存 → 開く → 同じ節目まで進めた状態が、保存しなかった流れと一致するか。結果は <c>auto.txt</c>、終了コード 0 = 例外 0 かつ一致。
    /// </summary>
    public static class AutoRunner
    {
        public const string Arg = "-ekidenAuto";

        public static bool TryStart(UiRoot root)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, Arg);
            if (i < 0) return false;
            string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : Path.Combine(Application.persistentDataPath, "auto");
            root.gameObject.AddComponent<AutoRunnerBehaviour>().Init(root, dir);
            return true;
        }
    }

    public sealed class AutoRunnerBehaviour : MonoBehaviour
    {
        private UiRoot _root;
        private string _dir;
        private readonly StringBuilder _log = new StringBuilder();
        private int _errors;
        private readonly List<string> _errorLines = new List<string>();
        private readonly HashSet<string> _captured = new HashSet<string>();
        private readonly List<string> _screens = new List<string>();
        private ScreenRouter R { get { return _root.Router; } }
        private static readonly string[] Heavy = { "scr.plan", "scr.roster", "scr.live", "scr.scout.map", "scr.history" };
        private readonly Dictionary<string, string> _perf = new Dictionary<string, string>();

        public void Init(UiRoot root, string dir)
        {
            _root = root;
            _dir = dir;
            Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            _deadline = Time.realtimeSinceStartup + 900f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            StartCoroutine(Run());
        }

        private void OnLog(string msg, string stack, LogType t)
        {
            if (t != LogType.Exception && t != LogType.Error) return;
            _errors++;
            if (_errorLines.Count < 30) _errorLines.Add(t + ": " + msg + "\n" + stack);
        }

        private void W(string line) { _log.AppendLine(line); Debug.Log("[auto] " + line); }

        private IEnumerator Run()
        {
            yield return null;
            while (R == null) yield return null;
            R.Shown += id => { if (_screens.Count == 0 || _screens[_screens.Count - 1] != id) _screens.Add(id); };
            W("unity " + Application.unityVersion + " / " + Application.platform + " / " + SystemInfo.operatingSystem + " / " + SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName);
            W("screen " + Screen.width + "x" + Screen.height);

            // ① 世界の生成の時間(Domain の GameSession.New だけ。3 回)
            for (int k = 0; k < 3; k++)
            {
                var sw = Stopwatch.StartNew();
                GameSession.New(1000UL + (ulong)k, Difficulty.Mid);
                W("world_gen_s " + sw.Elapsed.TotalSeconds.ToString("F3"));
            }

            // ② タイトルで仮想のゲームパッド
            R.ShowTitle();
            yield return Frames(3);
            yield return Capture("scr.title");
            yield return PadCheck();

            // ③ 新規ゲーム → 1 年度
            R.Show(new NewGameScreen(), true);
            yield return Frames(2);
            yield return Capture("scr.newgame");
            for (int k = 0; k < 3 && R.Current is NewGameScreen; k++) { R.Current.AutoDefault(); yield return Frames(2); }
            while (R.Busy) yield return null;
            W("new_game_s " + NewGameScreen.LastGenerationSeconds.ToString("F3"));
            if (R.Context.App.Host == null) { W("新規ゲームに失敗"); Finish(2); yield break; }
            yield return Capture("scr.home");
            yield return Visit();

            int startYear = R.Context.S.Year;
            string savedAt = null;
            int steps = 0;
            while (steps++ < 4000 && !_finished)
            {
                yield return Frames(1);
                while (R.Busy) yield return null;
                string id = R.CurrentDialog ?? R.Current.Id;
                yield return Capture(id);
                if (Heavy.Contains(id) && !_perf.ContainsKey(id)) yield return Measure(id);
                if (savedAt == null && R.CurrentDialog == null && id == "scr.report.month" && R.Context.S.YearWeek >= 20)
                {
                    if (R.Context.Host.SaveManual()) savedAt = R.Context.S.Year + "-" + R.Context.S.YearWeek;
                    W("manual_save " + (savedAt ?? "失敗"));
                    CopyDir(SaveRoot, SaveRoot + "_mid");                                 // 年度の途中のセーブ(自動 A / B と手動)を退避する
                }
                if (R.Context.S.Year > startYear && R.CurrentStop != null && R.CurrentStop.Kind != StopKind.YearEndTurnover && !(R.Current is EnrollmentScreen) && !(R.Current is GraduationScreen)) break;
                Step();
            }
            if (_finished) yield break;
            W("steps " + steps + " / year " + R.Context.S.Year + " / stop " + (R.CurrentStop != null ? R.CurrentStop.Key : "-"));
            string straight = GameSnapshot.Capture(R.Context.S).ToJson();
            string stopKey = R.CurrentStop != null ? R.CurrentStop.Key : "";

            // ⑥ 解像度と文字の倍率(年度 2 の初め。主要な画面)
            yield return Resolutions();

            // ⑦ 保存 → 開く → 同じ節目まで
            bool same = false;
            if (savedAt != null)
            {
                R.ShowTitle();                                                         // いまのプレイを閉じ、セーブを年度の途中の状態に戻して『続きから』
                Directory.Delete(SaveRoot, true);
                CopyDir(SaveRoot + "_mid", SaveRoot);
                LoadScreen.OpenSlot(R, 1);
                W("reopened " + R.Context.S.Year + "-" + R.Context.S.YearWeek);
                yield return Frames(2);
                int s2 = 0;
                while (s2++ < 4000 && !_finished)
                {
                    yield return Frames(1);
                    while (R.Busy) yield return null;
                    if (R.Context.S.Year > startYear && R.CurrentStop != null && R.CurrentStop.Key == stopKey && !(R.Current is EnrollmentScreen) && !(R.Current is GraduationScreen)) break;
                    Step();
                }
                string resumed = GameSnapshot.Capture(R.Context.S).ToJson();
                same = resumed == straight;
                int d = 0;
                while (d < Math.Min(resumed.Length, straight.Length) && resumed[d] == straight[d]) d++;
                W("save_open_continue " + (same ? "一致" : "不一致 位置 " + d + " / 長さ " + straight.Length + " と " + resumed.Length) + "  (保存 " + savedAt + "・比較の節目 " + stopKey + "・手数 " + s2 + ")");
                if (!same) W("  保存なし: …" + Mid(straight, d) + "\n  開いた後: …" + Mid(resumed, d));
            }
            Memory();
            W("screens " + string.Join(" → ", _screens));
            foreach (var kv in _perf) W("perf " + kv.Key + " " + kv.Value);
            W("errors " + _errors);
            foreach (string e in _errorLines) W("  " + e);
            Finish(_errors == 0 && same ? 0 : 1);
        }

        private float _deadline;

        /// <summary>既定の操作を 1 つ(例外は数えて続ける。同じ画面で止まり続けないよう、時間の上限で打ち切る)。</summary>
        private void Step()
        {
            if (Time.realtimeSinceStartup > _deadline) { W("時間の上限で打ち切り(" + (R.CurrentDialog ?? R.Current.Id) + ")"); Finish(3); return; }
            try
            {
                if (R.DialogOpen) R.PressDialogDefault();
                else R.Current.AutoDefault();
            }
            catch (Exception e) { Debug.LogException(e); R.Home(); }
        }

        private string SaveRoot { get { return ((UnityFileStore)R.Context.App.Env.Files).Root; } }

        private static void CopyDir(string from, string to)
        {
            if (Directory.Exists(to)) Directory.Delete(to, true);
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dst = Path.Combine(to, f.Substring(from.Length).TrimStart('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
        }

        private static string Mid(string s, int i) { int a = Math.Max(0, i - 80); return s.Substring(a, Math.Min(200, s.Length - a)); }

        /// <summary>ホームから入口の画面を一巡(撮影・測定)して戻る。</summary>
        private IEnumerator Visit()
        {
            ScreenBase[] list = { new PlanScreen(), new RosterScreen(), new AnnualScreen(), new ScoutMapScreen(), new ScoutListScreen(), new OpsScreen(), new HistoryScreen(), new NotificationsScreen(), new RunnerScreen { RunnerId = R.Context.S.RosterRows()[0].Id } };
            foreach (ScreenBase s in list)
            {
                R.Show(s, true);
                yield return Frames(2);
                yield return Capture(s.Id);
                if (Heavy.Contains(s.Id) && !_perf.ContainsKey(s.Id)) yield return Measure(s.Id);
                yield return FocusWalk(s.Id);
                R.Back();
                yield return Frames(1);
            }
            R.Home();
            yield return Frames(1);
        }

        private static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        private IEnumerator Capture(string id)
        {
            if (_captured.Contains(id)) yield break;
            _captured.Add(id);
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_dir, (_captured.Count).ToString("00") + "_" + id + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
        }

        /// <summary>p95 のフレーム時間(何もしない表示)と、画面の組み立て(Rebuild)の時間。</summary>
        private IEnumerator Measure(string id)
        {
            var frames = new List<double>();
            for (int i = 0; i < 120; i++) { yield return null; frames.Add(Time.unscaledDeltaTime * 1000.0); }
            var builds = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                R.Rebuild();
                builds.Add(sw.Elapsed.TotalMilliseconds);
                yield return null;
            }
            var after = new List<double>();
            for (int i = 0; i < 60; i++) { yield return null; after.Add(Time.unscaledDeltaTime * 1000.0); }
            _perf[id] = "frame_p95_ms " + P95(frames).ToString("F2") + " / build_p95_ms " + P95(builds).ToString("F2") + " / frame_after_build_p95_ms " + P95(after).ToString("F2")
                + " / gc_mb " + (GC.GetTotalMemory(false) / 1048576.0).ToString("F1") + " / reserved_mb " + (Profiler.GetTotalReservedMemoryLong() / 1048576.0).ToString("F1");
        }

        private static double P95(List<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s[Math.Min(s.Count - 1, (int)Math.Ceiling(s.Count * 0.95) - 1)];
        }

        private void Memory()
        {
            W("memory gc_mb " + (GC.GetTotalMemory(false) / 1048576.0).ToString("F1") + " / allocated_mb " + (Profiler.GetTotalAllocatedMemoryLong() / 1048576.0).ToString("F1")
                + " / reserved_mb " + (Profiler.GetTotalReservedMemoryLong() / 1048576.0).ToString("F1") + " / mono_heap_mb " + (Profiler.GetMonoHeapSizeLong() / 1048576.0).ToString("F1"));
            try { W("working_set_mb " + (Process.GetCurrentProcess().WorkingSet64 / 1048576.0).ToString("F1")); }
            catch (Exception e) { W("working_set_mb 取れない(" + e.GetType().Name + ")"); }
        }

        /// <summary>十字キー(仮想のゲームパッドの十字を押す = Input System → UiRoot → grid)で、すべての領域に届くか(08 §3.1・§11.1)。</summary>
        private IEnumerator FocusWalk(string id)
        {
            FocusGrid<VisualElement> g = R.ActiveGrid;
            if (g == null || R.Current == null) yield break;
            var seen = new HashSet<string>();
            var want = new HashSet<string>();
            foreach (string name in new[] { "header", "steps", "body", "status", "next", "menu", "groups", "forecast", "detail", "tabs", "list", "side", "focus", "target", "peaking", "map", "points", "slots", "actions", "summary", "roster", "entered", "sections", "candidates", "stats" })
            {
                var r = g.Find(name);
                if (r != null && r.Items.Count > 0) want.Add(name);
            }
            VisualElement panelRoot = _root.GetComponent<UIDocument>().rootVisualElement;
            GamepadButton[] seq = { GamepadButton.DpadUp, GamepadButton.DpadUp, GamepadButton.DpadUp, GamepadButton.DpadDown, GamepadButton.DpadRight,
                GamepadButton.DpadDown, GamepadButton.DpadRight, GamepadButton.DpadDown, GamepadButton.DpadLeft, GamepadButton.DpadDown, GamepadButton.DpadRight, GamepadButton.DpadDown, GamepadButton.DpadDown };
            int mismatch = 0, nullPanel = 0;
            seen.Add(R.FocusedRegion ?? "");
            foreach (var d in seq)
            {
                for (int rep = 0; rep < 25; rep++)
                {
                    string before = R.FocusedRegion;
                    if (_pad == null) { W("focus " + id + " 仮想のパッドがない"); yield break; }
                    yield return Press(_pad, d);
                    seen.Add(R.FocusedRegion ?? "");
                    var fc = panelRoot != null && panelRoot.panel != null ? panelRoot.panel.focusController : null;
                    if (fc == null) nullPanel++;
                    else if (fc.focusedElement != R.Focused) mismatch++;
                    if (R.FocusedRegion != before) break;
                }
            }
            var missing = want.Where(x => !seen.Contains(x)).ToList();
            W("focus " + id + " 届いた " + string.Join(",", seen.Where(x => x.Length > 0)) + (missing.Count > 0 ? " / 届かない " + string.Join(",", missing) : " / すべて") + (mismatch > 0 ? " / 既定のフォーカスとずれた " + mismatch : "") + (nullPanel > 0 ? " / パネルなし " + nullPanel : ""));
        }

        /// <summary>仮想のゲームパッド(Input System)で: 十字キーの下でフォーカスが動く / A で選択 / B で戻る。</summary>
        private Gamepad _pad;

        private IEnumerator PadCheck()
        {
            Gamepad pad = _pad = InputSystem.AddDevice<Gamepad>();
            yield return Frames(2);
            VisualElement before = R.Focused;
            yield return Press(pad, GamepadButton.DpadDown);
            VisualElement moved = R.Focused;
            W("pad dpad_down: " + (moved != before ? "フォーカスが動いた(" + R.FocusedRegion + ")" : "動かない"));
            // 『はじめから』に合わせて A
            R.ShowTitle();
            yield return Frames(2);
            FocusGrid<VisualElement> g = R.ActiveGrid;
            var menu = g.Find("menu");
            if (menu != null && menu.Items.Count > 1) { g.Sync(menu.Items[1]); menu.Items[1].Focus(); }
            yield return Frames(1);
            yield return Press(pad, GamepadButton.South);
            yield return Frames(2);
            W("pad A: " + (R.Current is NewGameScreen ? "新規ゲームが開いた" : "開かない(" + R.Current.Id + ")"));
            yield return Press(pad, GamepadButton.East);
            yield return Frames(2);
            W("pad B: " + (R.Current is TitleScreen ? "タイトルに戻った" : "戻らない(" + R.Current.Id + ")"));
        }

        private static IEnumerator Press(Gamepad pad, GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            yield return null;
        }

        /// <summary>基準キャンバス(高さ 360・画面に合わせて拡大)の確認: 解像度ごと・文字 150% で主要な画面を撮る(RenderTexture に描く)。</summary>
        private IEnumerator Resolutions()
        {
            var doc = _root.GetComponent<UIDocument>();
            PanelSettings ps = doc.panelSettings;
            if (ps == null) { W("resolutions: PanelSettings がない"); yield break; }
            (int w, int h)[] sizes = { (640, 360), (1280, 720), (1920, 1080), (2560, 1440), (720, 1280) };
            Func<ScreenBase>[] screens = { () => new HomeScreen(), () => new PlanScreen(), () => new RosterScreen(), () => new OrderScreen { Kind = Domain.Race.RaceKind.NewYear }, () => new OpsScreen(), () => new HistoryScreen() };
            string dir = Path.Combine(_dir, "res");
            Directory.CreateDirectory(dir);
            foreach (int scale in new[] { 100, 150 })
            {
                R.SetTextScale(scale);
                foreach (var make in screens)
                {
                    ScreenBase s = make();
                    R.Show(s, true);
                    foreach (var (w, h) in sizes)
                    {
                        if (scale == 150 && w != 1280) continue;
                        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                        ps.targetTexture = rt;
                        yield return Frames(3);
                        yield return new WaitForEndOfFrame();
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(dir, s.Id + "_" + w + "x" + h + "_text" + scale + ".png"), tex.EncodeToPNG());
                        Destroy(tex);
                        ps.targetTexture = null;
                        rt.Release();
                        Destroy(rt);
                    }
                }
            }
            R.SetTextScale(100);
            R.Home();
            yield return Frames(2);
            W("resolutions " + string.Join(", ", sizes.Select(x => x.w + "x" + x.h)) + " + 文字 150%(1280x720)を撮影");
        }

        private bool _finished;

        private void Finish(int code)
        {
            if (_finished) return;
            _finished = true;
            File.WriteAllText(Path.Combine(_dir, "auto.txt"), _log.ToString(), new UTF8Encoding(false));
            Application.logMessageReceived -= OnLog;
            Application.Quit(code);
        }
    }
}
