using Ekiden.Platform;
using UnityEngine;

namespace Ekiden.Unity
{
    /// <summary>
    /// シーンに 1 つ置く起動の部品。<see cref="GameApp"/> を作り(ファイル = <see cref="UnityFileStore"/>・時計 = <see cref="SystemClock"/>・文字列 = <c>Assets/Strings/ja.json</c>)、
    /// アプリの一時停止・復帰(<c>OnApplicationPause</c> → <c>GameHost.OnAppPause / OnAppResume</c>)と定期的な <c>Tick</c> をつなぐ。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("文字列テーブル(Assets/Strings/ja.json)。写しを作らず、この 1 つを参照する")]
        public TextAsset strings;

        [Tooltip("Tick を呼ぶ間隔(秒)")]
        public float tickInterval = 1f;

        public static GameBootstrap Instance { get; private set; }
        public GameApp App { get; private set; }

        private float _nextTick;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            var table = strings != null ? Strings.FromAsset(strings) : new System.Collections.Generic.Dictionary<string, string>();
            if (strings == null) Debug.LogError("GameBootstrap: 文字列テーブルが設定されていない");
            App = new GameApp(UnityFileStore.Default(), new SystemClock(), table, Strings.Localization(table), Application.version);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + tickInterval;
            App.Tick();
        }

        private void OnApplicationPause(bool paused)
        {
            if (App == null) return;
            if (paused) App.Pause();
            else App.Resume();
        }

        private void OnApplicationQuit()
        {
            if (App != null) App.Pause();
        }
    }
}
