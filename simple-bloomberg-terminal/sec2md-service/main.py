"""
sec2md sidecar — a tiny HTTP wrapper around the Python `sec2md` library so the C# app can turn a
SEC filing into clean markdown before its AI heading-triage runs. One endpoint: POST /convert with
the filing's EDGAR document URL, get back the whole filing as markdown.

Run:  pip install -r requirements.txt
      uvicorn main:app --host 127.0.0.1 --port 8088
The C# side reaches it at Sec2Md:BaseUrl (default http://localhost:8088).
"""
import os
import re

import sec2md
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

app = FastAPI(title="sec2md sidecar")

# ── Markdown table cleanup ─────────────────────────────────────────────────────
# sec2md renders SEC financial tables with the dollar sign and parenthesised negatives split into
# their own cells, plus stray empty columns: e.g. `| ( 16,389 | ) |` and `| $ 63,946 | $ | 64,773 |`.
# Short tables survive it, but long ones (balance sheet, cash flows) drift out of column alignment and
# the model mis-reads them. We rebuild each table so every value is one tidy cell under the right
# header: `| Net change... | 11,927 | (16,389) | 20,773 |`.

_SEP = re.compile(r"^:?-{2,}:?$")


def _is_sep_row(cells: list[str]) -> bool:
    non_empty = [c.strip() for c in cells if c.strip()]
    return bool(non_empty) and all(_SEP.match(c) for c in non_empty)


def _split_cells(line: str) -> list[str]:
    s = line.strip()
    if s.startswith("|"):
        s = s[1:]
    if s.endswith("|"):
        s = s[:-1]
    return s.split("|")


def _clean_row(cells: list[str]) -> list[str]:
    out: list[str] = []
    pending_dollar = False
    for c in cells:
        c = c.strip()
        if c == "":
            continue
        if c == "$":                       # lone $ belongs to the next value
            pending_dollar = True
            continue
        if c == ")":                       # orphan ) belongs to the previous value's open paren
            if out:
                out[-1] = out[-1] + ")"
            continue
        if pending_dollar and not c.startswith("$"):
            c = "$" + c
        pending_dollar = False
        c = re.sub(r"\$\s+", "$", c)        # "$ 63,946" -> "$63,946"
        c = re.sub(r"\(\s+", "(", c)        # "( 16,389" -> "(16,389"
        c = re.sub(r"\s+\)", ")", c)        # "16,389 )" -> "16,389)"
        out.append(c)
    return out


def _rebuild_table(block: list[str]) -> list[str]:
    rows = []
    for line in block:
        cells = _split_cells(line)
        if _is_sep_row(cells):             # drop original separators; we regenerate one
            continue
        cleaned = _clean_row(cells)
        if cleaned:
            rows.append(cleaned)
    if not rows:
        return block
    cols = max(len(r) for r in rows)
    out = []
    for idx, r in enumerate(rows):
        out.append("| " + " | ".join(r + [""] * (cols - len(r))) + " |")
        if idx == 0:                       # header gets the separator right after it
            out.append("| " + " | ".join(["---"] * cols) + " |")
    return out


def clean_markdown_tables(md: str) -> str:
    lines = md.split("\n")
    result: list[str] = []
    i = 0
    while i < len(lines):
        if not lines[i].lstrip().startswith("|"):
            result.append(lines[i])
            i += 1
            continue
        block = []
        while i < len(lines) and lines[i].lstrip().startswith("|"):
            block.append(lines[i])
            i += 1
        result.extend(_rebuild_table(block))
    return "\n".join(result)


# SEC requires a descriptive User-Agent on every request; sec2md forwards this to EDGAR.
USER_AGENT = os.environ.get(
    "SEC2MD_USER_AGENT", "simple-bloomberg-terminal lukaosojnikinfo@gmail.com")


class ConvertRequest(BaseModel):
    url: str
    # Reserved: full-markdown conversion (parse_filing) doesn't need it, but the C# side sends the
    # form so a later switch to sec2md.extract_sections can scope to specific Items.
    filing_type: str | None = None


class ConvertResponse(BaseModel):
    markdown: str


@app.post("/convert", response_model=ConvertResponse)
def convert(req: ConvertRequest) -> ConvertResponse:
    try:
        pages = sec2md.parse_filing(req.url, user_agent=USER_AGENT)
    except Exception as e:  # noqa: BLE001 — surface any sec2md/EDGAR failure as 502 for the caller
        raise HTTPException(status_code=502, detail=f"sec2md failed: {e}")

    # Full filing as one markdown string: each Page already carries clean markdown in .content.
    markdown = "\n\n".join(p.content for p in pages)
    markdown = clean_markdown_tables(markdown)   # align split $/() cells so dense tables parse
    return ConvertResponse(markdown=markdown)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}
