#!/usr/bin/env python3
"""仕様書 統合版 PDF のビルド。

    python3 -I specbook/build_spec_pdf.py [--out dist/ekiden_spec_integrated.pdf] [--work DIR] [--html-only]

原典(`00_spec_index.md` `01`〜`10`・`08a`・`08b`)と、統合版のために書いた `specbook/front.md`(全体像)・`specbook/appendix_f.md`(実装との対応)
から、1 冊の A4 の PDF を作る。原典の本文は書き換えない(相互参照の番号がそのまま使える)。変えるのは次の 4 点だけ:
  1. 各文書の先頭の表題を『第N部』の見出しに置き換え、改版メモ(『vN の変更』『NN の反映』)を付録 G に集める。
  2. `00_spec_index.md` の §2〜§7 と検算ツールの説明を付録 A〜E にする。
  3. 目次(頁番号つき)・しおり・ヘッダ/フッタ・表紙を付ける。
  4. 表の列幅(pandoc が付ける colgroup)を外し、内容に合わせて組む。

道具: pandoc(GFM → HTML)、Node の playwright(Chromium で印刷)、pypdf + reportlab(ヘッダ/フッタの押印)。
頁番号は先に測る: 1 回目は目次の頁番号を『000』にして(構造タグつきで)印刷し、PDF のしおりから見出しの頁を読む。2 回目で目次に入れて同じ頁になることを確かめ、
最後にタグなしで印刷して(半分の大きさ)、ヘッダ/フッタとしおりを付ける。
"""
import argparse
import glob
import html
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPECBOOK = ROOT / "specbook"
EDITION = "統合版 第 1 版"
EDITION_DATE = "2026-10-29"
BOOK_TITLE = "大学駅伝 育成シミュレーション(仮題)"
RUNNING_HEAD = BOOK_TITLE + " 仕様書 統合版"

# (部の番号, 見出し, 原典, 版の表記(None なら原典の表題から読む), 短い名前)
PARTS = [
    (1, "基本システム設計書", "01_basic_system_design.md", None, "01"),
    (2, "データ仕様", "02_data_spec.md", None, "02"),
    (3, "育成ルール仕様", "03_training_rules_spec.md", None, "03"),
    (4, "駅伝本番仕様", "04_race_spec.md", None, "04"),
    (5, "世界・スカウト・名声・資金・施設 仕様", "05_world_scout_spec.md", None, "05"),
    (6, "イベント仕様", "06_event_spec.md", None, "06"),
    (7, "特別招聘・追加コンテンツ仕様", "07_special_recruit_spec.md", None, "07"),
    (8, "画面仕様 — 本編", "08_screen_spec.md", None, "08"),
    (8, "画面仕様 — 画面カタログ", "08a_screen_catalog.md", None, "08a"),
    (8, "画面仕様 — 画面ごとの必要データと読み取り API", "08b_screen_data.md", "M1 準備", "08b"),
    (9, "セーブ・技術仕様", "09_save_tech_spec.md", None, "09"),
    (10, "個性・素養仕様", "10_individuality_spec.md", None, "10"),
]

NOTE_RES = [
    re.compile(r"^> (\*\*)?v\d+\.\d+ ?(の変更|からの主な変更)"),
    re.compile(r"^> \*\*\d{2} の反映\*\*"),
]
FENCE_RE = re.compile(r"^\s{0,3}(```|~~~)")
HEAD_RE = re.compile(r"<h([1-6])\s+id=\"([^\"]*)\"[^>]*>(.*?)</h\1>", re.S)
SHORT_CELL = 12          # 空白を除いてこれ以下の短いセルは折り返さない(『SC-10』が SC- と 10 に、『区分』が 区 と 分 に、『1ポイント / 人』が 1ポイ と ント に割れるのを防ぐ)
LONG_TOKEN = 20          # 表のセルの中で、これ以上続く切れ目のない英数字の連なりは、折り返せるようにする(列幅が縮まらず表が頁からはみ出すのを防ぐ)
FONT_CANDIDATES = ["/usr/share/fonts/opentype/ipafont-gothic/ipag.ttf", "/usr/share/fonts/truetype/fonts-japanese-gothic.ttf"]


def log(msg):
    print(f"[specbook] {msg}", flush=True)


