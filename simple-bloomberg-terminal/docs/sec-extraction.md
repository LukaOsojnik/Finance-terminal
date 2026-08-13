# SEC extraction — preserving table structure

How a 10-K becomes chunks for the extraction workers, and why the table handling looks the way it
does. Supersedes the sec2md-sidecar description in `extraction-pipeline.md` /
`extraction-pipeline-v2.md`; the scanning, triage and digest halves of those documents still stand.

## 1. The problem

The extractor's job is to pull revenue / cost / risk sources out of a filing, and most of what it
wants lives in tables. The old pipeline lost those tables twice over:

```
EDGAR HTML  ->  sec2md sidecar (Python)  ->  markdown pipe tables
            ->  FilingSections.ToText()  ->  plain text  ->  chunks  ->  LLM
```

**Loss 1 — markdown cannot express a financial table.** Pipe tables have no `colspan`/`rowspan`, so
a header spanning three year columns, or a two-level header, flattens into ambiguity. Nothing
downstream can recover it, because the information is gone at the format level rather than mangled.

**Loss 2 — `ToText()` deleted the columns entirely.** It inserted a newline at `</tr>` but put
*nothing* between `</td>` cells, then took `InnerText`. A balance-sheet row arrived as:

```
Cash and cash equivalents$ 29,965$ 23,646
```

This path ran whenever the sidecar was unreachable, and it fed `Build()` — the flat scan used by
`ExtractAsync` and by the chat's `RawFallbackAsync`.

Two smaller losses sat on top: a table's caption (`The following table shows net sales by reportable
segment … (dollars in millions):`) is a *separate paragraph*, so chunking could put the units in one
worker call and the figures in another; and row hierarchy (`Total current assets` parenting the
indented rows above it) was carried only by indentation, which did not survive either.

### What was and wasn't actually broken

Worth recording, because it redirected this work. The dumped markdown in `filings/` suggested the
converter was mangling everything — the Apple dump has 334 mojibake sequences (`Shareholdersâ€™`) and
uncollapsed `( 214 )` cells. But that dump predates the sidecar's `clean_markdown_tables` pass. The
later MSFT dump has **zero** mojibake and consistent column counts per table.

So the Python cleanup was working, and simple 3–6 column tables came through fine. The failures were
structural (the four above), not cosmetic — which is why the fix is a format change rather than more
regex repair.

## 2. The pipeline now

```
                    ┌─ Items 1 / 1A / 7 / 7A  (narrative) ─────────────────┐
EDGAR filing HTML ──┤                                                       ├─ chunks ─> LLM workers
                    └─ Item 8 (financial statements) ───────────────────────┘
                          │
                          └─ FilingSummary.xml -> R*.htm  (SEC-rendered)
