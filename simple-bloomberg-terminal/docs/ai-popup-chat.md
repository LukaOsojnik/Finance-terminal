# AI Popup Chat — background scanning, chat & saving

A bottom-right notification widget that runs filing **auto-scans in the background**, surfaces the
result with an **AI chat**, and lets the user **tick-and-save** the AI-proposed objects (revenue /
cost / risk) — all without staying on, or returning to, the Extraction page.

It exists because the original Auto-Scan blocked the Extraction page for minutes and any save had
to be done one row at a time on that page. Now the scan is detached, the user can browse freely,
gets notified on completion, chats with the grounded model, edits objects, and batch-saves them
(bidirectionally) from anywhere in the app.

---

## What it does (user-facing)

1. **Start a scan** from the Extraction page's *Auto-Scan* button. It returns instantly; a window
   pops open bottom-right showing the running task (company · filing · node), a live phase label and
   an elapsed timer.
2. **Browse anywhere** while it runs — the widget lives in the shared layout, so it rebuilds itself
   on every page from the server (full page reloads wipe JS state; the server is the source of truth).
3. **Notification on completion** — the chip pulses/surfaces when the scan finishes (and again when a
   chat reply finishes), even if the user navigated away.
4. **Chat** — opening a finished job shows an auto-generated AI summary, then a chat box. Replies are
   generated **detached on the server** and **polled**, so they survive minimize *and* navigation.
   Thinking trace + "scanning…" status are shown.
5. **Tick-and-save** — every ```save``` block the model emits becomes a checkbox row. Tick any, click
   **Save selected (N)** to persist them in one call.
6. **Edit before saving** — clicking a row opens a popup with the node's fields (like the Extraction
   form) to fix name/classification/value/etc.
7. **Bidirectional save** — objects naming a counterparty resolve/create that company (FMP/Yahoo, the
   same as the discover→link pipeline) and create the reciprocal mirror row.
8. **Resizable** — drag the grip above the checklist to grow/shrink it; the height persists.

---

## Architecture at a glance

```
Extraction page  ──POST /scan-auto-async──▶  ScanJob (singleton store) ──Task.Run(own DI scope)──▶ ScanAutoAsync + auto-summary
      │                                            ▲   ▲
 window.startScanJob(jobId, meta)                  │   │
      │                                   GET /scan-jobs?ids=   (list poll, every 2.5s, every page)
      ▼                                            │   │
 bottom-right widget (site.js)  ◀───────────────────   │
      │  chat: POST /scan-jobs/{id}/reply (detached) ──┘
      │        GET  /scan-jobs/{id}/reply  (poll buffer, every 1s)
      ▼
 tick-and-save: POST /extraction/save-batch  ──▶ rows + proofs + reciprocal counterparty links
```

**Key principle:** all long-running work (scan, chat reply) runs **detached** via
`IServiceScopeFactory` (its own DI scope — the request scope and its `DbContext` are gone the moment
the action returns the job id). The browser holds only *job ids* (in `localStorage`) and a view; it
**polls** the server for everything else. This is why the feature survives navigation. There is no
SignalR/SSE — deliberately, to match the codebase's fetch-only style.

---

## Server-side pieces

### `Services/ScanJobStore.cs` (new, singleton)
- `ScanJob` — the per-scan state object (NOT an EF entity; pure in-memory):
  - identity: `Id`, `CompanyId`, `CompanyName`, `Accession`, `Doc`, `Node`, `Form`, `FilingLabel`
  - scan: `Status` (`Running`/`Done`/`Error`), `Progress` (live phase text), `Report`
    (`AutoScanResult`), `Summary` (auto AI prose), `Error`, `CreatedAt`, `CompletedAt`
  - chat reply buffer: `Replying`, `ReplyBuffer` (incremental answer), `ReplyThink` (reasoning),
    `ReplyError`
- `ScanJobStore` — `ConcurrentDictionary<string, ScanJob>` with `Add`/`Get`/`Remove`.
- Registered in `Program.cs`: `builder.Services.AddSingleton<ScanJobStore>();`

### `Controllers/ExtractionController.cs` (endpoints added)
Injects `ScanJobStore _jobs` and `IServiceScopeFactory _scopeFactory`. All routes under
`[Route("extraction")]`.

| Route | Action | Purpose |
|-------|--------|---------|
| `POST /extraction/scan-auto-async/{companyId}` | `ScanAutoAsync` | Registers a `ScanJob`, fires the scan + auto-summary on `Task.Run` (own scope), returns `{ jobId }` immediately. Sets `Progress` phases around the awaited call. |
| `GET /extraction/scan-jobs?ids=a,b,c` | `ScanJobs` | Status of the jobs the browser tracks. Returns `{ id, status, progress, replying, createdAt, companyId, companyName, accession, doc, node, form, filingLabel, found, summary, error }`. |
| `POST /extraction/scan-jobs/dismiss/{jobId}` | `DismissScanJob` | Removes a job from the store. |
| `POST /extraction/scan-jobs/{jobId}/reply` | `ScanJobReply` | Starts a **detached** chat reply (own scope) streaming into `ReplyBuffer`/`ReplyThink`. Body: `{ messages: [{role,content}] }`. 409 if already replying. |
| `GET /extraction/scan-jobs/{jobId}/reply` | `ScanJobReplyState` | Polls the in-flight reply: `{ replying, reply, think, error }`. |
| `POST /extraction/save-batch` | `SaveBatch` | Saves many ticked objects at once (see below). |