# ---------------------------------------------------------------- Markdown の前処理
def read_lines(path):
    return Path(path).read_text(encoding="utf-8").split("\n")


def split_header(lines):
    """先頭の表題(# …)・改版メモ・本文に分ける。改版メモは、最初の『---』か『## 』までの引用行のうち、改版を述べるものだけ。"""
    assert lines[0].startswith("# "), "先頭が表題でない: " + lines[0][:40]
    title = lines[0][2:].strip()
    notes, body = [], [lines[0]]
    in_header = True
    for line in lines[1:]:
        if in_header and (line.strip() == "---" or line.startswith("## ")):
            in_header = False
        if in_header and any(r.match(line) for r in NOTE_RES):
            notes.append(line)
            continue
        body.append(line)
    return title, notes, body[1:]


def drop_leading_rule(lines):
    """表題の直後の『---』を外す(見出しの下線と重なるため)。"""
    out, seen = [], False
    for line in lines:
        if not seen and line.strip() == "---":
            seen = True
            continue
        out.append(line)
    return out


def sections_by_number(lines):
    """`## N. 題` ごとの本文(見出しの行は含めない)。コードの囲みの中は見ない。"""
    out, cur, in_fence = {}, None, False
    for line in lines:
        if FENCE_RE.match(line):
            in_fence = not in_fence
        m = None if in_fence else re.match(r"^## (\d+)\. (.*)$", line)
        if m:
            cur = m.group(1)
            out[cur] = {"title": m.group(2).strip(), "lines": []}
            continue
        if cur is not None:
            out[cur]["lines"].append(line)
    for sec in out.values():
        while sec["lines"] and sec["lines"][-1].strip() in ("", "---"):
            sec["lines"].pop()
        while sec["lines"] and sec["lines"][0].strip() == "":
            sec["lines"].pop(0)
    return out


CODE_SPAN_RE = re.compile(r"(`+)(.+?)\1")
BOLD_RE = re.compile(r"\*\*([^*\n]+?)\*\*")
ANGLE_RE = re.compile(r"<(?!br\s*/?>)(?=[A-Za-z/])")


def prepare_markdown(md):
    """原典を直さずに済むよう、組版の直前に 2 つだけ手当てする(コードの囲み・コードの中は除く)。
    ① 『**…(注)**だけ』のように、閉じの ** の前が記号・後ろが日本語の文字だと、CommonMark の規則では強調と見なされず ** が文字のまま出る → <strong> に置き換える。
    ② 表の型に書かれた list<string> や map<id, int> の <…> は、HTML のタグと見なされて消える → < をエスケープする(<br> は原典が改行に使っているので残す)。"""
    out, in_fence = [], False
    for line in md.split("\n"):
        if FENCE_RE.match(line):
            in_fence = not in_fence
        if in_fence or ("**" not in line and "<" not in line):
            out.append(line)
            continue
        spans = []
        masked = CODE_SPAN_RE.sub(lambda m: (spans.append(m.group(0)), f"\x00{len(spans) - 1}\x00")[1], line)
        masked = ANGLE_RE.sub(r"\\<", masked)
        masked = BOLD_RE.sub(r"<strong>\1</strong>", masked)
        out.append(re.sub(r"\x00(\d+)\x00", lambda m: spans[int(m.group(1))], masked))
    return "\n".join(out)


