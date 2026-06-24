# Cross-segment hand-off (agent → agent, worker-less spawn)

Status: **design locked, not yet implemented.** This doc is the source of truth — the
conversation that produced it will be deleted. Implement from here.

## Goal

While chatting with the extraction agent for one segment (e.g. RISK), the user often surfaces
information that belongs to **another** segment (e.g. a risk factor describing a key supplier =
COST, or a customer concentration = REVENUE). The user wants to *tell the agent in chat* what they
want done with it, and have it routed to the right segment's agent — which then proposes the save
in its own context.

## The key realisation

The source agent (RISK) **already found the fact** — its parallel workers read Item 1A and the
text is in its context. So the hand-off must NOT re-run the target segment's worker scan: that
would re-discover something we already hold. The target agent is *handed the finding* in place of
a worker summary, exactly the way a normal scan hands the worker digest to the lead analyst
(`ExtractionController.cs` ScanAutoAsync seed turn).

Two distinct kinds of knowledge, deliberately kept in separate agents so neither is polluted:

- **What was found** — RISK has this. It emits it in plain, node-neutral terms.
- **How the target records it** (the COST/REVENUE save schema) — lives ONLY in the target
  agent's system prompt. RISK never learns it.

Spawning is therefore for **schema isolation**, not re-discovery. The spawned agent's only job is
to dress an already-found fact in the target segment's schema. That's a formatting turn, not a scan.

## Locked decisions

1. **Trigger is conversational, not a button.** The user types their request in the current chat
   (e.g. "record the TSMC supplier dependency under cost"). The source agent, judging it belongs
   to another node, emits a hand-off block. There is **no button** to click.
2. **The source agent's hand-off block is node-neutral.** It carries only `{node, seed}` — the
   target node name and a `seed` prompt (the request + the verbatim source passage). The source
   prompt learns ONLY this tiny block (~15 tokens). It never carries another segment's save schema,
   so it is never poisoned by cross-segment concerns.
3. **A hand-off spawns a second agent for the target segment — with its workers OFF.** No re-scan.
   The spawned agent is grounded on the `seed` text the source handed it (plus the *cached* target
   XBRL view if one exists — `GetXbrlViewAsync`, no worker swarm), and runs one turn whose first
   user message IS the `seed`. The target agent's system prompt (`SystemFor(node)`) supplies the
   save schema.
4. **Propose-and-confirm is preserved.** The spawned agent proposes a ` ```save ` block in the
   target node's saves checklist; the user still ticks + Save. Nothing auto-writes to the DB from
   chat (`save-batch` is the only writer, and it is node-agnostic — needs only
   `{CompanyId, Node, Accession, Form, Items}`, no ScanJob lookup).

## The hand-off block

The source agent emits, alongside its normal reply, a fenced block — mirroring the existing
` ```save ` mechanism (parsed client-side, hidden from the bubble):

```handoff
{"node":"COST","seed":"User wants the TSMC fabrication dependency recorded as a cost. Source (10-K Item 1A): \"…verbatim passage the workers found…\""}
```

- `node` — target segment: `COST` | `REVENUE` | `RISK`.
- `seed` — the request plus the **verbatim source passage**. This passage must ride along because
  it lives in the *source* segment's grounding (Item 1A), which the target agent never reads. The
  source agent quotes it from its own findings digest.

Filing identifiers (companyId, accession, doc, form) are NOT in the block — the front-end already
holds them for the source widget and reuses them on spawn. Only segment-local knowledge travels.

### Source-prompt addition (per segment, in `ExtractionChatService.SystemFor`)

Add to each node's system prompt, after the `save`-block instructions:

> When the user surfaces information that belongs to a DIFFERENT segment (a supplier/cost detail
> while in REVENUE/RISK, a customer/revenue detail while in COST/RISK, a risk while in
> COST/REVENUE), do NOT try to save it yourself. Emit a fenced block exactly like:
> ` ```handoff ` `{"node":"COST","seed":"<the user's request + the verbatim passage you found>"}` ` ``` `
> `node` is one of COST, REVENUE, RISK. Put everything the other segment needs into `seed`: what
> the user wants done, and the verbatim source text backing it. Emit one block per hand-off,
> alongside your normal reply.

