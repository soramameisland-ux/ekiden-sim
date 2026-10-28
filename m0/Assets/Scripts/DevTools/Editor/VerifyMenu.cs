using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ekiden.DevTools.Editor
{
    /// <summary>
    /// M1 のゲート B の入口(Editor)。メニュー Ekiden/Verify から、T1 のベクタと T3 の決定性を Editor(Mono)で走らせる / Windows の IL2CPP ビルドを作る。
    /// バッチモード: <c>Unity.exe -batchmode -nographics -quit -projectPath m0 -executeMethod Ekiden.DevTools.Editor.VerifyMenu.BuildWindowsIl2cpp</c>
    /// </summary>
    public static class VerifyMenu
    {
        private static string ProjectRoot { get { return Directory.GetParent(Application.dataPath).FullName; } }

        [MenuItem("Ekiden/Verify/Run T1 + T3 in Editor (Mono)")]
        public static void RunInEditor()
        {
            string dir = Path.Combine(ProjectRoot, "Logs", "verify", "editor");
            int rc;
            try
            {
                EditorUtility.DisplayProgressBar("Ekiden 検証", "T1 のベクタと T3 の決定性を実行中(1 分ほどかかります)", 0.5f);
                rc = VerifyBootstrap.RunAll(dir);
            }
            finally { EditorUtility.ClearProgressBar(); }
            Debug.Log("Ekiden 検証(Editor)" + (rc == 0 ? " 合格" : " 不合格") + ": " + dir);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Ekiden 検証(Editor)", (rc == 0 ? "T3 はすべて一致しました。\n" : "T3 に不一致があります。\n") + "出力: " + dir, "OK");
        }

        [MenuItem("Ekiden/Verify/Build Windows IL2CPP player")]
        public static void BuildWindowsIl2cpp()
        {
            string outDir = Path.Combine(ProjectRoot, "Builds", "verify-win-il2cpp");
            Directory.CreateDirectory(outDir);
            // 09 §15.1: スクリプトの実行は IL2CPP(PC も揃える)
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                scenes = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).Take(1).ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "EkidenVerify.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            bool ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log("Ekiden 検証用ビルド: " + report.summary.result + "  " + options.locationPathName);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            else if (!ok) throw new Exception("ビルドに失敗しました: " + report.summary.result + "(Console のエラーを見てください)");
        }
    }
}