```

Two sources, because the SEC publishes the financial statements twice: once inside the 10-K document
(styled for humans, hard to parse) and once as generated `R*.htm` reports (built from the submitted
XBRL, one statement per file). Item 8 reads the latter. Everything narrative still reads the document.

### Tables travel as HTML

`FilingTables.Render` turns any `<table>` node into minimal structure-preserving HTML: `colspan` and
`rowspan` kept, everything presentational dropped. Formats were compared before choosing — LLMs read
HTML tables measurably better than markdown ones, and unlike markdown, HTML can actually represent a
merged header cell.

A table is never split across chunks (it is emitted with no internal newlines, so `Paragraphs()` sees
one paragraph and `MaxChunkChars` clipping is bypassed for it), and never separated from its caption
(`Paragraphs()` carries the preceding paragraph into the new chunk when a table starts one).

Layout tables — SEC filings wrap ordinary prose in `<table>` constantly — are detected (fewer than 4
cells, or no numeric cell) and left to flatten into text as before, so their prose still reaches the
model.

### Why the R-files are worth a second fetch

An `R*.htm` carries more than a clean table. From Apple's `R5.htm`:

| What it gives                | Example                                                    |
| ---------------------------- | ---------------------------------------------------------- |
| Real header cells            | `<th>Sep. 30, 2023</th>`                                    |
| **Units, in the title**      | `CONSOLIDATED BALANCE SHEETS - USD ($) $ in Millions`       |
| **us-gaap concept per row**  | `<td c="us-gaap:CashAndCashEquivalentsAtCarryingValue">`    |
| Grouping rows marked         | `…Abstract` concepts are header rows, not figures           |
| Negatives already normalised | `(214)`                                                     |

The concept name is the valuable part: it pins an ambiguous English label to the canonical tagged
concept, which is what `XbrlFacts` and `XbrlInstanceReader` already key on. The label and the
authoritative number now share a vocabulary.

Measured on Apple FY2023 `R5.htm`: **119,221 bytes in → 4,100 bytes out (3.4%)**, whole balance sheet,
nothing dropped. Most of the source is the hidden `authRefData` FASB-definition popups, removed before
rendering.

### Fallbacks

Every new step degrades instead of failing:

- No `FilingSummary.xml` (filings before ~2009) → Item 8 falls back to sequential document chunks.
- A report fetch fails → that report is skipped, the rest proceed.
- `Render` returns `null` → treated as a layout table, prose preserved.

## 3. What changed

**Added**

| File                                             | Role                                                         |
| ------------------------------------------------ | ------------------------------------------------------------ |
| `Services/Extraction/FilingTables.cs`             | HTML table → compact structure-preserving HTML. Pure.        |
| `Services/Clients/Edgar/FilingReportReader.cs`    | `FilingSummary.xml` index + `R*.htm` fetch. Best-effort I/O. |
| `simple-bloomberg-terminal.Tests/FilingTableTests.cs` | 12 tests over the pure parsers and the chunking rules.  |

**Changed**

- `FilingSections.ToText` — lifts data tables out, renders them, splices them back as their own
  paragraphs. This is the fix for Loss 2.
- `FilingSections.Paragraphs` — tables stay atomic and keep their caption.
- `FilingSections.CollectLines` — a table inside a heading body is emitted as one rendered line
  instead of having its cells walked, so MD&A segment tables survive heading-based chunking.
- `FilingSections.SelectReport` / `BuildReports` — new; report selection and Item 8 chunking.
- `FilingExtractionService` — takes `IFilingReportReader` instead of `ISec2MdClient`; `FetchRawAsync`
  reads EDGAR HTML directly; Item 8 prefers report chunks.
- `ExtractionChatService` — same, for `RawFallbackAsync`.

**Removed**

- `sec2md-service/` (the Python FastAPI sidecar and its `sec2md` dependency)
- `Services/Clients/Edgar/Sec2MdClient.cs`, `ISec2MdClient.cs`
- the `Sec2Md` block in `appsettings.json` and its `AddHttpClient` registration

There is no longer a second process to run. `uvicorn main:app --port 8088` is not part of local setup.

`Sec2MdClient` also dumped converted markdown to `filings/` for eyeballing. That is gone, and not
replaced: the scan-progress widget already reports each worker's verbatim prompt and reply
(`ScanProgress.Prompt` / `.Response`), which shows exactly what the model saw — strictly more useful
than a file on disk. The existing `filings/*.md` dumps are stale artefacts of the old pipeline.

`filingType` is accepted by `IFilingExtractionService` and is now read again — see §4a. It originally
scoped the sidecar's conversion, sat dead through the sidecar's removal, and was already threaded
through every layer, so form-specific Item routing needed no signature change past `ItemsFor`.

## 4. Which reports get fetched

`FilingSections.SelectReport` decides this, and it runs *before* any download — Apple's FY2023 filing
indexes **76** reports, so an unselective rule means 76 SEC round-trips per scan.
`FilingReportReader.MaxReports` (20) is a backstop, not the policy.

The rule:

| Category                            | Taken?                          | Why                                                       |
| ----------------------------------- | ------------------------------- | --------------------------------------------------------- |
| `Statements` (6)                    | the income statement only       | the only statement naming revenue and cost lines           |
| `Details` (39)                      | those matching node topic words | where disaggregated revenue / segment cost actually live   |
| `Cover`, `Notes`, `Tables`, `Policies` | none                         | metadata, prose already reaching the model, empty templates |

### Why only one of the statements

Measured on Apple FY2023 and AMD FY2025, both nodes:

| Filer | Node | today | balance sheet + cash flow + equity + comprehensive dropped | saved |
| ----- | ---- | ------------------ | ------------------ | ----- |
| AAPL  | REVENUE | 10 chunks / 36,293 ch | 6 / 17,915 | **50.6%** |
| AAPL  | COST    | 9 / 34,034            | 5 / 15,656 | **54.0%** |
| AMD   | REVENUE | 9 / 39,360            | 5 / 17,353 | **55.9%** |
| AMD   | COST    | 11 / 43,828           | 7 / 21,821 | **50.2%** |

Those four reports are ~half the entire Item 8 payload — 4 of 9–11 worker calls per node, ~4,600–5,500
tokens — and none of them names a revenue or cost line; Apple's balance sheet opens on cash, short-term
investments and receivables. The "anchor totals" they were originally taken for are reconciled in code
by `ExtractionChatService.SumCheck` over tagged XBRL facts, never by the model reading the statement.
The saving is identical for REVENUE and COST because this arm is node-independent.

The income statement is the opposite case — it is the only place the pipeline sees figures `XbrlFacts`
cannot surface at all:

| Line | Apple FY2023 | AMD FY2025 | In `XbrlFacts`? |
| ---- | ------------ | ---------- | --------------- |
| Research and development | 29,915 | 8,091 | no — `Opex` is only `OperatingExpenses` + `SG&A` |
| Restructuring charges | — | 0 (186 in FY24) | no |
| Acquisition-related amortization | — | 1,031 COGS + 1,223 opex | no (`amd:` custom concepts) |
| Cost of sales, product vs service | 189,282 / 24,855 | — | no — `ProductOrServiceAxis` is skipped by `XbrlInstanceReader` |

AMD also tags its main cost line as `CostOfGoodsAndServiceExcludingDepreciationDepletionAndAmortization`,
which is not in `XbrlFacts.Cogs` either.

`IsIncomeStatement` matches "Operations" / "Income" / "Earnings" in `ShortName`, after stripping
"Comprehensive Income" — that is a different statement carrying the word "Income", and some filers
combine the two under one title (`Statements of Operations and Comprehensive Income`), which must still
be kept. It is an include rule on purpose: an exclude rule tolerates unseen titles better but has to
enumerate every non-P&L statement and gets the combined title backwards. The accepted risk is a filer
whose income statement uses none of the three words; the fix there is to widen the word list, not to
invert the rule.

Two cross-cutting exclusions, worth ~a third of the index: `(Parenthetical)` reports carry only share
counts and par values, and every note's `Additional Information` report is a bag of loose scalars.

Topic words are matched against `ShortName`, which filers title conventionally enough to rely on
(`Segment Information and Geographic Data - Net Sales (Details)`):

- **REVENUE** — Revenue, Net Sales, Segment, Geographic, Product, Customer, Disaggregat,
  Performance Obligation, Concentration, Equity Method. The last three are the note topics §4a routes
  Item 8 for: remaining performance obligations (ASC 606), major-customer concentration
  (ASC 275-10-50), equity-method investments and JV partners (ASC 323). `Contract` is deliberately not
  a keyword — it also matches the contractual-obligation maturity tables, which are liabilities.
- **COST** — Cost, Expense, Operating Income, Segment, Geographic, Supply, Purchase Obligation
- **RISK** — none. Its Items are 1A/7A, which are narrative; every rendered report is a financial
  statement. `ScanAutoAsync` only asks for reports when the node's items include 8, so RISK never
  reaches the predicate anyway.

On Apple FY2023 this selects **~12 reports for REVENUE and ~13 for COST**, both comfortably inside
the cap.

## 4a. Which Items the revenue workers get

`FilingSections.ItemsFor(node, form)` decides this. It is the static prior for the whole pipeline —
the LLM triage in `TriageHeadingsAsync` only ever picks *within* the Items routed here, so an Item
left out is invisible to every downstream stage.

### 10-K

| Item | Why |
| ---- | --- |
| **1 — Business** | customer discussion, distribution channels, backlog, JV partners |
| **1A — Risk Factors** | concentration framed as risk ("loss of Customer A"), naming counterparties the ASC 275-10-50 note states only as a percentage |
| **7 — MD&A** | revenue drivers, named large contracts, constant-currency detail |
| **8 — the notes** | the core of it (below) |
| ~~5~~ | repurchase and dividend context. Not revenue — deliberately excluded |

Item 7 was reviewed for removal, since its segment *tables* duplicate the ASC 280 note. It stays: the
driver narrative, constant-currency splits and named large contracts appear nowhere else in the
filing, and `FilingExtractionService.Combine` exists precisely because 3M's Item 7 overview names the
segments while the section below carries their figures.

Four Items forced a budget fix. `MaxChunks` was 36 — exactly 3 × `MaxChunksPerSection` — and `Build`
checks it *after* the per-section break, so a fourth Item received one chunk and `Build` returned.
Item 8 is last in document order and is the Item carrying the figures, so it was precisely the one
being starved. `MaxChunks` is now 48 and must stay in step with the widest routing in `ItemsFor`;
`Build_GivesEveryRoutedItemItsShareOfTheChunkBudget` fails with "Item 8 was starved: 1 chunk(s)" if it
drifts.

Item 8 is where the breakdowns actually live, by accounting standard:

| Standard | What it puts in the notes |
| -------- | -------------------------- |
| ASC 280 | segment and geographic revenue |
| ASC 606 | the disaggregation table, remaining performance obligations |
| ASC 275-10-50 | customer concentration ("Customer A, 18%") |
| ASC 323 | equity-method investments — JV partners are always named |

Those four are what `Keywords(REVENUE)` in §4 selects R-files for, and what the revenue triage prompt
(`TriageSystemFor`) now names explicitly, so triage recognises a note by its own heading wording
rather than only by the word "revenue".

### Routing an Item is not enough — the worker prompt has to be able to express what it finds

Routing Items 1 and 1A surfaced a gap in `SystemFor(REVENUE)`. Every field in that prompt pointed at
a figure, and `SourceType` (`CUSTOMER, SEGMENT, REGION, PRODUCT`) has no bucket for a partner — so a
worker handed AMD's Item 1A dropped this on the floor:

> We are finalizing an investment and partnership agreement with OpenAI.

No dollar amount, no segment label, nothing the prompt licensed it to return. The prompt now states
that **a named counterparty is itself a source** even with no figure — customers, commercial partners,
JV and equity-method counterparties, distributors and resellers — recorded as `name` +
`related_company` with `classification: CUSTOMER` and null figures. Pending or unsigned relationships
are returned too, with the hedging words ("finalizing", "no assurance") quoted verbatim in `proof` so
the reviewer sees them; nothing persists until a human confirms each cell.

The counterparty is modelled by `related_company` → `RevenueSource.RelatedCompanyId` (nullable FK),
**not** by a new `SourceType` value. Adding `PARTNER` would mean a DB enum change and a migration for
something the schema already expresses.

The paired exclusion list is load-bearing in the other direction: risk factors name companies
constantly, so competitors, litigation adversaries, suppliers and pure acquisition targets are
explicitly ruled out. Without that, Item 1A returns every proper noun on the page.

Item 1 relies on **number-only** detection: `ItemTitles` deliberately has no entry for it, because
"Business" is one generic word and false-matches everywhere (§5). COST has routed Item 1 this way
since it was added, so this is the established behaviour, not a new risk. Item 1A is detected by both
number and its canonical title (`Risk Factors`), which is what §5 added for Intel.

**Known gap, not addressed here:** Item 1A is 20–40 pages of largely generic risk, and unlike Item 7
it is *not* force-included in `ScanAutoAsync` — LLM triage is the only thing filtering it, which is
why `TriageSystemFor(REVENUE)` qualifies it down to risk factors naming a specific customer, contract,
backlog or concentration. Two failure modes remain open. If a filer does not bold its sub-headings
(Intel surfaces 4 headings in a 3.3 MB 10-K, §5), Items 1 *and* 1A both fall below
`MinHeadingsPerItem` and are read sequentially at `BuildSection`'s default of 40 chunks each — up to
80 worker calls on one revenue scan. And triage judging by title alone can read a section header like
"Risks Related to Our Business" as on-topic and take the lot. Capping `BuildSection` for thin
narrative Items is the lever if this proves expensive in practice.

### 8-K

A current report is numbered on a completely different scheme, so the annual Items find nothing in
one:

| Item | Why |
| ---- | --- |
| **1.01** | entry into a material definitive agreement — customer wins |
| **2.02** | results of operations — the quarterly revenue detail |
| **8.01** | other events — where contract announcements land when they miss 1.01 |

This needed one parser change. `ItemHeadingPattern` was `Item\s+(\d+[A-Z]?)`, which reads "Item 2.02"
as **"2"** — every 8-K Item collapses onto its whole-number prefix, no requested Item matches, and the
scan returns nothing without erroring. The number group is now `(\d+(?:\.\d+)?[A-Z]?)`, which takes a
dot only when digits follow it, so `Item 7.` still yields `7` and 10-K detection is untouched.

No R-files are fetched for an 8-K: `ScanAutoAsync` asks for reports only when the routed Items contain
`"8"`, and `"8.01"` is not `"8"`. The 8-K path is document-only, and its short Items fall below
`MinHeadingsPerItem`, so they are read sequentially by `BuildSection` — which is the right treatment
for a two-page filing anyway.

**Known gap, not addressed here:** an Item 2.02 earnings 8-K usually carries the revenue table in its
`EX-99.1` press-release exhibit, not in the 8-K body. The routing above reads the body only.

COST and RISK keep their annual Items regardless of form. That is unchanged behaviour — on an 8-K they
come up empty, exactly as they did before.

## 4b. Token ceilings and the reasoning-model trap

A worker came back cut off mid-object, on the very first source:

```
{"sources":[{"proof":{"name":"sales in EMEA","value":null
```

Three things combined:

| | |
| --- | --- |
| `ChatProviders.cs` | the OpenAI **fast** tier is `gpt-5-mini` — a reasoning model |
| `Program.cs` | the OpenAI provider sends the cap as `max_completion_tokens`, which on a reasoning model covers **reasoning tokens *and* the visible reply** |
| `FilingExtractionService` | workers asked for `maxTokens: 4000` |

Reasoning consumed almost the whole budget and the reply died about fifteen tokens in. The fast tier is
not an escape from this — `ChatProviders.cs` warns that reasoners "starve the small per-call token
budget", but the *fast* model is a reasoner too.

**The ceiling is not a spend.** Unused tokens are never billed, so a generous ceiling costs nothing
while a tight one silently returns zero findings. Workers now ask for 16000 and triage for 8000.

Triage was the worse of the two. Its visible answer is a short id list, so 800 looked generous — but
it reasons over the *entire* heading list (86 titles on AMD), and on failure `TriageHeadingsAsync`
falls through to `Enumerable.Range(0, headings.Count)`, i.e. **read every heading**. A starved triage
call therefore turned triage off and scanned the whole filing, which presents as a slow expensive
scan and never as an error.

### Truncation used to be invisible

`DeepSeekResponse` does not deserialise `finish_reason`:

```csharp
public record DeepSeekResponse(List<DeepSeekChoice>? Choices);
public record DeepSeekChoice(DeepSeekMessage? Message);
```

`CompleteAsync` returns `Message.Content` and never inspects why generation stopped, so a truncated
reply is indistinguishable from a complete one. `Parse`'s `"]}"` salvage needs at least one *complete*
source object, and this cut landed inside the first `proof` — so it recovered nothing and the chunk
reported a tidy **"0 matches"**, identical to an excerpt that genuinely names no revenue.

`ScanChunkAsync` now treats a reply that does not parse as JSON at all as an **error**, not an empty
finding, and puts the raw reply in `ScanProgress.Response` where the widget's inspector shows it.
Malformed output is the only truncation signal available until `finish_reason` is plumbed through
`IChatProvider` — that is the deeper fix, and it is not done.

## 5. Validated against three filers

Run over the real filings — Apple FY2023, AMD FY2025, Intel FY2025 — because Apple alone is not a
sample. Two of the three exposed problems Apple never would have.

| Filer | Doc    | Reports | REV sel. | COST sel. | REV chunks | Tables | Rows | Headings |
| ----- | ------ | ------- | -------- | --------- | ---------- | ------ | ---- | -------- |
| Apple | 1.6 MB | 75      | 11       | 12        | 19         | 14     | 197  | 59       |
| AMD   | 2.2 MB | 96      | 11       | 14        | 24         | 8      | 212  | 86       |
| Intel | 3.3 MB | 105     | 10       | 12        | 13         | 6      | 45   | 4        |

Selection stays at 10–14 reports against indexes of 75–105 — comfortably inside the cap of 20, on
filings with very different note structures.

### Three defects this surfaced

**1. Item detection missed non-numbered filers (was: Intel produced 2 chunks from 3.3 MB).**
`SectionBody` matched only `^Item\s+N`. Intel's 10-K contains the string `Item 1A.` **exactly once**,
in the contents table; the body heads that section `Risk Factors`, and runs its Items in the order
7, 7A, 1A. So the only match was the contents line, and the section came out empty.

Fixed by also matching the canonical Reg S-K titles (`Risk Factors`,
`Management's Discussion and Analysis`, `Quantitative and Qualitative Disclosures About Market Risk`,
`Financial Statements and Supplementary Data`), which are prescribed by regulation and therefore as
canonical as the number. `Item 1` ("Business") is excluded — one generic word, too easy to
false-match. The title form must be the whole line, so the many mid-sentence references
(`see "Risk Factors" above`) are not mistaken for boundaries.

A second half to the same fix: a section now ends at the next **different** Item, not the next
heading of any kind. Filers repeat the section title as a running page header — Intel carries ~15
`Risk Factors` lines through its risk section — and the old rule cut the section at its own first
page break.

*Result: Intel 2 → 13 REVENUE chunks, 2 → 20 RISK chunks, 0 → 6 tables.*

**2. Currency symbols in their own cells (was: `<td>$</td><td>32,228</td>`).**
Intel's segment tables put `$` and the parentheses of negatives in separate cells, which reads to a
model as an extra, meaningless column. Those symbol-only cells are now folded into the figure they
decorate. Genuinely empty spacer cells are deliberately **left alone**: they appear in different
counts per row, and dropping them row-by-row is exactly what slid values under the wrong header in
the old markdown pipeline.

**3. Heading detection assumes filers bold their sub-headings (Intel: 4 headings vs AMD's 86).**
`BuildHeadings` finds a sub-heading by font-weight; Intel styles its headings by size and colour, so
almost none are detected and the heading path would hand the workers two thin chunks — silently
missing the MD&A segment tables. An Item that yields fewer than `MinHeadingsPerItem` (5) headings is
now read sequentially instead, the same treatment Item 8 already gets and for the same reason.

### What the worker actually receives

Intel's segment table, end to end — including the two-level header (`rowspan`/`colspan`) that a
markdown pipe table cannot represent at all:

```html
<table><tr><td colspan="3"></td><td colspan="3"></td><td colspan="15">Dec 27, 2025</td></tr>
<tr><td colspan="3">Year Ended ($ In Millions)</td>…<td colspan="3">CCG</td>…<td colspan="3">DCAI</td>…<td colspan="3">Total</td></tr>
<tr><td colspan="3">Revenue</td>…<td>$32,228</td>…<td>$16,919</td>…<td>$49,147</td></tr>
<tr><td colspan="3">Cost of sales and operating expenses</td>…<td colspan="2">22,911</td>…<td colspan="2">13,497</td>…<td colspan="2">36,408</td></tr>
<tr><td colspan="3">Operating income</td>…<td>$9,317</td>…<td>$3,422</td>…<td>$12,739</td></tr>
<tr><td colspan="3">Operating margin %</td>…<td colspan="3">29%</td>…<td colspan="3">20%</td>…<td colspan="3">26%</td></tr></table>
```

Intel's remaining spacer columns (`<td colspan="3"></td>`) are the one accepted inefficiency. They
are real columns in the source, present per row in varying counts, so removing them safely means
column-wise analysis rather than per-row filtering — not worth the complexity for the token saving.

## 6. Status

Solution builds. `FilingTableTests` — 33 tests, all passing — covers table rendering, R-file chrome
removal, merged-header spans, layout-table rejection, currency-cell folding, index parsing, the
selection rule (including the income-statement-only narrowing of `Statements`, across the title forms
filers use), title-based section detection under running headers, the two chunking guarantees
(a table is never split; it never loses its caption), and the §4a Item routing — the per-form revenue
Items, Item 5 staying out, COST/RISK staying on their annual Items, dotted 8-K numbers surviving
detection with their Item boundaries intact, whole-number 10-K detection unchanged by that, and every
routed Item getting an equal share of the chunk budget.

Six failures in `CounterpartyDiscoveryTests` are pre-existing on a clean `HEAD` and unrelated to this
work (Perplexity discovery, not extraction).
