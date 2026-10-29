using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Ekiden.Unity.UI
{
    /// <summary>
    /// UI Toolkit の入口(UIDocument と同じ GameObject)。画面のルーターを作り、入力をつなぐ(08 §3.1)。
    /// パッド・キーボードは Input System を直接読む: 十字キー / 左スティック / 矢印 = 画面の grid(08a)で移動(押し続けると繰り返し)、
    /// A / Enter / Space = 選択、B / Esc = 戻る(戻れないホームではポーズ)、Start / F10 = ポーズ、Y = おまかせ。
    /// UI Toolkit 自身の Navigation のイベント(既定のフォーカスの移動)は根元で止める(二重に動かさない)。マウス・タッチは UI Toolkit のまま。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiRoot : MonoBehaviour
    {
        public const float RepeatDelay = 0.4f, RepeatEvery = 0.12f;

        public ScreenRouter Router { get; private set; }

        private NavDir? _held;
        private float _nextRepeat;

        private void Start()
        {
            var doc = GetComponent<UIDocument>();
            VisualElement root = doc.rootVisualElement;
            GameBootstrap boot = GameBootstrap.Instance;
            var ctx = new UiContext { App = boot.App, Loc = boot.App.Loc };
            Router = new ScreenRouter(ctx, root);
            root.RegisterCallback<NavigationMoveEvent>(Swallow, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(Swallow, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(Swallow, TrickleDown.TrickleDown);
            if (!AutoRunner.TryStart(this)) Router.ShowTitle();
        }

        private static void Swallow(EventBase e)
        {
            var panel = (e.target as VisualElement)?.panel;
            if (panel != null) panel.focusController.IgnoreEvent(e);
            e.StopImmediatePropagation();
        }

        private bool Typing
        {
            get
            {
                var f = Router.Focused;
                return f is TextField || (f != null && f.GetFirstAncestorOfType<TextField>() != null);
            }
        }

        private void Update()
        {
            if (Router == null) return;
            Router.Tick();
            Gamepad pad = Gamepad.current;
            Keyboard kb = Keyboard.current;

            // 移動(押した瞬間 + 押し続けの繰り返し)
            NavDir? dir = null;
            if (pad != null)
            {
                Vector2 v = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
                if (v.y > 0.5f) dir = NavDir.Up; else if (v.y < -0.5f) dir = NavDir.Down; else if (v.x < -0.5f) dir = NavDir.Left; else if (v.x > 0.5f) dir = NavDir.Right;
            }
            if (dir == null && kb != null && !Typing)
            {
                if (kb.upArrowKey.isPressed) dir = NavDir.Up; else if (kb.downArrowKey.isPressed) dir = NavDir.Down;
                else if (kb.leftArrowKey.isPressed) dir = NavDir.Left; else if (kb.rightArrowKey.isPressed) dir = NavDir.Right;
            }
            if (dir == null) _held = null;
            else if (_held != dir) { _held = dir; _nextRepeat = Time.unscaledTime + RepeatDelay; Router.Navigate(dir.Value); }
            else if (Time.unscaledTime >= _nextRepeat) { _nextRepeat = Time.unscaledTime + RepeatEvery; Router.Navigate(dir.Value); }

            bool submit = (pad != null && pad.buttonSouth.wasPressedThisFrame) || (kb != null && !Typing && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame));
            bool cancel = (pad != null && pad.buttonEast.wasPressedThisFrame) || (kb != null && kb.escapeKey.wasPressedThisFrame);
            bool pause = (pad != null && pad.startButton.wasPressedThisFrame) || (kb != null && kb.f10Key.wasPressedThisFrame);
            bool omakase = pad != null && pad.buttonNorth.wasPressedThisFrame;
            if (submit) Router.Submit();
            else if (cancel) { if (Router.DialogOpen || Router.CanBack) Router.Back(); else Router.OpenPause(); }
            else if (pause) Router.OpenPause();
            else if (omakase) Router.Omakase();
        }
    }
}
