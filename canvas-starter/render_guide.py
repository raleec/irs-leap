"""Create a self-contained, searchable workshop guide from the source handouts."""

from html import escape
from pathlib import Path

ROOT = Path(__file__).resolve().parent
GUIDE = ROOT / "guide"

THEME = """
:root {
  color-scheme: light;
  --cp-bg: #f7f4ef;
  --cp-bg-elevated: #fcfbf8;
  --cp-surface: #ffffff;
  --cp-surface-soft: #f5f5f5;
  --cp-border: #dedede;
  --cp-border-strong: #919191;
  --cp-text: #242424;
  --cp-text-muted: #5c5c5c;
  --cp-text-soft: #6f6f6f;
  --cp-accent: #b11f4b;
  --cp-accent-hover: #9a1a41;
  --cp-accent-soft: rgba(177, 31, 75, 0.08);
  --cp-accent-fg: #ffffff;
  --cp-success: #16a34a;
  --cp-danger: #dc2626;
  --cp-warning: #f59e0b;
  --cp-link: #0078d4;
  --cp-shadow: 0 18px 48px rgba(0, 0, 0, 0.12);
  --cp-overlay: rgba(255, 255, 255, 0.8);
  --cp-panel: rgba(255, 255, 255, 0.86);
  --cp-panel-strong: rgba(255, 255, 255, 0.96);
  --cp-sheen: rgba(255, 255, 255, 0.55);
  --cp-highlight: rgba(177, 31, 75, 0.12);
}
html[data-theme="dark"] {
  color-scheme: dark;
  --cp-bg: #3d3b3a;
  --cp-bg-elevated: #343231;
  --cp-surface: #292929;
  --cp-surface-soft: #2e2e2e;
  --cp-border: #474747;
  --cp-border-strong: #5f5f5f;
  --cp-text: #dedede;
  --cp-text-muted: #919191;
  --cp-text-soft: #b0b0b0;
  --cp-accent: #fd8ea1;
  --cp-accent-hover: #fb7b91;
  --cp-accent-soft: rgba(253, 142, 161, 0.14);
  --cp-accent-fg: #1a1a1a;
  --cp-success: #4ade80;
  --cp-danger: #f87171;
  --cp-warning: #fbbf24;
  --cp-link: #4da6ff;
  --cp-shadow: 0 18px 48px rgba(0, 0, 0, 0.32);
  --cp-overlay: rgba(41, 41, 41, 0.88);
  --cp-panel: rgba(41, 41, 41, 0.72);
  --cp-panel-strong: rgba(41, 41, 41, 0.96);
  --cp-sheen: rgba(255, 255, 255, 0.04);
  --cp-highlight: rgba(253, 142, 161, 0.12);
}
"""


