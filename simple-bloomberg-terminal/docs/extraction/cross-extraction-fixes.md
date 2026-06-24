# Cross-segment hand-off — bugs found & fixed during first run

Status: **implemented and working.** Companion to [cross-extraction.md](cross-extraction.md) (the design).
This file records the bugs the first live run surfaced, the root cause of each, and the fix — so we
don't reintroduce them.

## Feature recap (what's wired)

Source agent (e.g. RISK) emits a node-neutral ` ```handoff {node, seed}``` ` block when info belongs
to another segment. The front-end parses it (like ` ```save ` blocks), then routes the `seed` to the
target segment's agent — **reusing** a tracked job for that filing+node, or **spawning a worker-less
one**. The target agent records it as a ` ```save ` block in its own checklist; the user ticks + Save.

Key files:
- `Services/ExtractionChatService.cs` — `SystemFor(node, handoff)`, `HandoffSuffix`, `HandoffReceiverSuffix`, `StreamReplyAsync(..., bool handoff)`.
- `Controllers/ExtractionController.cs` — `POST scan-handoff/{companyId}` (worker-less spawn), `ScanJobReply` (handoff flag on the reuse path).
- `Services/CompanyProvisioningService.cs` — `GetOrCreateCounterpartyAsync` (CIK match).
- `Repositories/CompanyRepository.cs` — `MatchByCik`.
- `wwwroot/js/site.js` — `parseHandoffBlocks`, `dispatchHandoffs`, `routeHandoff`, `sendSeedToJob`, `stripSave` (hides handoff blocks too).

---

## Bug 1 — hand-off ping-pong (nothing ever saved)

**Symptom.** RISK handed a supplier dependency to COST. The COST agent decided "this is from Item 1A
Risk Factors, it's a *risk* not a dollar cost line" and emitted its OWN ` ```handoff ` block back to
RISK. The item bounced between segments forever; no ` ```save ` block was ever produced.

**Root cause.** The hand-off instruction (`HandoffSuffix`) was appended to EVERY agent's system
prompt — including the one *receiving* a hand-off. A receiver therefore treated the item as routable
and re-routed it. Source and target need *asymmetric* prompts.

**Fix.** A separate `HandoffReceiverSuffix` used when `handoff == true`: "routing is already decided —
RECORD it here, do NOT re-route, do NOT question the segment; emit a ` ```save ` block now." Applied on
BOTH delivery paths (fresh spawn via `scan-handoff`, reused job via `ScanJobReplyRequest.Handoff`).

**Guard against regression.** A hand-off turn must use the receiver prompt. If you add a new way to
deliver a hand-off, it MUST pass `handoff: true` (controller) → `StreamReplyAsync(..., handoff: true)`,
or the loop returns.

---

## Bug 2 — qualitative items refused (no dollar value → not saved)

**Symptom.** Even when the COST agent engaged, it refused to save a supplier dependency because it had
no dollar figure traceable to the tagged XBRL.

**Root cause.** The COST/REVENUE system prompts lean hard on "PREFER the audited tagged XBRL `value`".
A counterparty dependency (TSMC, Microsoft) is qualitative — there's no XBRL line for it — so the agent
concluded it wasn't a valid cost record.

**Fix.** `HandoffReceiverSuffix` explicitly licenses null-value records: "the item may be a qualitative
relationship (supplier/customer/counterparty dependency) with NO dollar figure — that is expected and
valid: set `value` and `percentage` to null, name the counterparty in `related_company`, and still
emit the save block."

---

## Bug 3 — duplicate company created (acronym vs legal name)

**Symptom.** Saving the TSMC supplier dependency created a NEW company "Taiwan Semiconductor
Manufacturing Company" instead of linking the existing "TSMC" row.

**Root cause.** `CompanyRepository.MatchByName` normalises by stripping legal suffixes
(Corp/Inc/Ltd/Company…) but cannot bridge an **acronym ↔ full legal name**: "TSMC" → `tsmc`,
"Taiwan Semiconductor Manufacturing Company" → `taiwan semiconductor manufacturing`. No token overlap,
so the existing row was never found and a twin was inserted (with full FMP enrichment).

**Fix.** The **CIK is the canonical join** — identical across the acronym, the legal name, and FMP's
own spelling. Added `ICompanyRepository.MatchByCik(cik)` (normalises both sides to 10-digit via
`Cik.Normalize`) and call it in `GetOrCreateCounterpartyAsync` AFTER the FMP profile resolves, BEFORE
the name re-check / insert. Dedup order is now:
1. `MatchByName(agent's name)`
2. fetch FMP profile (ticker → canonical name + CIK)
3. **`MatchByCik(profile.Cik)`** ← new rung that catches the acronym case
4. `MatchByName(profile.CompanyName)`
5. create

**Caveat / limitation.** The CIK rung only fires when the existing company has a CIK stored AND FMP
returns one for the ticker. Foreign filers (20-F, e.g. TSMC) do have CIKs, so this is covered. A
ticker-less / CIK-less existing row still can't be matched this way — those fall back to name matching.

---

## Architectural traps we hit (not bugs, but easy to get wrong)

- **Grounding silently re-scans.** `StreamReplyAsync` → `GroundingAsync` → `GetOrScanDigestAsync`
  **runs the worker swarm if the digest isn't cached** (`FilingExtractionService`). A hand-off must NOT
  re-discover what the source already found, so `handoff: true` sets `scanIfMissing: false` — grounding
  reads only cached findings (+ raw-text fallback + cheap XBRL), never fans out.
- **The save-parser reads stored history, not the live buffer.** For a spawned job's first reply to be
  parseable as a ` ```save `, the front-end pre-seeds `chatKey(jobId)` with the `seed` as a user turn,
  and the endpoint sets `job.Summary = reply` — so `ensureChatHistory`/`refreshReply` reconstruct the
  assistant turn into history where `parseSaves` can see it.
- **`save-batch` needs no ScanJob.** It persists from `{CompanyId, Node, Accession, Form, Items}` alone.
  The "job" is purely a client-side UI container that holds the parsed save blocks.

## Known residual risks (left intentionally — revisit if seen)

- **Re-handoff despite the receiver prompt.** If the model still emits a ` ```handoff ` while in receiver
  mode, `dispatchHandoffs` would re-route it (the same-node guard doesn't catch COST↔RISK). The prompt
  should prevent it; if a bounce is observed, mark handoff-delivered jobs and never dispatch handoffs
  originating from them.
- **Hand-off to a target job mid-scan.** The reuse path POSTs to `/scan-jobs/{id}/reply`, which 409s
  while the target is still `Running`/`Replying`. The seed is dropped (already marked dispatched).
- **Lost delivery on network failure.** `routeHandoff` marks the hand-off dispatched BEFORE the call, so
  a failed spawn/route won't retry (trade-off: no double-spawn over guaranteed delivery).
- **Save-click latency.** Persisting a cross-segment cost/revenue row with a *new* counterparty that has
  a ticker triggers FMP fetches + an industry-classification LLM inside `GetOrCreateCounterpartyAsync`
  (~2-3s). An existing counterparty links instantly. Could be deferred to a background task if it annoys.