def pandoc(md, prefix):
    """GFM → HTML 片。表の colgroup は外す(列幅は内容で決める)。"""
    md = prepare_markdown(md)
    res = subprocess.run(
        ["pandoc", "-f", "gfm", "-t", "html5", "--wrap=none", f"--id-prefix={prefix}-"],
        input=md.encode("utf-8"), stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if res.returncode != 0:
        raise RuntimeError("pandoc 失敗(" + prefix + "): " + res.stderr.decode("utf-8", "replace")[:400])
    out = res.stdout.decode("utf-8")
    out = re.sub(r"<colgroup>.*?</colgroup>\s*", "", out, flags=re.S)
    return mark_long_cells(out)


CELL_RE = re.compile(r"<(td|th)((?:\s[^>]*)?)>(.*?)</\1>", re.S)
LONG_RUN_RE = re.compile(r"[\x21-\x7e]{%d,}" % LONG_TOKEN)


def mark_long_cells(h):
    """表のセルの折り返しを、内容で決める。切れ目のない長い英数字(パス・識別子の連なり)を含むセルは class="brk"(CSS: どこでも折り返して列幅を縮める)、
    短いセルは class="nw"(折り返さない。列が 8 以上ある表は、頁からはみ出さないよう短さの基準を 8 に下げる)。
    それ以外は、識別子を途中で折らない(Motivati/on のようにならない)。"""
    def table(tm):
        t = tm.group(0)
        limit = SHORT_CELL if len(re.findall(r"<th[ >]", t.split("</tr>", 1)[0])) < 8 else 8

        def sub(m):
            text = html.unescape(re.sub(r"<[^>]+>", "", m.group(3))).strip()
            cls = "brk" if LONG_RUN_RE.search(text) else ("nw" if 0 < len(re.sub(r"\s+", "", text)) <= limit else "")
            if not cls:
                return m.group(0)
            attrs = m.group(2)
            attrs = attrs.replace('class="', f'class="{cls} ', 1) if 'class="' in attrs else attrs + f' class="{cls}"'
            return f"<{m.group(1)}{attrs}>{m.group(3)}</{m.group(1)}>"
        return CELL_RE.sub(sub, t)
    return re.sub(r"<table>.*?</table>", table, h, flags=re.S)


def esc(s):
    return html.escape(s, quote=True)


def plain(s):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", s))).strip()


# ---------------------------------------------------------------- 原稿の組み立て
def h1(hid, text, extra_class=""):
    cls = f' class="{extra_class}"' if extra_class else ""
    return f'<h1 id="{hid}"{cls}>{esc(text)}</h1>'


def docmeta(src, version, gref):
    items = [f"原典: <code>{esc(src)}</code>"]
    if version:
        items.append(f"原典の版: {esc(version)}")
    items.append(f"改版の経緯: 付録 {esc(gref)}" if gref else "改版の経緯: なし(初版のまま)")
    return '<p class="docmeta">' + "".join(f"<span>{i}</span>" for i in items) + "</p>"


def build_parts(show_headers=False):
    """各部の HTML と、付録 G に回す改版メモを返す。"""
    blocks, g_entries = [], []
    for idx, (no, name, fname, ver, short) in enumerate(PARTS, 1):
        title, notes, body = split_header(read_lines(ROOT / fname))
        body = drop_leading_rule(body)
        version = ver or (re.search(r"v\d+\.\d+", title).group(0) if re.search(r"v\d+\.\d+", title) else None)
        gref = None
        if notes:
            gref = f"G.{len(g_entries) + 1}"
            g_entries.append((f"第{no}部 {name}({short})", fname, notes))
        if show_headers:
            print(f"--- {fname}: {len(notes)} 件の改版メモを分離。冒頭:")
            print("\n".join(l[:110] for l in body[:6]))
        text = f"第{no}部 {name}"
        blocks.append(h1(f"part-{short}", text, "part") + docmeta(fname, version, gref) + pandoc("\n".join(body), f"p{short}"))
    return blocks, g_entries


def split_h1_chunks(md):
    """front.md を # 見出しごとに分ける(コードの囲みの中の # は見ない)。"""
    chunks, cur, in_fence = [], [], False
    for line in md.split("\n"):
        if FENCE_RE.match(line):
            in_fence = not in_fence
        if not in_fence and line.startswith("# ") and cur:
            chunks.append("\n".join(cur))
            cur = []
        cur.append(line)
    chunks.append("\n".join(cur))
    return chunks


def appendix_blocks(g_entries):
    idx_lines = read_lines(ROOT / "00_spec_index.md")
    secs = sections_by_number(idx_lines)
    for k in ("1", "2", "3", "4", "5", "6", "7"):
        assert k in secs, "00_spec_index.md に § " + k + " が無い"
    out = []

    def md(lines):
        return "\n".join(lines)

    # A 用語集
    out.append(h1("app-a", "付録A 用語集") + pandoc(md(secs["4"]["lines"]), "aA"))
    # B 完了の基準・書式規約
    out.append(h1("app-b", "付録B 仕様完了の基準と書式規約")
               + pandoc(f"## B.1 {secs['2']['title']}\n\n" + md(secs["2"]["lines"]) + f"\n\n## B.2 {secs['3']['title']}\n\n" + md(secs["3"]["lines"]), "aB"))
    # C 未決トラッカー
    out.append(h1("app-c", "付録C " + secs["5"]["title"]) + pandoc(md(secs["5"]["lines"]), "aC"))
    # D 課題トラッカー・拡張メモ
    out.append(h1("app-d", "付録D 課題トラッカーと拡張メモ")
               + pandoc(f"## D.1 {secs['6']['title']}\n\n" + md(secs["6"]["lines"]) + f"\n\n## D.2 {secs['7']['title']}\n\n" + md(secs["7"]["lines"]), "aD"))
    # E 検算ツール
    s1 = secs["1"]["lines"]
    i_models = next(i for i, l in enumerate(s1) if l.startswith("**検算用の参照モデル**"))
    i_impl = next(i for i, l in enumerate(s1) if l.startswith("**実装**"))
    claude = (ROOT / "CLAUDE.md").read_text(encoding="utf-8")
    m = re.search(r"^## よく使うコマンド\n(.*?)(?=^## )", claude, re.S | re.M)
    assert m, "CLAUDE.md に『よく使うコマンド』が無い"
    e_md = ("## E.1 参照モデル(Python)と検算の実行\n\n" + "\n".join(s1[i_models:i_impl]).rstrip()
            + "\n\n## E.2 C# の実装と実行コマンド\n\n" + "\n".join(s1[i_impl:]).rstrip()
            + "\n\n実行コマンド(`m0/` で。原典 `CLAUDE.md` より):\n\n" + m.group(1).strip() + "\n")
    out.append(h1("app-e", "付録E 検算ツールと実行方法") + pandoc(e_md, "aE"))
    # F 実装との対応
    out.append(h1("app-f", "付録F 仕様と実装(C#)・試験の対応") + pandoc((SPECBOOK / "appendix_f.md").read_text(encoding="utf-8"), "aF"))
    # G 改版履歴
    g = ["各文書の冒頭に書かれていた改版メモ(『vN の変更』『NN の反映』)を、原典の文言のまま集めた。第 1〜10 部の本文からは省いてある。"
         "文書の版は、その文書を大きく改めたときの版で、番号は文書ごとに独立している(本書の版とは別)。\n"]
    for n, (label, fname, notes) in enumerate(g_entries, 1):
        g.append(f"## G.{n} {label}\n\n原典: `{fname}`\n")
        g.extend("- " + l[2:] for l in notes)
        g.append("")
    out.append(h1("app-g", "付録G 改版履歴") + pandoc("\n".join(g), "aG"))
    return out


def cover_html():
    return f"""<section class="cover">
<p class="kicker">SPECIFICATION</p>
<p class="title">大学駅伝 育成シミュレーション<small>(仮題)</small></p>
<div class="bar"></div>
<p class="doc">仕様書 統合版</p>
<p class="edition">{esc(EDITION)}({esc(EDITION_DATE)} 時点)</p>
<div class="contents">
第 0 部 全体像 ／ 第 1〜10 部 各仕様書(基本設計・データ・育成・駅伝本番・世界とスカウト・イベント・特別招聘・画面・セーブと技術・個性と素養)／ 付録 A〜G<br>
原典のリポジトリ <code>ekiden-sim</code>(非公開)の Markdown から <code>specbook/build_spec_pdf.py</code> が生成。仕様の正本は原典。
</div>
</section>"""


def assemble_body(show_headers=False):
    front = split_h1_chunks((SPECBOOK / "front.md").read_text(encoding="utf-8"))
    assert len(front) == 3, "front.md の # 見出しは 3 つのはず(文書情報 / この仕様書の読み方 / 第0部 全体像): %d" % len(front)
    parts, g_entries = build_parts(show_headers)
    pieces = [cover_html(),
              pandoc(front[0], "f1"),
              h1("toc", "目 次", "toc-title") + "@@TOC@@",
              pandoc(front[1], "f2"),
              pandoc(front[2], "f3")]
    pieces += parts + appendix_blocks(g_entries)
    return ascii_ids("\n".join(pieces))


def ascii_ids(body):
    """見出しの id を h0001… に振り直す(日本語の id は PDF の名前が長くなって一部の閲覧ソフトが警告する。原典に内部リンクは無い)。
    見出しの中に a0001… の目印(空の span)も置く: 目次のリンクはこちらへ張る(見出しの上の余白ではなく文字の位置に飛ぶので、頁の先頭に来た見出しでも頁がずれない)。"""
    n = [0]

    def sub(m):
        n[0] += 1
        return f'{m.group(1)}h{n[0]:04d}{m.group(2)}{m.group(3)}<span id="a{n[0]:04d}"></span>'

    return re.sub(r'(<h[1-6]\s+id=")[^"]*(")([^>]*>)', sub, body)


def headings_of(body):
    return [(int(m.group(1)), m.group(2), plain(m.group(3))) for m in HEAD_RE.finditer(body)]


def toc_html(heads, pages):
    rows = []
    for level, hid, text in heads:
        if hid == "toc" or level > 2:
            continue
        pg = str(pages.get(hid, "000"))
        rows.append(f'<div class="t{level}"><a href="#a{esc(hid[1:])}"><span class="tt">{esc(text)}</span><span class="dots"></span><span class="pg">{pg}</span></a></div>')
    return '<div class="toc">' + "\n".join(rows) + "</div>"


def page_html(body, css, toc):
    return ("<!doctype html>\n<html lang=\"ja\"><head><meta charset=\"utf-8\">"
            f"<title>{esc(BOOK_TITLE)} 仕様書 統合版</title><style>\n{css}\n</style></head><body>\n"
            + body.replace("@@TOC@@", toc) + "\n</body></html>\n")


# ---------------------------------------------------------------- 印刷とページ番号
def print_pdf(html_path, pdf_path, tagged):
    t = time.time()
    subprocess.run(["node", str(SPECBOOK / "print_pdf.js"), str(html_path), str(pdf_path)] + (["tagged"] if tagged else []), check=True)
    log(f"印刷 {Path(pdf_path).name}: {time.time() - t:.0f} 秒")


def page_texts(pdf_path):
    """頁ごとの本文(空白を除く)。poppler の pdftotext を使う(頁の区切りは改ページ文字)。"""
    res = subprocess.run(["pdftotext", str(pdf_path), "-"], stdout=subprocess.PIPE, check=True)
    return [re.sub(r"\s+", "", t) for t in res.stdout.decode("utf-8", "replace").split("\f")]


def fix_pages(pdf_path, heads, pages):
    """しおりの頁は、頁の先頭に来た見出し(上の余白が頁をまたぐ)で 1 頁前になることがある。次の頁の先頭がその見出しなら、次の頁にする。
    最後に、全部の見出しが割り当てた頁に載っているか確かめる(載っていなければ止める)。"""
    texts = page_texts(pdf_path)
    fixed, moved, missing = {}, 0, []
    for lvl, hid, title in heads:
        p, key = pages[hid], re.sub(r"\s+", "", title)
        if p < len(texts) and texts[p].startswith(key[:20]) and not texts[p - 1].endswith(key[:20]):   # texts は 0 起点: texts[p] が次の頁
            p, moved = p + 1, moved + 1
        fixed[hid] = p
        if key not in texts[p - 1]:
            missing.append((p, title))
    if missing:
        raise RuntimeError(f"見出しが割り当てた頁に見つからない {len(missing)} 件: {missing[:4]}")
    if moved:
        log(f"頁の先頭に来た見出し {moved} 件を次の頁に直した")
    return fixed


def outline_pages(pdf_path, heads):
    """PDF のしおりから、見出し(出現順)の頁番号を読む。見出しの数と題が合わなければ止める。"""
    from pypdf import PdfReader
    r = PdfReader(str(pdf_path))
    flat = []

    def walk(items):
        for it in items:
            if isinstance(it, list):
                walk(it)
            else:
                flat.append((plain(it.title or ""), r.get_destination_page_number(it) + 1))

    walk(r.outline)
    if len(flat) != len(heads):
        raise RuntimeError(f"しおり {len(flat)} 件 ≠ 見出し {len(heads)} 件")
    bad = [(h[2], f[0]) for h, f in zip(heads, flat) if h[2] != f[0]]
    if bad:
        raise RuntimeError(f"しおりの題が見出しと違う {len(bad)} 件: {bad[:3]}")
    return fix_pages(pdf_path, heads, {h[1]: f[1] for h, f in zip(heads, flat)}), len(r.pages)


def check_toc_links(pdf_path, pages):
    """目次の各行のリンクが、目次に書いた頁へ飛ぶか確かめる(リンクの数と、飛び先の頁のずれ)。"""
    from pypdf import PdfReader
    r = PdfReader(str(pdf_path))
    names = r.named_destinations
    n, bad = 0, []
    for pg in range(2, 12):
        for a in r.pages[pg].get("/Annots", []):
            a = a.get_object()
            if a.get("/Subtype") != "/Link":
                continue
            d = a.get("/Dest") or (a.get("/A") or {}).get("/D")
            if d is None:
                continue
            name = str(d) if not isinstance(d, list) else None                       # 名前は /a0001 の形
            if not name or not name.startswith("/a"):
                continue
            n += 1
            dest = names.get(name)
            got = r.get_destination_page_number(dest) + 1 if dest is not None else None
            want = pages.get("h" + name[2:])
            if got != want:
                bad.append((name, want, got))
    return n, bad


def stamp(pdf_in, pdf_out, heads, pages, total):
    """ヘッダ(書名・現在の部)とフッタ(頁 / 全頁)を余白に押し、しおりを付ける。表紙(1 頁)には押さない。
    日本語フォントは 1 つだけ作って全頁で共有する(頁ごとに merge_page すると、フォントが頁数ぶん複製されて巨大になる)。"""
    from pypdf import PdfReader, PdfWriter
    from pypdf.generic import ArrayObject, DecodedStreamObject, NameObject
    from reportlab.lib.colors import Color
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.pdfgen import canvas
    font = next((p for p in FONT_CANDIDATES if os.path.exists(p)), None) or (glob.glob("/usr/share/fonts/**/ipag.ttf", recursive=True) or [None])[0]
    if not font:
        raise RuntimeError("IPAGothic が見つからない")
    pdfmetrics.registerFont(TTFont("IPAG", font))
    reader = PdfReader(str(pdf_in))
    writer = PdfWriter(clone_from=reader)
    h1s = sorted((pages[hid], text) for lvl, hid, text in heads if lvl == 1)
    ov_path = Path(pdf_out).with_suffix(".overlay.pdf")
    w, h = float(reader.pages[0].mediabox.width), float(reader.pages[0].mediabox.height)
    mm = 72 / 25.4
    c = canvas.Canvas(str(ov_path), pagesize=(w, h))
    ink, rule = Color(.36, .39, .44), Color(.78, .8, .84)
    for p in range(1, total + 1):
        if p > 1:
            part = [t for pg, t in h1s if pg <= p][-1]
            c.setFillColor(ink)
            c.setFont("IPAG", 7.5)
            c.drawString(16 * mm, h - 11.5 * mm, RUNNING_HEAD)
            c.drawRightString(w - 16 * mm, h - 11.5 * mm, part)
            c.setStrokeColor(rule)
            c.setLineWidth(.5)
            c.line(16 * mm, h - 13.5 * mm, w - 16 * mm, h - 13.5 * mm)
            c.line(16 * mm, 13 * mm, w - 16 * mm, 13 * mm)
            c.drawCentredString(w / 2, 8.5 * mm, f"{p} / {total}")
        c.showPage()
    c.save()
    ov = PdfReader(str(ov_path))
    ov_fonts = ov.pages[1]["/Resources"]["/Font"]
    font_refs = {}                                                                        # 重ね書きのフォントは 1 回だけ複製して全頁で共有する
    rename = {}
    for k, key in enumerate(sorted(ov_fonts.keys())):
        rename[key[1:].encode()] = f"OvF{k}".encode()
        font_refs[f"/OvF{k}"] = ov_fonts.raw_get(key).clone(writer)
    q_ref, big_q_ref = (writer._add_object(DecodedStreamObject()) for _ in range(2))
    q_ref.get_object().set_data(b"q\n")
    big_q_ref.get_object().set_data(b"\nQ\n")
    for i, page in enumerate(writer.pages):
        if i == 0:
            continue
        stamp_bytes = re.sub(rb"/(F\d+(?:\+\d+)?) ", lambda m: b"/" + rename[m.group(1)] + b" ", ov.pages[i].get_contents().get_data())
        st = DecodedStreamObject()
        st.set_data(stamp_bytes)
        contents = page[NameObject("/Contents")]
        orig = list(contents) if isinstance(contents, ArrayObject) else [page.raw_get("/Contents")]
        page[NameObject("/Contents")] = ArrayObject([q_ref] + orig + [big_q_ref, writer._add_object(st)])
        fonts = page["/Resources"]["/Font"]
        for name, ref in font_refs.items():
            fonts[NameObject(name)] = ref
    # しおり(見出し 1〜3 階層)
    stack = {}
    for lvl, hid, text in heads:
        if lvl > 3:
            continue
        parent = next((stack[l] for l in range(lvl - 1, 0, -1) if l in stack), None)
        stack[lvl] = writer.add_outline_item(text, pages[hid] - 1, parent=parent)
        for l in [l for l in stack if l > lvl]:
            del stack[l]
    writer.add_metadata({"/Title": f"{BOOK_TITLE} 仕様書 統合版", "/Subject": f"{EDITION}({EDITION_DATE} 時点)", "/Creator": "specbook/build_spec_pdf.py"})
    writer.page_mode = "/UseOutlines"
    with open(pdf_out, "wb") as f:
        writer.write(f)
    ov_path.unlink()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=str(ROOT / "dist" / "ekiden_spec_integrated.pdf"))
    ap.add_argument("--work", default=None, help="中間ファイルの置き場(省略時は一時ディレクトリ)")
    ap.add_argument("--html-only", action="store_true", help="HTML だけ作って止まる(確認用)")
    ap.add_argument("--show-headers", action="store_true", help="各文書の先頭(改版メモを外したあと)を表示")
    args = ap.parse_args()
    if args.html_only and not args.work:
        ap.error("--html-only は --work(HTML を置く場所)と一緒に使う")

    tmp = None if args.work else tempfile.TemporaryDirectory(prefix="specbook-")        # --work を省くと、中間ファイルは終わりに消える
    try:
        return build(args, Path(args.work or tmp.name))
    finally:
        if tmp:
            tmp.cleanup()