Reused (no new persistence logic):
- `IFilingExtractionService.ScanAutoAsync` — runs the parallel scan and **caches the findings digest**
  in `IMemoryCache` (key `filing-findings:{node}:{accession}:{doc}`).
- `IExtractionChatService.StreamReplyAsync` — grounds on that cached digest; used both for the
  auto-summary and for chat replies.
- `UpsertRowByNode`, `UpsertReviewByNode`, `GetOrCreateCompanyAsync`, `EnsureReciprocal`,
  `ResolveFilingId` — the existing save / link-counterparty helpers.

#### `SaveBatch` behaviour
For each item: upsert the source row + per-field proof (endpoint `"AI extraction"`, pointer
`"ai-suggested"`, filing upserted by accession). If the item names a related company and the node is
not RISK, it resolves/creates that company via `GetOrCreateCompanyAsync` (FMP/Yahoo when a ticker is
present, else a minimal stub) and creates the reciprocal mirror row via `EnsureReciprocal`
(revenue↔cost). Returns `{ saved, links }`.

### `Models/ViewModels/ExtractionViewModels.cs`
- `ScanJobReplyRequest { List<ChatMessage> Messages }`
- `SaveBatchRequest { CompanyId, Node, Accession, Form, List<SaveBatchItem> Items }`
- `SaveBatchItem { Name, Classification, Value?, Percentage?, Note?, RelatedCompany?,
  RelatedCompanyTicker?, ExtractionProof? Proof }`

### `Services/ExtractionChatService.cs`
The ```save``` block schema in `SystemFor(node)` (revenue & cost) gained an optional
`related_company_ticker` so the model can supply a counterparty's ticker, enabling the FMP/Yahoo
enrichment path in `SaveBatch`.

---

## Client-side pieces

### `Views/Shared/_Layout.cshtml`
Markup just before the scripts (renders on every page):
- `#scanNotify` — fixed bottom-right root: `#scanNotifyChip` (+ `#scanNotifyBadge`, dot) and
  `#scanNotifyPanel`.
- Panel: `#scanNotifyList` (job rows) and `#scanNotifyChat` (back button, head, `#scanNotifyLog`,
  `#scanNotifySavesGrip` drag handle, `#scanNotifySaves` checklist, compose box with
  `#scanNotifyInput`/`#scanNotifySend`, `#scanNotifyOpen`).
- `#scanEditModal` — the edit-one-object popup (`#scanEditBody` filled per node, Apply/Cancel/Close).

> These elements are **precompiled into the assembly** (no Razor runtime compilation here), so layout
> changes need a **rebuild + restart**. The JS guards against missing elements (optional chaining) so
> a static-asset refresh before the restart won't break the widget.

### `wwwroot/css/site.css`
All widget styles are at the end of the file, prefixed `scan-notify-*` / `scan-edit-*`. Reuses the
theme vars (`--accent`, `--green`, `--bg-card`, etc.). Z-index layering: footer `1030` → widget
`1031` → edit modal `1050`. Dot colour: green when idle/done, **blue + pulsing while running OR
replying** (the `has-running`/`has-replying` rules come after `has-done` so blue wins during a reply).

### `wwwroot/js/site.js`
The widget is a single IIFE near the bottom of the file (after the ticker IIFE). Core state:
- `trackedIds` — job ids this browser tracks (localStorage `bbt.scanJobs`).
- `jobs` — latest server snapshot; `openJobId` — expanded job.
- `live` — mirrored reply buffer `{ jobId, reply, think, replying, error }` (poll-driven; survives
  re-renders and navigation).
- `saveSel` (Set of save **keys** = original block name), `saveEdits` (key → field overrides from the
  edit popup), `editingKey`.
- timers: `timer` (list poll 2.5s), `elapsedTimer` (1s elapsed label), `chatTimer` (1s reply poll).

Key functions:
- `poll()` — GET `/scan-jobs`, detects `Running→Done` (`prevStatus`) and `replying true→false`
  (`prevReplying`) to **surface the panel / notify**; stops the timer when nothing is running/replying.
- `render()` / `renderList()` / `renderChat()` — pure functions of `(jobs, history, live, saveSel,
  saveEdits)`. `renderChat` rebuilds the log, then `paintStreaming()` re-attaches the live reply and
  `renderSaves()` rebuilds the checklist — so nothing visual is lost on a poll-driven re-render.
