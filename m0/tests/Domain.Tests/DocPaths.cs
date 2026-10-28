using System;
using System.IO;

namespace Ekiden.Domain.Tests
{
    /// <summary>仕様書(docs/*.md)の場所を、テストの実行場所から上へ辿って探す。</summary>
    public static class DocPaths
    {
        public static string Find(string fileName)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string p = Path.Combine(dir, fileName);
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException(fileName);
        }
    }
}