def build(args, work):
    work.mkdir(parents=True, exist_ok=True)
    css = (SPECBOOK / "spec.css").read_text(encoding="utf-8")
    t0 = time.time()
    body = assemble_body(args.show_headers)
    heads = headings_of(body)
    log(f"原稿 {len(body) / 1e6:.2f} MB・見出し {len(heads)} 件({time.time() - t0:.0f} 秒)")
    page1 = work / "book.html"
    page1.write_text(page_html(body, css, toc_html(heads, {})), encoding="utf-8")
    if args.html_only:
        log(f"HTML: {page1}")
        return 0

    pdf1 = work / "pass1.pdf"
    print_pdf(page1, pdf1, tagged=True)
    pages, total = outline_pages(pdf1, heads)
    pdf2 = work / "pass2.pdf"
    for attempt in range(3):
        page1.write_text(page_html(body, css, toc_html(heads, pages)), encoding="utf-8")
        print_pdf(page1, pdf2, tagged=True)
        new_pages, total = outline_pages(pdf2, heads)
        if new_pages == pages:
            break
        log("目次を入れたら頁がずれた。もう一度")
        pages = new_pages
    else:
        raise RuntimeError("目次の頁番号が落ち着かない")
    final = work / "final_untagged.pdf"
    print_pdf(page1, final, tagged=False)                                           # 同じ HTML をタグなしで(約半分の大きさ)
    from pypdf import PdfReader
    if len(PdfReader(str(final)).pages) != total:
        raise RuntimeError("タグなしの印刷で頁数が変わった")
    n_links, bad_links = check_toc_links(final, pages)
    log(f"目次のリンク {n_links} 件(飛び先のずれ {len(bad_links)} 件{': ' + str(bad_links[:3]) if bad_links else ''})")
    if bad_links or n_links == 0:
        raise RuntimeError("目次のリンクが合わない")
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    stamp(final, out, heads, pages, total)
    log(f"出力: {out}({total} 頁・{out.stat().st_size / 1e6:.1f} MB・{time.time() - t0:.0f} 秒)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
