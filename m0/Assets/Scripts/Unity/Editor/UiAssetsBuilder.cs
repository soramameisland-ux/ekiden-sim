using System.IO;
using System.Linq;
using Ekiden.Unity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Ekiden.Unity.Editor
{
    /// <summary>
    /// UI の資産を作る(Unity の資産は手で YAML を書かず、ここで作る)。メニュー Ekiden → UI → Build UI assets。
    /// バッチ: <c>-executeMethod Ekiden.Unity.Editor.UiAssetsBuilder.BuildBatch</c>。
    /// ① 日本語のフォント資産(TextCore の動的な FontAsset。Noto Sans JP・OFL)② PanelTextSettings ③ PanelSettings(画面サイズに合わせて拡大・基準の高さ 360)
    /// ④ シーン Assets/Scenes/Main.unity(GameBootstrap + UIDocument + UiRoot)を作り、ビルドの最初のシーンにする。
    /// </summary>
    public static class UiAssetsBuilder
    {
        public const string FontRegular = "Assets/Fonts/NotoSansJP-Regular.otf";
        public const string FontBold = "Assets/Fonts/NotoSansJP-Bold.otf";
        public const string FontAssetRegular = "Assets/Fonts/NotoSansJP-Regular-SDF.asset";
        public const string FontAssetBold = "Assets/Fonts/NotoSansJP-Bold-SDF.asset";
        public const string TextSettingsPath = "Assets/UI/EkidenTextSettings.asset";
        public const string PanelSettingsPath = "Assets/UI/EkidenPanelSettings.asset";
        public const string ThemePath = "Assets/UI/EkidenTheme.tss";
        public const string ShellPath = "Assets/UI/Shell.uxml";
        public const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Ekiden/UI/Build UI assets")]
        public static void Build()
        {
            FontAsset regular = EnsureFontAsset(FontRegular, FontAssetRegular);
            FontAsset bold = EnsureFontAsset(FontBold, FontAssetBold);
            if (regular.fallbackFontAssetTable == null) regular.fallbackFontAssetTable = new System.Collections.Generic.List<FontAsset>();
            EditorUtility.SetDirty(regular);

            var text = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (text == null)
            {
                text = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(text, TextSettingsPath);
            }
            text.defaultFontAsset = regular;
            EditorUtility.SetDirty(text);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.textSettings = text;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(640, 360);              // 08 §2 K_UI_REF_H = 360・K_UI_REF_W = 640
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 1f;                                                    // 高さに合わせる(広い画面は左右に余白)
            panel.clearColor = true;
            panel.colorClearValue = new Color(0.086f, 0.102f, 0.129f, 1f);
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();

            BuildScene(panel);
            Debug.Log("Ekiden UI 資産: フォント・PanelSettings・シーンを作った");
        }

        /// <summary>Windows のプレイヤー(IL2CPP)を作る。<c>Builds/ekiden-win/Ekiden.exe</c>。M1 の自動進行・撮影・測定に使う(<c>-ekidenAuto</c>)。</summary>
        [MenuItem("Ekiden/UI/Build Windows player (IL2CPP)")]
        public static void BuildPlayer()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(root, "Builds", "ekiden-win");
            Directory.CreateDirectory(outDir);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            var options = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = Path.Combine(outDir, "Ekiden.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(options);
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log("Ekiden プレイヤー: " + report.summary.result + "  " + options.locationPathName);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void BuildBatch()
        {
            Build();
            EditorApplication.Exit(0);
        }

        private static FontAsset EnsureFontAsset(string fontPath, string assetPath)
        {
            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
            if (fa != null) return fa;
            AssetDatabase.ImportAsset(fontPath);
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null) throw new FileNotFoundException("フォントが読めない: " + fontPath);
            // 動的(使う文字を実行時に足す)。CJK は字数が多いので複数のアトラスを許す
            fa = FontAsset.CreateFontAsset(font, 42, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTextures[0].name = fa.name + " Atlas";
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        private static void BuildScene(PanelSettings panel)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);     // NewScene が使っていない資産を解放するので、前に読んだ参照は無効になる(読み直す)
            var cam = new GameObject("Main Camera");
            var c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;
            cam.tag = "MainCamera";

            var go = new GameObject("Ekiden");
            var boot = go.AddComponent<GameBootstrap>();
            boot.strings = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(Strings.AssetPath);
            var doc = go.AddComponent<UIDocument>();
            // 代入だけではシーンに保存されない(m_PanelSettings が空のまま = パネルができず何も描かれない)ので、SerializedObject で書く
            var so = new SerializedObject(doc);
            so.FindProperty("m_PanelSettings").objectReferenceValue = panel;
            so.FindProperty("sourceAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ShellPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<UiRoot>();

            // パッド・キーボード → UI Toolkit の Navigation / Submit / Cancel(Input System)
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var module = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            module.AssignDefaultActions();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }
    }
}
