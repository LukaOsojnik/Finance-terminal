// ─────────────────────────────────────────────────────────────────────────────
// Evidence → filing jump.
//
// Every extracted row carries ONE verbatim quote from the filing (RevenueSource.Evidence and its
// cost/risk twins). This turns that quote into a link: click it and the filing opens in-page,
// scrolled to the exact passage with the passage highlighted.
//
// Why the filing is rendered here instead of being linked to sec.gov with a #:~:text= fragment:
// a filing sentence is scattered across dozens of <span>/<td> nodes padded with &nbsp;, and the
// browser's text-fragment matcher needs one contiguous, exactly-matching text run. It misses most
// real quotes. Rendering the document ourselves lets us normalise whitespace, bridge cell/block
// boundaries, and fall back to a shorter anchor when the model's quote is not byte-exact.
//
// Two entry points, one matching engine:
//   EvidenceViewer.open({ filingId | companyId+accession+doc, quote, label })  → modal viewer
//   EvidenceViewer.highlightIn(container, quote)                              → an already-rendered
//                                                                               filing (scan widget,
//                                                                               extraction page)
// ─────────────────────────────────────────────────────────────────────────────
(function () {
    'use strict';

    // ── Normalisation ──
    // Everything the filing and the quote are compared through. Whitespace collapses to one space
    // (SEC pads cells with runs of &nbsp;), typographic punctuation folds to ASCII (the model
    // re-types “smart” quotes and en-dashes as it copies), and case is dropped.
    const FOLD = {
        ' ': ' ', ' ': ' ', ' ': ' ', '​': '',
        '‘': "'", '’': "'", '‚': "'", '′': "'",
        '“': '"', '”': '"', '„': '"', '″': '"',
        '‐': '-', '‑': '-', '‒': '-', '–': '-', '—': '-', '−': '-',
        '…': '.'
    };
    const foldChar = c => (c in FOLD ? FOLD[c] : c.toLowerCase());
    const isSpace = c => c === ' ' || c === '\t' || c === '\n' || c === '\r' || c === '\f' || c === '\v';

    function normalize(s) {
        let out = '', space = false;
        for (const raw of String(s || '')) {
            const c = foldChar(raw);
            if (c === '') continue;
            if (isSpace(c)) { space = out.length > 0; continue; }
            if (space) { out += ' '; space = false; }
            out += c;
        }
        return out;
    }

    // Inline tags whose boundaries are NOT word boundaries — SEC wraps individual words in <span>
    // and <font>, so treating those as breaks would insert spaces mid-word. Every other element
    // (td, tr, p, div, br…) DOES break: "Products" and "$294,866" in adjacent cells must read as two
    // words, or a table-row quote — the most common shape of a revenue quote — can never match.
    const INLINE = new Set(['SPAN', 'A', 'B', 'I', 'EM', 'STRONG', 'U', 'FONT', 'SUP', 'SUB',
        'SMALL', 'LABEL', 'CODE', 'MARK', 'IX:NONNUMERIC', 'IX:NONFRACTION']);

    // Inline-XBRL metadata blocks and anything explicitly hidden: real text in the markup that the
    // reader never sees. Indexing it would let a match scroll to an invisible node.
    const HIDDEN_TAGS = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'HEAD',
        'IX:HEADER', 'IX:HIDDEN', 'IX:REFERENCES', 'IX:RESOURCES']);
    const isHidden = el =>
        HIDDEN_TAGS.has(el.tagName) || el.hidden ||
        /display\s*:\s*none/i.test(el.getAttribute('style') || '');

    // ── The flat text index ──
    // One walk over the container produces:
    //   text  — the whole document normalised into a single string, searchable with indexOf
    //   parts — [{ node, start, len, offsets }] where offsets[i] is the RAW offset inside `node` of
    //           the normalised character at global position start + i.
    // That map is the whole trick: a string search gives an index into `text`, and the parts walk it
    // back to a DOM (node, offset) pair a Range can be built from.
    function buildIndex(root) {
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, {
            acceptNode: n => {
                if (n.nodeType === Node.ELEMENT_NODE)
                    return isHidden(n) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT;
                return n.nodeValue ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
            }
        });
        const parts = [];
        let text = '', pendingSpace = false, node;

        while ((node = walker.nextNode())) {
            // Entering a non-inline element ends the previous word: the next text starts a new cell,
            // row or paragraph. Entry alone is enough — text always follows an element entry.
            if (node.nodeType === Node.ELEMENT_NODE) {
                if (!INLINE.has(node.tagName) && text.length) pendingSpace = true;
                continue;
            }

            const raw = node.nodeValue;
            const offsets = new Int32Array(raw.length + 1);   // +1: a leading separator space can be
            const start = text.length;                        // emitted on top of every raw character
            let local = 0, chunk = '';

            for (let i = 0; i < raw.length; i++) {
                const c = foldChar(raw[i]);
                if (c === '') continue;
                if (isSpace(c)) { pendingSpace = text.length + chunk.length > 0; continue; }
                if (pendingSpace) {
                    // The separator belongs to whichever node emits it; map it to this node's current
                    // raw offset so a match is allowed to start on it.
                    offsets[local++] = i;
                    chunk += ' ';
                    pendingSpace = false;
                }
                offsets[local++] = i;
                chunk += c;
            }
            if (!chunk) continue;
            text += chunk;
            parts.push({ node, start, len: chunk.length, offsets: offsets.subarray(0, local) });
        }
        return { text, parts };
    }

    // ── Fuzzy anchor ──
    // The model's quote is verbatim "in principle". In practice it drops a footnote marker, retypes a
    // dash, or runs a table row together in reading order. So the search degrades in stages:
    //   1. the whole quote                 — the normal case, and the only one reported `exact`
    //   2. either side of an elision ("… x …")
    //   3. the LONGEST leading run of words that is in the document
    //   4. its longest sentence            — for a quote that diverges at the START (a reformatted
    //                                        heading glued onto a real paragraph)
    // Anything under MIN_ANCHOR words is too generic to prove anything, so a match that short is
    // reported as a miss rather than scrolling the user somewhere arbitrary.
    const MIN_ANCHOR = 5;

    const clean = q => normalize(q).replace(/^["'.\s]+|["'.\s]+$/g, '');

    // Longest matching prefix, by binary search on word count: "the first n words are present" is
    // monotone — a longer prefix matching implies every shorter one does — so this costs ~log2(words)
    // scans of the document instead of one per word.
    function longestPrefix(text, words) {
        let lo = MIN_ANCHOR, hi = words.length - 1, best = -1;
        while (lo <= hi) {
            const mid = (lo + hi) >> 1;
            if (text.indexOf(words.slice(0, mid).join(' ')) >= 0) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best;
    }

    // Where `quote` sits in the document, as [start, end) in normalised space, or null.
    function findRange(index, quote) {
        const text = index.text;
        const full = clean(quote);
        if (!full) return null;

        const at = text.indexOf(full);
        if (at >= 0) return { start: at, end: at + full.length, exact: true };

        const tries = [];
        // Elisions: "… we recognised revenue …" — each side is its own contiguous run.
        for (const frag of full.split(/\s*\.{3,}\s*/).map(f => f.trim()).filter(Boolean))
            if (frag !== full && frag.split(' ').length >= MIN_ANCHOR) tries.push(frag);

        const words = full.split(' ');
        const n = longestPrefix(text, words);
        if (n > 0) tries.push(words.slice(0, n).join(' '));

        tries.push(...full.split(/(?<=[.;:])\s+/)
            .map(x => x.trim())
            .filter(x => x.split(' ').length >= MIN_ANCHOR)
            .sort((x, y) => y.length - x.length));

        for (const cand of tries) {
            const i = text.indexOf(cand);
            if (i >= 0) return { start: i, end: i + cand.length, exact: false };
        }
        return null;
    }

    // ── Painting the hit ──
    const HIT = 'evidence-hit';
    function clearHits(root) {
        root.querySelectorAll('mark.' + HIT).forEach(m => {
            const parent = m.parentNode;
            while (m.firstChild) parent.insertBefore(m.firstChild, m);
            parent.removeChild(m);
            parent.normalize();
        });
    }

    // Wrap every text node the match touches. Done per-node (rather than Range.surroundContents)
    // because a filing quote routinely spans table cells, and surroundContents throws the moment a
    // range crosses an element boundary.
    function paint(index, hit) {
        const marks = [];
        for (const p of index.parts) {
            if (p.start + p.len <= hit.start || p.start >= hit.end) continue;
            const from = p.offsets[Math.max(hit.start, p.start) - p.start];
            const toIdx = Math.min(hit.end, p.start + p.len) - 1 - p.start;
            const to = p.offsets[toIdx] + 1;
            let node = p.node;
            if (from > 0) node = node.splitText(from);
            if (to - from < node.nodeValue.length) node.splitText(to - from);
            const mark = document.createElement('mark');
            mark.className = HIT;
            node.parentNode.insertBefore(mark, node);
            mark.appendChild(node);
            marks.push(mark);
        }
        return marks;
    }

    /**
     * Highlight `quote` inside an already-rendered filing and scroll it into view.
     * Returns { found, exact } — `exact:false` means only a shortened anchor matched, so the caller
     * can tell the user the quote is approximate rather than pretending it nailed it.
     */
    function highlightIn(container, quote) {
        if (!container || !quote) return { found: false, exact: false };
        clearHits(container);
        const index = buildIndex(container);
        const hit = findRange(index, quote);
        if (!hit) return { found: false, exact: false };
        const marks = paint(index, hit);
        if (!marks.length) return { found: false, exact: false };
        marks[0].scrollIntoView({ block: 'center', inline: 'nearest' });
        marks[0].classList.add('is-flash');
        setTimeout(() => marks.forEach(m => m.classList.remove('is-flash')), 1600);
        return { found: true, exact: hit.exact };
    }

    // ── Modal viewer ──
    let box, docPane, statusEl, titleEl, openBtn, current = null;

    // Fetched filings, newest last. A 10-K runs to several MB of markup, so this keeps only the few a
    // user actually flips between while checking a company's rows.
    const docCache = new Map();
    const DOC_CACHE_MAX = 3;
    function cacheDoc(key, raw) {
        docCache.set(key, raw);
        while (docCache.size > DOC_CACHE_MAX) docCache.delete(docCache.keys().next().value);
    }

    function ensureShell() {
        if (box) return box;
        box = document.getElementById('evidenceModal');
        if (!box) return null;
        docPane = box.querySelector('#evidenceDoc');
        statusEl = box.querySelector('#evidenceStatus');
        titleEl = box.querySelector('#evidenceTitle');
        openBtn = box.querySelector('#evidenceOpenSec');
        box.querySelector('#evidenceClose').addEventListener('click', close);
        box.querySelector('.evidence-backdrop').addEventListener('click', close);
        box.querySelector('#evidenceAgain').addEventListener('click', () => {
            if (current) report(highlightIn(docPane, current.quote));
        });
        document.addEventListener('keydown', e => { if (e.key === 'Escape' && !box.hidden) close(); });
        return box;
    }

    function close() { if (box) { box.hidden = true; docPane.innerHTML = ''; current = null; } }

    function report(res) {
        if (res.found && res.exact) statusEl.textContent = 'Quote located in the filing.';
        else if (res.found) statusEl.textContent = 'Closest match — the stored quote is not byte-exact in this document.';
        else statusEl.textContent = 'Quote not found in this document (the row may cite a different exhibit).';
        statusEl.classList.toggle('is-miss', !res.found);
    }

    // Sanitise the SEC markup and make its links safe/absolute — same treatment the scan widget's
    // doc pane gives, kept here so the viewer works on pages that never load the widget.
    function renderDoc(raw, isHtml) {
        if (!isHtml) { docPane.textContent = raw; return; }
        const tmp = document.createElement('div');
        tmp.innerHTML = raw;
        tmp.querySelectorAll('script,style,link,meta,noscript,base,title').forEach(e => e.remove());
        tmp.querySelectorAll('a[href]').forEach(el => {
            const href = el.getAttribute('href') || '';
            if (href.startsWith('#')) return;
            try { el.href = new URL(href, 'https://www.sec.gov').href; } catch { }
            el.target = '_blank'; el.rel = 'noopener';
        });
        docPane.innerHTML = tmp.innerHTML;
        docPane.scrollTop = 0;
    }

    /**
     * Open the filing behind a saved row and jump to its evidence.
     * Address it either by `filingId` (saved rows) or by companyId+accession+doc (live scan rows).
     */
    async function open(opts) {
        if (!ensureShell()) return;
        const quote = opts.quote || '';
        const url = opts.filingId
            ? `/api/filings/${opts.filingId}/document`
            : `/api/stock/filing/${opts.companyId}?accession=${encodeURIComponent(opts.accession)}&doc=${encodeURIComponent(opts.doc)}`;
        const key = url;

        current = { quote, url };
        box.hidden = false;
        titleEl.textContent = opts.label || 'Filing evidence';
        statusEl.textContent = 'Loading filing…';
        statusEl.classList.remove('is-miss');
        docPane.textContent = '';
        openBtn.hidden = !opts.secUrl;
        if (opts.secUrl) openBtn.href = opts.secUrl;

        let raw = docCache.get(key);
        try {
            if (raw === undefined) {
                const res = await fetch(url);
                if (!res.ok) {
                    statusEl.textContent = `Could not load the filing (${res.status} ${await res.text()}).`;
                    statusEl.classList.add('is-miss');
                    return;
                }
                raw = await res.text();
                cacheDoc(key, raw);
            }
            if (!current || current.url !== url) return;      // user closed / opened another meanwhile
            const isHtml = /<html|<body|<div|<table/i.test(raw.slice(0, 4000));
            renderDoc(raw, isHtml);
            report(highlightIn(docPane, quote));
        } catch {
            statusEl.textContent = 'Network error loading the filing.';
            statusEl.classList.add('is-miss');
        }
    }

    // ── Declarative wiring ──
    // Any element with data-evidence (+ data-filing-id, or data-company-id/accession/doc) becomes a
    // jump link. Views only add attributes; no per-page script.
    function openFrom(el) {
        const quote = el.getAttribute('data-evidence');
        if (!quote) return;
        open({
            quote,
            filingId: el.getAttribute('data-filing-id') || null,
            companyId: el.getAttribute('data-company-id') || null,
            accession: el.getAttribute('data-accession') || null,
            doc: el.getAttribute('data-doc') || null,
            secUrl: el.getAttribute('data-sec-url') || null,
            label: el.getAttribute('data-evidence-label') || null
        });
    }

    document.addEventListener('click', e => {
        const el = e.target.closest('[data-evidence]');
        if (!el) return;
        e.preventDefault();
        openFrom(el);
    });
    // The quote is often a <span>/<div> rather than a button (it lives inside table cells), so the
    // keyboard activation role="button" promises has to be wired by hand.
    document.addEventListener('keydown', e => {
        if (e.key !== 'Enter' && e.key !== ' ') return;
        const el = e.target.closest ? e.target.closest('[data-evidence]') : null;
        if (!el) return;
        e.preventDefault();
        openFrom(el);
    });

    window.EvidenceViewer = { open, close, highlightIn, clearHits, normalize };
})();
