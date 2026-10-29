using System.Collections.Generic;

namespace Ekiden.Unity.UI
{
    public enum NavDir { Up, Down, Left, Right }

    /// <summary>
    /// 十字キーのフォーカスの移動(08 §3・08a の領域 `grid`)。画面の領域を『行の並び(上から下)× 行の中の領域(左から右)』に置き、
    /// 領域の中の項目は縦(リスト)か横(ボタンの並び)に並ぶ。領域の中で動き、端に来たら隣の領域へ移る。
    /// 縦の領域: 上下 = 項目、左右 = 同じ行の隣の領域。横の領域: 左右 = 項目、上下 = 上 / 下の行。
    /// 項目のない領域は飛ばす。UI に依存しない(試験で確かめる)。
    /// </summary>
    public sealed class FocusGrid<T> where T : class
    {
        public sealed class Region
        {
            public string Name;
            public bool Horizontal;
            public readonly List<T> Items = new List<T>();
            public int Row, Col;
        }

        private readonly List<List<Region>> _rows = new List<List<Region>>();
        private Region _cur;
        private int _index;

        /// <summary>新しい行を始める(上から下の順に呼ぶ)。</summary>
        public void NewRow() { _rows.Add(new List<Region>()); }

        /// <summary>いまの行の右に領域を足す。</summary>
        public Region AddRegion(string name, bool horizontal = false)
        {
            if (_rows.Count == 0) NewRow();
            var r = new Region { Name = name, Horizontal = horizontal, Row = _rows.Count - 1, Col = _rows[_rows.Count - 1].Count };
            _rows[_rows.Count - 1].Add(r);
            return r;
        }

        public Region Find(string name)
        {
            foreach (List<Region> row in _rows) foreach (Region r in row) if (r.Name == name) return r;
            return null;
        }

        public T Current { get { return _cur != null && _index < _cur.Items.Count ? _cur.Items[_index] : null; } }
        public string CurrentRegion { get { return _cur != null ? _cur.Name : null; } }

        /// <summary>既定のフォーカス(08a の default_focus)。その領域に項目がなければ、最初の項目のある領域。</summary>
        public T FocusDefault(string region)
        {
            Region r = Find(region);
            if (r != null && r.Items.Count > 0) { _cur = r; _index = 0; return Current; }
            foreach (List<Region> row in _rows) foreach (Region x in row) if (x.Items.Count > 0) { _cur = x; _index = 0; return Current; }
            _cur = null;
            return null;
        }

        /// <summary>マウス・タッチで選んだ項目に合わせる。</summary>
        public bool Sync(T item)
        {
            foreach (List<Region> row in _rows)
                foreach (Region r in row)
                {
                    int i = r.Items.IndexOf(item);
                    if (i >= 0) { _cur = r; _index = i; return true; }
                }
            return false;
        }

        /// <summary>1 つ動かす。動いた先(動けなければ今のまま)。</summary>
        public T Move(NavDir d)
        {
            if (_cur == null) return FocusDefault(null);
            bool along = _cur.Horizontal ? (d == NavDir.Left || d == NavDir.Right) : (d == NavDir.Up || d == NavDir.Down);
            if (along)
            {
                int step = (d == NavDir.Up || d == NavDir.Left) ? -1 : 1;
                int ni = _index + step;
                if (ni >= 0 && ni < _cur.Items.Count) { _index = ni; return Current; }
                if (_cur.Horizontal) return Current;                          // 横の領域の端: 左右は止まる
                return JumpRow(step);                                         // 縦の領域の端: 上 / 下の行へ
            }
            if (d == NavDir.Left || d == NavDir.Right) return JumpCol(d == NavDir.Left ? -1 : 1);
            return JumpRow(d == NavDir.Up ? -1 : 1);
        }

        private T JumpCol(int step)
        {
            List<Region> row = _rows[_cur.Row];
            for (int c = _cur.Col + step; c >= 0 && c < row.Count; c += step)
            {
                if (row[c].Items.Count == 0) continue;
                _cur = row[c];
                _index = System.Math.Min(_index, _cur.Items.Count - 1);
                return Current;
            }
            return Current;
        }

        private T JumpRow(int step)
        {
            int col = _cur.Col;
            for (int r = _cur.Row + step; r >= 0 && r < _rows.Count; r += step)
            {
                List<Region> row = _rows[r];
                Region best = null;
                for (int k = 0; k < row.Count; k++)
                {
                    int c = System.Math.Min(col, row.Count - 1) + (k % 2 == 0 ? k / 2 : -(k / 2 + 1));   // 同じ列から近い順
                    if (c < 0 || c >= row.Count || row[c].Items.Count == 0) continue;
                    best = row[c];
                    break;
                }
                if (best == null) foreach (Region x in row) if (x.Items.Count > 0) { best = x; break; }
                if (best == null) continue;
                _cur = best;
                _index = step > 0 ? 0 : (best.Horizontal ? 0 : best.Items.Count - 1);
                return Current;
            }
            return Current;
        }
    }
}
