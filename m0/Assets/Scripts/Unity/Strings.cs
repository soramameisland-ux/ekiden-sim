using System.Collections.Generic;
using System.IO;
using Ekiden.Persistence.Strings;
using Ekiden.Platform;
using UnityEngine;

namespace Ekiden.Unity
{
    /// <summary>
    /// 文字列テーブルの読み込み(09 §12.3)。正は <c>Assets/Strings/ja.json</c> の 1 つだけで、ビルドには TextAsset として入る(シーンの <see cref="GameBootstrap"/> が参照を持つ)。
    /// 写しを別に持たない。欠落は開発ビルドで <c>⟦キー⟧</c>、製品で <c>？</c>(<see cref="TableLocalization"/>)。
    /// </summary>
    public static class Strings
    {
        /// <summary>プロジェクトの中の表のパス(Editor・試験から読む)。</summary>
        public const string AssetPath = "Assets/Strings/ja.json";

        public static Dictionary<string, string> Parse(string json) { return StringTable.Parse(json); }

        public static Dictionary<string, string> FromAsset(TextAsset asset) { return StringTable.Parse(asset.text); }

        /// <summary>Editor から: プロジェクトの <see cref="AssetPath"/> を読む(試験・道具用。ビルドでは使えない)。</summary>
        public static Dictionary<string, string> FromProject()
        {
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, AssetPath);
            return StringTable.Parse(File.ReadAllText(full));
        }

        public static ILocalization Localization(IReadOnlyDictionary<string, string> table)
        {
            return new TableLocalization(table, Debug.isDebugBuild, m => Debug.LogWarning(m));
        }
    }
}