def main():
    sources = [
        (ROOT / "START-HERE.txt", "Start here", "overview"),
        (GUIDE / "RELEASE-1.1.txt", "Release 1.1 - actual components and target setup", "release"),
        (GUIDE / "DIRECTORY-PICKER.txt", "Organization directory picker - release 1.1.0.1", "directory"),
        (GUIDE / "SOLUTIONS-FIRST.txt", "Solutions-first workshop - 150 minutes", "solutions"),
        (GUIDE / "COMPLETE-SOLUTION-PREPARATION.txt", "Rebuild and maintenance reference", "preparation"),
        (GUIDE / "CORE-LAB.txt", "Optional app/flow construction reference", "core"),
        (GUIDE / "Facilitator-Guide.txt", "Detailed facilitator reference", "facilitator"),
        (ROOT / "ADMIN-HANDOFF.txt", "Administrator handoff", "admin"),
        (ROOT / "backend" / "REGISTRATION.txt", "Backend registration", "backend"),
    ]
    sources.extend((path, path.name, f"snippet-{i}") for i, path in enumerate(
        sorted((GUIDE / "Snippets").iterdir())) if path.is_file())
    cards = []
    navigation = []
    for path, title, anchor in sources:
        text = path.read_text(encoding="utf-8")
        navigation.append(f'<a href="#{anchor}">{escape(title)}</a>')
        cards.append(
            f'<details id="{anchor}" data-search="{escape((title + " " + text).lower(), quote=True)}"'
            f'{" open" if anchor in ["overview", "release"] else ""}>'
            f'<summary>{escape(title)}</summary><div class="tools">'
            '<button type="button" class="copy">Copy text</button>'
            '<span class="status" role="status"></span></div>'
            f'<pre tabindex="0">{escape(text)}</pre></details>')
    document = """<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<script>
(() => {
  const param = new URLSearchParams(window.location.search).get("scoutTheme");
  const theme = param || (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
  document.documentElement.setAttribute("data-theme", theme);
})();
</script>
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>IRS LEAP - Canvas Hackathon Guide</title><style>
""" + THEME + """
*{box-sizing:border-box}body{margin:0;background:var(--cp-bg);color:var(--cp-text);
font-family:"Segoe UI",Aptos,Calibri,-apple-system,BlinkMacSystemFont,sans-serif}
header{padding:32px;max-width:1200px;margin:auto}h1{margin:0 0 8px}
.subtitle{color:var(--cp-text-muted);line-height:1.5}
.notice{border-left:4px solid var(--cp-accent);background:var(--cp-accent-soft);padding:16px;border-radius:10px}
.layout{display:grid;grid-template-columns:260px minmax(0,1fr);gap:24px;max-width:1200px;margin:0 auto;padding:0 32px 48px}
nav{position:sticky;top:16px;max-height:90vh;overflow:auto;align-self:start}
nav a{display:block;color:var(--cp-link);text-decoration:none;padding:6px 4px;font-size:14px}
nav a:hover{text-decoration:underline}main{min-width:0}
.searchbar{display:flex;flex-wrap:wrap;gap:8px;margin:16px 0}
input{min-width:200px;flex:1}button,input{font:inherit;border:1px solid var(--cp-border-strong);
border-radius:10px;padding:8px 12px;background:var(--cp-surface);color:var(--cp-text)}
button{cursor:pointer}button:hover{border-color:var(--cp-accent);color:var(--cp-accent)}
details{margin-bottom:16px;border:1px solid var(--cp-border);border-radius:16px;background:var(--cp-surface)}
summary{cursor:pointer;font-weight:600;padding:16px}
.tools{padding:0 16px;display:flex;align-items:center;gap:12px}
.status{font-size:13px;color:var(--cp-text-muted)}
pre{margin:0;padding:16px;overflow:auto;white-space:pre-wrap;overflow-wrap:anywhere;
font-family:Consolas,"Courier New",Courier,monospace;font-size:13px;line-height:1.5}
[hidden]{display:none!important}
@media(max-width:800px){.layout{grid-template-columns:1fr;padding:0 16px 32px}nav{position:static;max-height:200px}header{padding:24px 16px}}
@media print{nav,.searchbar,.tools{display:none}.layout{display:block;padding:0}header{padding:0}
details{border:0;border-radius:0;break-before:page}summary{font-size:18px;padding:0}
pre{font-size:9px;padding:8px 0}body{background:var(--cp-surface)}}
</style></head><body>
<header><h1>IRS LEAP</h1><p class="subtitle">Leave and Exception Approval Portal<br>
Canvas + full Dataverse + Power Automate | Solutions-first workshop kit</p>
<p class="notice"><strong>Import. Configure. Use. Customize. Promote.</strong>
Version 1.1 includes the native Canvas app, stopped approval flow and portable configuration.
Bind target connections, set current values, assign licensed users and rehearse before enabling notifications.
Customer GCC validation remains required. This is not a production-approved IRS system.</p>
<div class="searchbar"><input id="search" type="search" aria-label="Search guide and snippets" placeholder="Search guide or formula">
<button id="expand" type="button">Expand all</button><button id="collapse" type="button">Collapse all</button>
<button id="print" type="button">Print / save PDF</button></div></header>
<div class="layout"><nav aria-label="Guide sections">
""" + "".join(navigation) + '</nav><main>' + "".join(cards) + """</main></div>
<script>
const cards = Array.from(document.querySelectorAll("details"));
document.getElementById("search").addEventListener("input", event => {
  const query = event.target.value.trim().toLowerCase();
  cards.forEach(card => { card.hidden = !!query && !card.dataset.search.includes(query); if(query && !card.hidden) card.open = true; });
});
document.getElementById("expand").addEventListener("click", () => cards.forEach(card => card.open = true));
document.getElementById("collapse").addEventListener("click", () => cards.forEach(card => card.open = false));
document.getElementById("print").addEventListener("click", () => { cards.forEach(card => {card.hidden = false; card.open = true;}); window.print(); });
document.querySelectorAll("nav a").forEach(link => link.addEventListener("click", () => {
 const card = document.getElementById(link.hash.slice(1)); if(card){card.hidden=false;card.open=true;}
}));
document.querySelectorAll(".copy").forEach(button => button.addEventListener("click", async () => {
  const card = button.closest("details"), status = card.querySelector(".status");
  const content = card.querySelector("pre");
  if(navigator.clipboard && window.isSecureContext) {
    try {await navigator.clipboard.writeText(content.textContent); status.textContent="Copied."; return;}
    catch(error) {status.textContent="Clipboard unavailable. Text selected; press Ctrl+C.";}
  } else {status.textContent="Text selected; press Ctrl+C.";}
  const selection = window.getSelection(), range = document.createRange(); range.selectNodeContents(content);
  selection.removeAllRanges(); selection.addRange(range);
}));
</script></body></html>"""
    output = GUIDE / "IRS-LEAP-Workshop-Guide.html"
    output.write_text(document, encoding="utf-8")
    print(f"Rendered {len(cards)} sections: {output}")


if __name__ == "__main__":
    main()