- `sendChat()` — POSTs `/scan-jobs/{id}/reply`, then `startChatPoll()`; `refreshReply()` mirrors the
  buffer into `live` and, on completion, appends the final answer to stored history once
  (guarded by "last stored turn is the user's").
- `parseSaves(id)` / `normalizeSave()` — extract ```save``` blocks from stored assistant turns, dedupe
  by name, each gets `key` = block name.
- `renderSaves()` — merges `saveEdits` overlay onto parsed items, renders checkboxes + the
  "Save selected" bar; `saveSelected()` POSTs `/extraction/save-batch`.
- `openEdit()` / `applyEdit()` — the per-node edit popup; `CLASS_OPTS` mirrors the enum dropdowns.
- drag-resize: `pointerdown` on `#scanNotifySavesGrip` adjusts `#scanNotifySaves` height (persisted in
  `bbt.scanSavesH`).
- `window.startScanJob(jobId, meta)` — exposed for the Extraction page to hand off a scan and pop the
  window open instantly with an optimistic stub row.

#### localStorage keys
| Key | Holds |
|-----|-------|
| `bbt.scanJobs` | array of tracked job ids |
| `bbt.scanChat.{id}` | per-job visible chat turns `[{role,content}]` |
| `bbt.scanSavesH` | dragged checklist height (px) |

### `Views/Extraction/Index.cshtml`
- The *Auto-Scan* handler now calls `/scan-auto-async` and `window.startScanJob(...)` (non-blocking).
- `bootstrapFromScan()` — when the page is opened from the widget's *Open in Extraction* link
  (`?companyId&accession&doc&node&jobId&form`), it reopens the filing and replays the stored chat /
  save-cards so the existing per-row save flow still works. (Company + node are restored server-side
  by `ExtractionController.Index`.)

---

## Data flow: one full cycle

1. Extraction page → `POST /scan-auto-async` → `{jobId}` → `window.startScanJob` adds id to
   `localStorage`, opens the panel, starts polling.
2. Background task runs `ScanAutoAsync` (caches digest) then one `StreamReplyAsync` turn → `Summary`;
   sets `Status=Done`.
3. List poll sees `Running→Done` → surfaces the panel (notification).
4. User opens the job → chat shows `Summary` (seeded into `bbt.scanChat.{id}`) + the ```save```
   checklist.
5. User chats → `POST /scan-jobs/{id}/reply` (detached) → `GET .../reply` poll mirrors text into
   `live`; on finish it's appended to history; reply completion surfaces the panel again.
6. User ticks objects (optionally edits them) → `POST /extraction/save-batch` → rows + proofs +
   reciprocal links. Returns `{ saved, links }`.

---

## Gotchas / future-edit notes

- **Restart required** for any change to `ExtractionController`, `ScanJobStore`, the view models, the
  chat prompts, or `_Layout`/`Index.cshtml` markup (precompiled assembly). `site.js`/`site.css` are
  static — they refresh on hard reload (cache-busted via `asp-append-version`).
- **No per-chunk scan progress** — `Progress` is coarse (set around the one awaited call); finer
  progress would need a callback threaded through `ScanAutoAsync`.
- **Reply is polled, not streamed** — text arrives in ~1s chunks (the cost of detaching generation
  from the page). Token-by-token would require holding a page-bound stream, which can't survive
  navigation.
- **`CLASS_OPTS` is duplicated** in `site.js` from the server enums (`SourceType`/`CostBase`/
  `RiskScope`). If those enums change, update the JS map too.
- **Single user assumption** — `ScanJobStore` is not partitioned per user; the browser tracks its own
  ids. Jobs live until dismissed or process restart (no TTL eviction).
- **Save identity** — `saveSel`/`saveEdits` are keyed by the original block name; renaming in the edit
  popup keeps the key so selection/overrides don't detach.

## File map
| File | Role |
|------|------|
| `Services/ScanJobStore.cs` | job state + singleton store (new) |
| `Program.cs` | registers `ScanJobStore` |
| `Controllers/ExtractionController.cs` | scan-auto-async, scan-jobs(+dismiss), reply (POST/GET), save-batch |
| `Models/ViewModels/ExtractionViewModels.cs` | `ScanJobReplyRequest`, `SaveBatchRequest`, `SaveBatchItem` |
| `Services/ExtractionChatService.cs` | `related_company_ticker` in the save-block schema |
| `Views/Shared/_Layout.cshtml` | widget + edit-modal markup |
| `wwwroot/css/site.css` | `scan-notify-*` / `scan-edit-*` styles |
| `wwwroot/js/site.js` | the widget IIFE (poll, chat, checklist, edit, drag) |
| `Views/Extraction/Index.cshtml` | hand-off (`startScanJob`) + `bootstrapFromScan` rehydration |
| `docs/sitemap.md` | route reference (kept in sync) |
