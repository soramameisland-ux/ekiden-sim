// HTML を Chromium で PDF にする(build_spec_pdf.py から呼ぶ)。余白・用紙は HTML の @page が決める。
// 第 3 引数 tagged を付けると、構造タグとしおり(outline)つきで出る(見出しの頁の測定用。Chromium はタグなしだとしおりを出さない)。
// 付けないと約半分の大きさになる(最終版はこちら。しおりは build_spec_pdf.py が付ける)。
const { chromium } = require('playwright');

(async () => {
  const [, , input, output, mode] = process.argv;
  const tagged = mode === 'tagged';
  if (!input || !output) { console.error('usage: node print_pdf.js <input.html> <output.pdf> [tagged]'); process.exit(2); }
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    await page.goto('file://' + input, { waitUntil: 'load', timeout: 0 });
    await page.evaluate(() => document.fonts.ready);
    await page.pdf({ path: output, preferCSSPageSize: true, printBackground: true, outline: tagged, tagged });
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
