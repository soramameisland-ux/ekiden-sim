using System;
using System.IO;
using System.Text;
using Ekiden.Verification;
using UnityEngine;

namespace Ekiden.DevTools
{
    /// <summary>
    /// 実機・IL2CPP のビルドで、T1 のベクタと T3 の決定性を走らせて結果をファイルに書く(09 §16.1・§16.2・T7。M1 のゲート B)。
    /// 本体は SimCli と共有の <see cref="VectorsRunner"/> と <see cref="DeterminismRunner"/>(<c>Assets/Scripts/Verification</c>)。
    /// ビルドした実行ファイルを <c>-ekidenVerify [出力フォルダ]</c> つきで起動すると、シーンの読み込み前に走って終了する(終了コード 0 = 合格)。
    /// 例: <c>EkidenVerify.exe -batchmode -nographics -ekidenVerify C:\temp\verify</c>
    /// 端末・ビルドをまたいだビット一致は求めない(09 §8.4)。合格の基準は: ベクタが参照モデルと全行一致 / このビルドの中で T3 の ①②③ が一致。SHA-256 は記録用。
    /// </summary>
    public static class VerifyBootstrap
    {
        public const string Arg = "-ekidenVerify";

        /// <summary>これも付けると、T5 の性能の測定(<see cref="PerfRunner"/>)も走って <c>perf.txt</c> に書く(合否には入れない。09 §14.2)。例: <c>EkidenVerify.exe -batchmode -nographics -ekidenVerify C:\temp\verify -ekidenPerf</c></summary>
        public const string PerfArg = "-ekidenPerf";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRun()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, Arg);
            if (i < 0) return;
            string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                ? args[i + 1]
                : Path.Combine(Application.persistentDataPath, "verify");
            int rc;
            try
            {
                rc = RunAll(dir);
                if (Array.IndexOf(args, PerfArg) >= 0) WritePerf(dir);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(dir, "result.txt"), "NG 例外: " + e, new UTF8Encoding(false));
                rc = 2;
            }
            Application.Quit(rc);
        }

        /// <summary>env.txt・vectors.txt・determinism.txt・result.txt を <paramref name="dir"/> に書く。戻り値 0 = T3 がすべて一致。</summary>
        public static int RunAll(string dir, int seeds = 2, int years = 45)
        {
            Directory.CreateDirectory(dir);
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(dir, "env.txt"), Describe(), utf8);
            using (var w = new StreamWriter(Path.Combine(dir, "vectors.txt"), false, utf8) { NewLine = "\n" }) VectorsRunner.Run(w);
            int bad;
            using (var w = new StreamWriter(Path.Combine(dir, "determinism.txt"), false, utf8) { NewLine = "\n" }) bad = DeterminismRunner.Run(w, seeds, years);
            File.WriteAllText(Path.Combine(dir, "result.txt"), bad == 0 ? "OK" : "NG T3 の不一致 " + bad + " 件", utf8);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>T5 の性能の測定を <c>perf.txt</c> に書く。測れなかったら例外の内容を書く(T1・T3 の結果は変えない)。</summary>
        public static void WritePerf(string dir)
        {
            Directory.CreateDirectory(dir);
            var utf8 = new UTF8Encoding(false);
            try
            {
                using (var w = new StreamWriter(Path.Combine(dir, "perf.txt"), false, utf8) { NewLine = "\n" }) PerfRunner.Run(w);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(dir, "perf.txt"), "測定できなかった: " + e, utf8);
            }
        }

        /// <summary>どの環境で走らせたか(記録用)。</summary>
        public static string Describe()
        {
            string backend =
#if ENABLE_IL2CPP
                "IL2CPP";
#elif ENABLE_MONO
                "Mono";
#else
                "不明";
#endif
            var sb = new StringBuilder();
            sb.Append("unity ").AppendLine(Application.unityVersion);
            sb.Append("backend ").AppendLine(backend);
            sb.Append("platform ").AppendLine(Application.platform.ToString());
            sb.Append("debug_build ").AppendLine(Debug.isDebugBuild.ToString());
            sb.Append("os ").AppendLine(SystemInfo.operatingSystem);
            sb.Append("cpu ").Append(SystemInfo.processorType).Append(" x").AppendLine(SystemInfo.processorCount.ToString());
            sb.Append("little_endian ").AppendLine(BitConverter.IsLittleEndian.ToString());
            return sb.ToString();
        }
    }
}