## Front-end (site.js, scan-notify widget)

Mirror the existing save-block plumbing:

1. **Parse** — alongside `parseSaves` (`re = /```save\s*([\s\S]*?)```/g`), add a `parseHandoffs`
   over `/```handoff\s*([\s\S]*?)```/g`, `JSON.parse` each, keep `{node, seed}`.
2. **Strip** — extend `stripSave` so ` ```handoff ` blocks are hidden from the chat bubble too
   (kept verbatim in stored history).
3. **Spawn / route** — for each fresh hand-off in the latest assistant turn:
   - **Target job exists** (a tracked job with same `companyId` + `accession` + `node`): POST the
     `seed` as a follow-up user turn to `/extraction/scan-jobs/{id}/reply`. No spawn, no scan.
   - **No target job**: spawn a **worker-less** target job (see endpoint below), seeded with `seed`.
   - Surface the target widget so the user sees the proposal appear.
   - De-dupe: track which hand-off blocks have already been dispatched (by content hash) so polls
     /re-renders don't fire the same hand-off twice.

## Back-end — worker-less spawn endpoint

`scan-auto-async` always runs `ScanAutoAsync` (the ~36 worker LLM calls). For a hand-off we want a
job container + one grounded formatting turn, no workers. Add:

`POST /extraction/scan-handoff/{companyId:long}?accession&doc&node&form&companyName&filingLabel`
with body `{ "seed": "<the handoff seed>" }`.

It mirrors `scan-auto-async` EXCEPT:

- It does **not** call `extractor.ScanAutoAsync` (no worker swarm, no triage).
- It creates the `ScanJob` shell (CompanyId, Accession, Doc, Node, Form, FilingLabel) and marks the
  section tree as not-applicable (or omits it) — there is no scan to display.
- `job.Xbrl = await chat.GetXbrlViewAsync(companyId, accession, parsedNode)` — cheap, cached, gives
  the formatting turn the audited figures if any are tagged. Optional but free.
- The first (and only auto) turn's seed is the handoff `seed`, not "Summarize the candidates":
  ```csharp
  var seed = new List<ChatMessage> { new("user", req.Seed) };
  await foreach (var d in chat.StreamReplyAsync(companyId, accession, doc, parsedNode, seed, form)) { … }
  ```
- `job.Status = Done` once the formatting turn streams in; the widget then shows the proposed
  ` ```save ` block in the target node's checklist, ready to tick + Save.

`StreamReplyAsync`'s grounding (`GroundingAsync`) will try `GetOrScanDigestAsync`, which is a
**cache read** — if the target was scanned earlier it grounds on that for free; if not, the agent
grounds on the `seed` + XBRL alone, which is exactly enough to format a fact the source already
found. No worker scan is triggered by this path.

## Why not the alternatives

- **Source agent fills the target save block itself** — would inject the target's save schema into
  the source prompt (context poisoning) and have the source fill a `value` without the target's
  XBRL grounding. Rejected (decision #2 keeps the source pure).
- **Native LLM tool-calling** — the stream layer is pure text SSE across DeepSeek / Anthropic /
  OpenAI-compat on BYO keys; tool-calling means per-provider `tool_calls` buffering + execute/resume
  loops for zero benefit over a fenced block the parser already understands.
- **Spawn WITH a worker scan** — re-discovers a fact the source already found; wasted worker LLM
  calls. Rejected (the key realisation above).

## Flow summary

```
RISK agent emits ```handoff {node:"COST", seed:"<request + verbatim Item 1A passage>"}```
        ↓ front-end parses block (like ```save), hides it from the bubble
target COST job?
   exists → POST /scan-jobs/{id}/reply  (seed as a follow-up turn)
   none   → POST /scan-handoff/{companyId}?node=COST  (worker-less job shell, seed = first turn)
        ↓ COST agent: SystemFor(COST) schema + grounding(seed [+ cached XBRL]) — NO workers
COST agent emits ```save {COST schema}``` → COST checklist → user ticks + Save → save-batch(node=COST)
```
