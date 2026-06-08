// AJAX search wiring for every list page.
// Convention:
//   <input class="search-input" data-search-url="/entity/search" />
//   <tbody id="table-body">...</tbody>
// Server returns _TableBody partial HTML; JS swaps it into #table-body.

document.querySelectorAll('.search-input').forEach(input => {
    let timer = null;

    input.addEventListener('input', function () {
        const value = this.value;
        const url = this.dataset.searchUrl;
        const tbody = document.getElementById('table-body');
        if (!url || !tbody) return;

        clearTimeout(timer);
        timer = setTimeout(() => {
            fetch(url + '?term=' + encodeURIComponent(value))
                .then(r => r.ok ? r.text() : Promise.reject(r.status))
                .then(html => { tbody.innerHTML = html; })
                .catch(() => { /* keep prior rows on network/server error */ });
        }, 300);
    });
});

// Autocomplete picker wiring.
// Convention (see Views/Shared/_AutocompletePicker.cshtml):
//   <div class="autocomplete" data-lookup-url="/entity/lookup">
//     <input class="autocomplete-input">
//     <input type="hidden" name="FieldName" data-val-required="...">
//     <ul class="autocomplete-results" hidden></ul>
//   </div>
// Server returns JSON array of {id, label}.

document.querySelectorAll('.autocomplete').forEach(box => {
    const input  = box.querySelector('.autocomplete-input');
    const hidden = box.querySelector('input[type=hidden]');
    const list   = box.querySelector('.autocomplete-results');
    const url    = box.dataset.lookupUrl;
    if (!input || !hidden || !list || !url) return;

    let timer = null;
    let activeIdx = -1;

    const render = items => {
        list.innerHTML = items.map((r, i) =>
            `<li data-id="${r.id}" data-idx="${i}">${r.label}</li>`).join('');
        list.hidden = items.length === 0;
        activeIdx = -1;
    };
    const highlight = i => {
        [...list.children].forEach(li => li.classList.remove('active'));
        if (i >= 0 && list.children[i]) list.children[i].classList.add('active');
        activeIdx = i;
    };
    const pick = li => {
        hidden.value = li.dataset.id;
        input.value  = li.textContent;
        list.hidden  = true;
        // re-trigger unobtrusive on the hidden field (which carries data-val-*)
        hidden.dispatchEvent(new Event('blur'));
        if (window.jQuery) { jQuery(hidden).valid && jQuery(hidden).valid(); }
    };

    input.addEventListener('input', function () {
        hidden.value = ''; // any edit invalidates the prior selection
        clearTimeout(timer);
        const term = this.value.trim();
        if (term.length < 1) { render([]); return; }
        timer = setTimeout(() => {
            fetch(`${url}?term=${encodeURIComponent(term)}`)
                .then(r => r.ok ? r.json() : Promise.reject(r.status))
                .then(render)
                .catch(() => render([]));
        }, 250);
    });
    input.addEventListener('keydown', e => {
        if (list.hidden) return;
        const n = list.children.length;
        if (n === 0) return;
        if (e.key === 'ArrowDown') { highlight((activeIdx + 1) % n); e.preventDefault(); }
        else if (e.key === 'ArrowUp') { highlight((activeIdx - 1 + n) % n); e.preventDefault(); }
        else if (e.key === 'Enter' && activeIdx >= 0) { pick(list.children[activeIdx]); e.preventDefault(); }
        else if (e.key === 'Escape') { list.hidden = true; }
    });
    list.addEventListener('mousedown', e => {
        const li = e.target.closest('li');
        if (li) pick(li);
    });
    input.addEventListener('blur', () => setTimeout(() => { list.hidden = true; }, 150));
});

// Multi-select picker wiring (events: link countries / companies / trade blocs).
// Convention (see Views/Shared/_MultiSelectPicker.cshtml):
//   <fieldset class="multiselect" data-field="SelectedCompanyIds"
//             data-parent-field="SelectedCountryIds" data-empty-placeholder="...">
//     <div class="multiselect-control">
//       <div class="multiselect-chips"></div>
//       <input class="multiselect-input">
//     </div>
//     <ul class="multiselect-options"><li data-id data-label data-parent class="is-selected?">...</li></ul>
//   </fieldset>
// Selected state lives on the <li> (.is-selected); JS derives chips + hidden inputs from it.
// When data-parent-field is set, options are gated by the selection in that parent picker.
const msRegistry = {};

document.querySelectorAll('.multiselect').forEach(box => {
    const field    = box.dataset.field;
    const control  = box.querySelector('.multiselect-control');
    const chips    = box.querySelector('.multiselect-chips');
    const input    = box.querySelector('.multiselect-input');
    const list     = box.querySelector('.multiselect-options');
    if (!field || !control || !chips || !input || !list) return;

    const parentField   = box.dataset.parentField || null;
    const basePlaceholder  = input.placeholder;
    const emptyPlaceholder = box.dataset.emptyPlaceholder || basePlaceholder;

    const options = [...list.children];
    let activeIdx = -1;
    let allowedParents = parentField ? new Set() : null; // null = no gating
    const changeListeners = [];

    const selectedLis = () => options.filter(li => li.classList.contains('is-selected'));
    const visible     = () => options.filter(li => !li.hidden);
    const notify      = () => changeListeners.forEach(cb => cb());

    const isAllowed = li => !parentField || allowedParents.has(li.dataset.parent);

    const renderChips = () => {
        chips.innerHTML = '';
        selectedLis().forEach(li => {
            const chip = document.createElement('span');
            chip.className = 'ms-chip';
            chip.innerHTML =
                `${li.dataset.label}<button type="button" class="ms-chip-x" aria-label="Remove">×</button>` +
                `<input type="hidden" name="${field}" value="${li.dataset.id}"/>`;
            chip.querySelector('.ms-chip-x').addEventListener('click', () => {
                li.classList.remove('is-selected');
                renderChips();
                filter();
                notify();
            });
            chips.appendChild(chip);
        });
    };

    const highlight = i => {
        const vis = visible();
        vis.forEach(li => li.classList.remove('active'));
        if (i >= 0 && vis[i]) vis[i].classList.add('active');
        activeIdx = i;
    };

    const filter = () => {
        const term = input.value.trim().toLowerCase();
        options.forEach(li => {
            const match = !li.classList.contains('is-selected') &&
                          isAllowed(li) &&
                          li.dataset.label.toLowerCase().includes(term);
            li.hidden = !match;
        });
        list.hidden = visible().length === 0;
        if (parentField) {
            const locked = allowedParents.size === 0;
            input.disabled = locked;
            input.placeholder = locked ? emptyPlaceholder : basePlaceholder;
        }
        highlight(-1);
    };

    const pick = li => {
        li.classList.add('is-selected');
        input.value = '';
        renderChips();
        filter();
        notify();
        input.focus();
    };

    // Called by a parent picker when its selection changes.
    // prune=false on the initial load so saved selections (Edit) survive even if
    // their parent isn't linked; user-driven changes prune normally.
    const setAllowedParents = (ids, prune = true) => {
        allowedParents = new Set(ids);
        if (prune) {
            let pruned = false;
            selectedLis().forEach(li => { if (!isAllowed(li)) { li.classList.remove('is-selected'); pruned = true; } });
            if (pruned) { renderChips(); notify(); }
        }
        filter();
    };

    control.addEventListener('mousedown', e => {
        if (!input.disabled && (e.target === control || e.target === chips)) input.focus();
    });
    input.addEventListener('focus', filter);
    input.addEventListener('input', filter);
    input.addEventListener('keydown', e => {
        const vis = visible();
        if (e.key === 'ArrowDown') { highlight(Math.min(activeIdx + 1, vis.length - 1)); e.preventDefault(); }
        else if (e.key === 'ArrowUp') { highlight(Math.max(activeIdx - 1, 0)); e.preventDefault(); }
        else if (e.key === 'Enter') {
            if (vis[activeIdx]) pick(vis[activeIdx]);
            else if (vis.length === 1) pick(vis[0]);
            e.preventDefault();
        }
        else if (e.key === 'Backspace' && input.value === '') {
            const sel = selectedLis();
            if (sel.length) { sel[sel.length - 1].classList.remove('is-selected'); renderChips(); filter(); notify(); }
        }
        else if (e.key === 'Escape') { list.hidden = true; }
    });
    list.addEventListener('mousedown', e => {
        const li = e.target.closest('li');
        if (li) { e.preventDefault(); pick(li); }
    });
    input.addEventListener('blur', () => setTimeout(() => { list.hidden = true; }, 150));

    msRegistry[field] = {
        parentField,
        onChange: cb => changeListeners.push(cb),
        selectedIds: () => selectedLis().map(li => li.dataset.id),
        setAllowedParents,
    };
    renderChips();
});

// Wire each child picker to its parent: sync on parent change + once on load.
Object.values(msRegistry).forEach(child => {
    if (!child.parentField) return;
    const parent = msRegistry[child.parentField];
    if (!parent) return;
    parent.onChange(() => child.setAllowedParents(parent.selectedIds()));
    child.setAllowedParents(parent.selectedIds(), false); // initial: gate dropdown, keep saved chips
});

// Event link panel: countries column + company flyout (see _EventLinkPanel.cshtml).
// Checkboxes carry the form names and post on their own; JS handles flyout + chips.
document.querySelectorAll('[data-link-panel]').forEach(panel => {
    const countryRows  = [...panel.querySelectorAll('.link-country')];
    const companyLists  = [...panel.querySelectorAll('.link-company-list')];
    const head         = panel.querySelector('[data-company-head]');
    const placeholder  = panel.querySelector('[data-company-placeholder]');
    const chipsBox     = panel.querySelector('.link-chips');

    const openCountry = id => {
        countryRows.forEach(r => r.classList.toggle('active', r.dataset.countryId === id));
        companyLists.forEach(l => l.hidden = l.dataset.forCountry !== id);
        if (placeholder) placeholder.hidden = true;
        const row = countryRows.find(r => r.dataset.countryId === id);
        if (head) head.textContent = row
            ? 'Companies · ' + row.querySelector('.link-country-name').textContent
            : 'Companies';
    };

    panel.querySelectorAll('.link-country-open').forEach(btn => {
        btn.addEventListener('click', () => openCountry(btn.closest('.link-country').dataset.countryId));
    });

    const renderChips = () => {
        const checks = [...panel.querySelectorAll('.link-country-cb:checked, .link-company-cb:checked')];
        chipsBox.innerHTML = '';
        if (!checks.length) {
            chipsBox.innerHTML = '<span class="link-chips-empty">Nothing linked yet</span>';
            return;
        }
        checks.forEach(cb => {
            const chip = document.createElement('span');
            chip.className = 'ms-chip' + (cb.classList.contains('link-country-cb') ? ' ms-chip-country' : '');
            chip.innerHTML = `${cb.dataset.label}<button type="button" class="ms-chip-x" aria-label="Remove">×</button>`;
            chip.querySelector('.ms-chip-x').addEventListener('click', () => {
                cb.checked = false;
                cb.dispatchEvent(new Event('change', { bubbles: true }));
            });
            chipsBox.appendChild(chip);
        });
    };

    panel.addEventListener('change', e => {
        const cb = e.target;
        if (!cb.matches('.link-country-cb, .link-company-cb')) return;
        // keep the row highlight in sync with the checkbox
        cb.closest('.link-country, .link-company')?.classList.toggle('checked', cb.checked);
        // convenience: checking a country opens its companies
        if (cb.matches('.link-country-cb') && cb.checked) openCountry(cb.value);
        renderChips();
    });

    renderChips();
});

// Inline row delete: POST the form via fetch and drop its <tr> instead of reloading the page.
// Convention: <form class="js-row-delete" data-confirm="...">…</form> inside a table row.
// Delegated on document so it survives the search AJAX swapping out #table-body.
document.addEventListener('submit', async e => {
    const form = e.target.closest('.js-row-delete');
    if (!form) return;
    e.preventDefault();

    const msg = form.dataset.confirm;
    if (msg && !(await uiConfirm(msg, { danger: true }))) return;

    const row = form.closest('tr');
    const btn = form.querySelector('button');
    if (btn) btn.disabled = true;

    fetch(form.action, { method: 'POST', body: new FormData(form) })
        .then(async r => {
            // A blocked company delete (409) with linked sources opens the modal so the user can
            // clear those sources from here; the company then deletes automatically.
            if (r.status === 409 && form.dataset.companyId) {
                openLinkedModal(form, row);
                return;
            }
            // Surface the server's message instead of a generic error so the user knows why.
            if (!r.ok) throw (await r.text()) || 'Delete failed — refresh and try again.';
            if (row) row.remove();
        })
        .catch(err => {
            if (btn) btn.disabled = false;
            uiAlert(typeof err === 'string' ? err : 'Delete failed — refresh and try again.');
        });
});

// Generic confirm dialog for full-page POST delete forms (non-AJAX).
// Convention: <form class="js-confirm" data-confirm="Delete X?">…</form>.
// On confirm we call form.submit() (native submit bypasses this listener — no recursion).
document.addEventListener('submit', async e => {
    const form = e.target.closest('.js-confirm');
    if (!form) return;
    e.preventDefault();
    if (await uiConfirm(form.dataset.confirm, { danger: true })) form.submit();
});

// ── Generic confirm / alert dialogs (window.uiConfirm / window.uiAlert) ──
// Reuses the linked-sources modal styling. Builds one reusable overlay lazily and
// appends it to <body>, so it works on every page without per-view markup.
(function () {
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    let overlay, titleEl, msgEl, footerEl, current = null;

    function build() {
        overlay = document.createElement('div');
        overlay.className = 'modal-overlay';
        overlay.hidden = true;
        overlay.innerHTML = `
            <div class="modal-box" role="dialog" aria-modal="true" style="max-width:440px;">
                <div class="modal-header">
                    <h3 data-ui-title></h3>
                    <button type="button" class="modal-close" data-modal-close aria-label="Close">&times;</button>
                </div>
                <p class="modal-intro" data-ui-message></p>
                <div class="modal-footer" data-ui-footer></div>
            </div>`;
        document.body.appendChild(overlay);
        titleEl = overlay.querySelector('[data-ui-title]');
        msgEl = overlay.querySelector('[data-ui-message]');
        footerEl = overlay.querySelector('[data-ui-footer]');

        overlay.addEventListener('click', e => {
            if (e.target === overlay || e.target.closest('[data-modal-close]')) resolve(false);
        });
        document.addEventListener('keydown', e => {
            if (overlay.hidden || !current) return;
            if (e.key === 'Escape') resolve(false);
            else if (e.key === 'Enter') resolve(true);
        });
    }

    function resolve(ok) {
        if (!current) return;
        overlay.hidden = true;
        const c = current;
        current = null;
        c(ok);
    }

    function openDialog(message, opts, kind) {
        if (!overlay) build();
        return new Promise(done => {
            // Resolve any dialog already open before showing this one.
            if (current) resolve(false);
            current = ok => done(kind === 'confirm' ? !!ok : undefined);

            titleEl.textContent = opts.title || (kind === 'confirm' ? 'Confirm' : 'Notice');
            msgEl.innerHTML = esc(message);

            if (kind === 'confirm') {
                const cancel = opts.cancelLabel || 'Cancel';
                const confirm = opts.confirmLabel || 'Confirm';
                footerEl.innerHTML = `
                    <button type="button" class="btn btn-secondary" data-modal-close>${esc(cancel)}</button>
                    <button type="button" class="btn btn-primary" data-ui-ok ${opts.danger ? 'style="color:var(--red);"' : ''}>${esc(confirm)}</button>`;
            } else {
                footerEl.innerHTML = `<button type="button" class="btn btn-primary" data-ui-ok>${esc(opts.okLabel || 'OK')}</button>`;
            }
            footerEl.querySelector('[data-ui-ok]').onclick = () => resolve(true);

            overlay.hidden = false;
            footerEl.querySelector('[data-ui-ok]').focus();
        });
    }

    window.uiConfirm = (message, opts = {}) => openDialog(message, opts, 'confirm');
    window.uiAlert = (message, opts = {}) => openDialog(message, opts, 'alert');
})();

// ── Linked-sources modal (clears revenue/cost sources blocking a company delete) ──
(function () {
    const overlay = document.getElementById('linked-modal');
    if (!overlay) return;
    const body = document.getElementById('linked-modal-body');
    const title = document.getElementById('linked-modal-title');

    // Context for the company whose delete is blocked: its delete form + table row + button.
    let ctx = null;

    const fmtValue = v => v == null ? '' :
        v >= 1e9 ? `$${(v / 1e9).toFixed(2)}B` :
        v >= 1e6 ? `$${(v / 1e6).toFixed(1)}M` : `$${Number(v).toLocaleString()}`;

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    function rowHtml(s) {
        // "owned" rows belong to this company and point at `other`; "inverse" rows are owned by
        // `other` and point back here — so the relevant counterparty is `other` either way.
        const rel = s.other ? (s.direction === 'inverse' ? `owned by ${esc(s.other)}` : `→ ${esc(s.other)}`) : '';
        const meta = [esc(s.type), rel, fmtValue(s.value)].filter(Boolean).join(' · ');
        return `<div class="linked-row" data-kind="${s.kind}" data-id="${s.id}">
            <span class="linked-kind ${s.kind}">${s.kind === 'revenue' ? 'REV' : 'COST'}</span>
            <div class="linked-row-main">
                <div class="linked-row-name">${esc(s.name)}</div>
                <div class="linked-row-meta">${meta}</div>
            </div>
            <button type="button" class="btn-detail" data-del-source style="color:var(--red);">DEL</button>
        </div>`;
    }

    function render(data) {
        const owned = data.owned || [];
        const inverse = data.inverse || [];
        if (!owned.length && !inverse.length) { finalize(); return; }
        let html = '';
        if (owned.length) html += `<div class="linked-group-label">This company's sources (block deletion)</div>` + owned.map(rowHtml).join('');
        if (inverse.length) html += `<div class="linked-group-label">Referenced by other companies</div>` + inverse.map(rowHtml).join('');
        body.innerHTML = html;
    }

    function openLinkedModalImpl(form, row) {
        ctx = { id: form.dataset.companyId, form, row, btn: form.querySelector('button') };
        title.textContent = `Linked sources — ${form.dataset.companyName || 'company'}`;
        body.innerHTML = `<div class="modal-empty" style="color:var(--muted);">Loading…</div>`;
        overlay.hidden = false;
        fetch(`/companies/${ctx.id}/linked-sources`)
            .then(r => r.ok ? r.json() : Promise.reject())
            .then(render)
            .catch(() => { body.innerHTML = `<div class="modal-empty" style="color:var(--red);">Failed to load linked sources.</div>`; });
    }
    // Expose to the delete handler above.
    window.openLinkedModal = openLinkedModalImpl;

    // Delete a single source row, then auto-delete the company once none remain.
    function deleteSource(rowEl) {
        const kind = rowEl.dataset.kind, id = rowEl.dataset.id;
        const url = kind === 'revenue' ? `/api/RevenueSources/${id}` : `/api/CostSources/${id}`;
        const btn = rowEl.querySelector('[data-del-source]');
        if (btn) btn.disabled = true;
        fetch(url, { method: 'DELETE' })
            .then(r => {
                if (!r.ok) throw 0;
                rowEl.remove();
                if (!body.querySelector('.linked-row')) finalize();
            })
            .catch(() => { if (btn) btn.disabled = false; uiAlert('Could not delete that source — try again.'); });
    }

    // All blocking + inverse sources gone: delete the company via its original form, drop its row.
    function finalize() {
        body.innerHTML = `<div class="modal-empty">All sources cleared — deleting company…</div>`;
        fetch(ctx.form.action, { method: 'POST', body: new FormData(ctx.form) })
            .then(r => {
                if (!r.ok) throw 0;
                if (ctx.row) ctx.row.remove();
                close();
            })
            .catch(() => { body.innerHTML = `<div class="modal-empty" style="color:var(--red);">Company delete failed — refresh and try again.</div>`; });
    }

    function close() {
        overlay.hidden = true;
        if (ctx && ctx.btn) ctx.btn.disabled = false;
        ctx = null;
        body.innerHTML = '';
    }

    body.addEventListener('click', e => {
        const rowEl = e.target.closest('[data-del-source]') && e.target.closest('.linked-row');
        if (rowEl) deleteSource(rowEl);
    });
    overlay.addEventListener('click', e => {
        if (e.target === overlay || e.target.closest('[data-modal-close]')) close();
    });
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && !overlay.hidden) close(); });
})();

// ── Live Data Ticker ──
(function () {
    const track = document.getElementById('ticker-track');
    if (!track) return;

    const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    const itemHtml = it => `
        <a class="ticker-item" href="${escapeHtml(it.href)}" data-kind="${escapeHtml(it.kind)}" data-tone="${escapeHtml(it.tone)}">
            <span class="ticker-kind">${escapeHtml(it.kind)}</span>
            <span class="ticker-label">${escapeHtml(it.label)}</span>
            <span class="ticker-value">${escapeHtml(it.value)}</span>
        </a>`;

    // Page-aware: exclude the entity type matching current URL so each page
    // surfaces data from the *other* sections.
    const excludeMap = {
        '/countries': 'countries',
        '/companies': 'companies',
        '/events':    'events'
    };
    const path = window.location.pathname.toLowerCase();
    const exclude = Object.entries(excludeMap)
        .find(([prefix]) => path.startsWith(prefix))?.[1] ?? '';
    const feedUrl = exclude ? `/api/ticker/feed?exclude=${exclude}` : '/api/ticker/feed';

    track.innerHTML = `<span class="ticker-skeleton">Loading feed…</span>`;

    fetch(feedUrl)
        .then(r => {
            console.log('[ticker] fetch status:', r.status);
            return r.ok ? r.json() : Promise.reject('HTTP ' + r.status);
        })
        .then(items => {
            console.log('[ticker] received', items.length, 'items');
            if (!items.length) {
                track.innerHTML = `<span class="ticker-skeleton">Feed empty — check DB has events/countries/companies with metrics.</span>`;
                return;
            }
            // Render twice for seamless loop: animation translates from -50% to 0,
            // so visible viewport always shows continuous content.
            const html = items.map(itemHtml).join('');
            track.innerHTML = html + html;

            // Scroll speed ~80px/sec, but clamp 30s..120s.
            requestAnimationFrame(() => {
                const oneCopyWidth = track.scrollWidth / 2;
                const seconds = Math.min(120, Math.max(30, Math.round(oneCopyWidth / 80)));
                track.style.animationDuration = `${seconds}s`;
                console.log('[ticker] track width', track.scrollWidth, 'duration', seconds + 's');
            });
        })
        .catch(err => {
            console.error('[ticker] failed:', err);
            track.innerHTML = `<span class="ticker-skeleton">Feed unavailable (${err}). Check /api/ticker/feed in network tab.</span>`;
        });

    // Pause on hover — toggle a class so animation-play-state hooks via CSS.
    track.addEventListener('mouseenter', () => track.classList.add('paused'));
    track.addEventListener('mouseleave', () => track.classList.remove('paused'));
})();

// ── Background-scan notification widget ──
// Detached scans are owned by the server; this widget is rebuilt from it on every page (full
// reloads wipe JS state, so the only thing that survives in the browser is the list of job ids
// kept in localStorage). Polls /extraction/scan-jobs until nothing is running.
(function () {
    const root = document.getElementById('scanNotify');
    if (!root) return;

    const $ = id => document.getElementById(id);
    const chip = $('scanNotifyChip'), badge = $('scanNotifyBadge'), panel = $('scanNotifyPanel');
    const listEl = $('scanNotifyList'), chatEl = $('scanNotifyChat');
    const logEl = $('scanNotifyLog'), inputEl = $('scanNotifyInput'), sendBtn = $('scanNotifySend');
    const headEl = $('scanNotifyChatHead'), openBtn = $('scanNotifyOpen'), savesEl = $('scanNotifySaves');
    const savesGrip = $('scanNotifySavesGrip');
    const SAVES_H_KEY = 'bbt.scanSavesH';   // user-dragged checklist height (px)
    const editModal = $('scanEditModal'), editBody = $('scanEditBody'), editTitle = $('scanEditTitle');
    // Classification options per node — mirror the extraction form's enum dropdowns.
    const CLASS_OPTS = {
        REVENUE: ['CUSTOMER', 'SEGMENT', 'REGION', 'PRODUCT'],
        COST: ['COGS', 'OPEX', 'TOTAL_COSTS'],
        RISK: ['MACROECONOMIC', 'INDUSTRY', 'BUSINESS', 'LEGAL_REGULATORY', 'FINANCIAL', 'GENERAL']
    };

    const IDS_KEY = 'bbt.scanJobs';                 // array of tracked job ids
    const chatKey = id => `bbt.scanChat.${id}`;     // per-job visible turns [{role,content}]
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    const read = (k, def) => { try { return JSON.parse(localStorage.getItem(k)) ?? def; } catch { return def; } };
    const write = (k, v) => localStorage.setItem(k, JSON.stringify(v));
    const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    // Hide ```save {json}``` blocks from the prose shown in bubbles (kept verbatim in history).
    // Collapse the blank lines the removed blocks leave behind so stacked saves don't gap the text.
    const stripSave = t => t.replace(/```save[\s\S]*?```/g, '').replace(/\n{3,}/g, '\n\n').trim();
    const fmtElapsed = ms => {
        const s = Math.max(0, Math.floor(ms / 1000));
        return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`;
    };

    let trackedIds = read(IDS_KEY, []);
    let jobs = [];                 // latest server snapshot for tracked ids
    const prevStatus = {};         // id -> last seen status, to detect running->done
    const prevReplying = {};       // id -> last seen replying flag, to detect reply-finished
    let openJobId = null;          // job whose chat is expanded, or null
    let timer = null, elapsedTimer = null, chatTimer = null;
    // The in-flight reply, mirrored from the server buffer by polling: { jobId, reply, think,
    // replying, error }. Server-owned, so it survives both minimize and full page navigation.
    let live = null;
    let thinkOpen = false;          // user's expand/collapse choice for the live "thinking" block;
                                    // persisted across paintStreaming() rebuilds (else each poll reopens it)
    let saveSel = new Set();        // ticked save keys (original block name) for the open job
    let saveSelJob = null;          // which job saveSel belongs to (reset on switch)
    let saveEdits = {};             // key -> field overrides the user applied in the edit popup
    let editingKey = null;          // save key currently open in the edit popup
    let forceBottom = false;        // one-shot: scroll chat to bottom on the next render (open/send)
    // True when the chat log is scrolled to (or near) the bottom — so polls only auto-scroll then,
    // never yanking the user down while they read older messages.
    const atBottom = () => logEl.scrollHeight - logEl.scrollTop - logEl.clientHeight < 40;

    function trackedJobs() { return jobs.filter(j => trackedIds.includes(j.id)); }
    function jobById(id) { return jobs.find(j => j.id === id); }

    async function poll() {
        if (!trackedIds.length) { jobs = []; render(); stopTimer(); return; }
        try {
            const res = await fetch(`/extraction/scan-jobs?ids=${encodeURIComponent(trackedIds.join(','))}`);
            if (res.ok) jobs = await res.json();
        } catch { /* offline — keep last snapshot, try again next tick */ }
        // Detect a fresh scan completion or a fresh reply completion, to notify the user.
        let justDone = false, replyDone = false;
        for (const j of jobs) {
            if (prevStatus[j.id] === 'Running' && j.status !== 'Running') justDone = true;
            if (prevReplying[j.id] && !j.replying) { replyDone = true; refreshReply(j.id); } // pull the final answer into history
            prevStatus[j.id] = j.status;
            prevReplying[j.id] = j.replying;
        }
        render();
        if ((justDone || replyDone) && panel.hidden) { panel.hidden = false; }   // surface on completion
        // Keep polling while anything is scanning OR generating a reply (so the chip stays live).
        if (!jobs.some(j => j.status === 'Running' || j.replying)) stopTimer();
    }

    function startTimer() {
        if (!timer) timer = setInterval(poll, 2500);
        // 1s ticker keeps the "running for Ns" label live between polls.
        if (!elapsedTimer) elapsedTimer = setInterval(tickElapsed, 1000);
    }
    function stopTimer() {
        if (timer) { clearInterval(timer); timer = null; }
        if (elapsedTimer) { clearInterval(elapsedTimer); elapsedTimer = null; }
    }
    function tickElapsed() {
        document.querySelectorAll('.scan-notify-elapsed[data-start]').forEach(el =>
            el.textContent = fmtElapsed(Date.now() - Number(el.dataset.start)));
    }

    function render() {
        const tj = trackedJobs();
        root.hidden = tj.length === 0;
        badge.textContent = tj.length;
        const running = tj.some(j => j.status === 'Running');
        root.classList.toggle('has-running', running);
        root.classList.toggle('has-done', !running && tj.some(j => j.status === 'Done'));
        const replying = tj.some(j => j.replying) || (live && live.replying);
        root.classList.toggle('has-replying', !!replying);   // chip pulses while the AI replies
        if (openJobId && jobById(openJobId)) renderChat(); else renderList(tj);
    }

    function renderList(tj) {
        chatEl.hidden = true; listEl.hidden = false;
        if (!tj.length) { listEl.innerHTML = `<div class="scan-notify-empty">No scans.</div>`; return; }
        listEl.innerHTML = tj.map(j => {
            const replying = j.replying || (live && live.replying && live.jobId === j.id);
            const st = replying ? 'replying' : j.status.toLowerCase();
            const statusLabel = replying ? 'REPLYING…' : j.status;
            const running = j.status === 'Running';
            const meta = replying ? 'AI is answering…'
                : j.status === 'Done' ? `${j.found} found`
                : j.status === 'Error' ? (j.error || 'failed') : 'live';
            // While running, show the worker's current phase + a live elapsed timer.
            const startMs = j.createdAt ? Date.parse(j.createdAt) : Date.now();
            const elapsed = running
                ? `<span class="scan-notify-elapsed" data-start="${startMs}">${escapeHtml(fmtElapsed(Date.now() - startMs))}</span>` : '';
            const progress = running
                ? `<div class="scan-notify-progress">${escapeHtml(j.progress || 'starting…')}</div>` : '';
            return `<div class="scan-notify-job" data-id="${j.id}">
                <div class="scan-notify-job-title">${escapeHtml(j.companyName || 'Company')} · ${escapeHtml(j.filingLabel)}
                    <span class="scan-notify-node">${escapeHtml(j.node || '')}</span></div>
                <div class="scan-notify-job-meta">
                    <span class="scan-notify-status ${st}">${escapeHtml(statusLabel)}</span>
                    <span>${escapeHtml(meta)}</span>
                    ${elapsed}
                    <button class="scan-notify-job-x" data-dismiss="${j.id}" title="Dismiss">&times;</button>
                </div>
                ${progress}
            </div>`;
        }).join('');
    }

    function ensureChatHistory(job) {
        let h = read(chatKey(job.id), null);
        // Seed with the auto summary the server produced — but only once it exists (a still-running
        // job has no summary yet), so we never persist a placeholder that outlives completion.
        if (!h && job.status !== 'Running' && job.summary) {
            h = [
                { role: 'user', content: 'Summarize the candidates you found in this filing.' },
                { role: 'assistant', content: job.summary }
            ];
            write(chatKey(job.id), h);
        }
        return h || [];
    }

    function renderChat() {
        const job = jobById(openJobId);
        listEl.hidden = true; chatEl.hidden = false;
        // Decide BEFORE rebuilding whether to keep pinning to the bottom (forced on open/send, else
        // only if the user was already there). Preserve scroll position otherwise.
        const stick = forceBottom || atBottom();
        const prevTop = logEl.scrollTop;
        forceBottom = false;
        headEl.textContent = `${job.companyName || 'Company'} · ${job.filingLabel} · ${job.node}`;
        const h = ensureChatHistory(job);
        logEl.innerHTML = h.map(m =>
            `<div class="scan-notify-bubble ${m.role}"><span class="role">${m.role}</span>${escapeHtml(stripSave(m.content) || m.content)}</div>`
        ).join('');
        paintStreaming();   // re-attach any in-flight reply so re-renders never drop it
        const replying = live && live.replying && live.jobId === openJobId;
        sendBtn.disabled = !!replying;
        inputEl.placeholder = replying ? 'AI is answering…' : 'Ask about this filing…';
        renderSaves(job);
        logEl.scrollTop = stick ? logEl.scrollHeight : prevTop;
    }

    // Normalize one ```save``` block JSON to the batch-save item shape (snake_case → camelCase).
    function normalizeSave(j) {
        return {
            name: j.name || '',
            classification: j.classification || '',
            value: j.value != null && j.value !== '' ? Number(j.value) : null,
            percentage: j.percentage != null && j.percentage !== '' ? Number(j.percentage) : null,
            note: j.note ?? null,
            relatedCompany: j.related_company || j.relatedCompany || null,
            relatedCompanyTicker: j.related_company_ticker || j.relatedCompanyTicker || null,
            proof: {
                name: j.proof?.name ?? null, value: j.proof?.value ?? null,
                percentage: j.proof?.percentage ?? null, classification: j.proof?.classification ?? null,
                relatedCompany: j.proof?.related_company ?? j.proof?.relatedCompany ?? null,
                note: j.proof?.note ?? null
            }
        };
    }

    // Every ```save``` block across the stored conversation, deduped by name (latest wins).
    function parseSaves(id) {
        const byName = new Map();
        const re = /```save\s*([\s\S]*?)```/g;
        for (const m of read(chatKey(id), [])) {
            if (m.role !== 'assistant') continue;
            let x; re.lastIndex = 0;
            while ((x = re.exec(m.content)) !== null) {
                let j; try { j = JSON.parse(x[1].trim()); } catch { continue; }
                const s = normalizeSave(j);
                if (s.name) { s.key = s.name; byName.set(s.name, s); }   // key = original block name
            }
        }
        return [...byName.values()];
    }

    function renderSaves(job) {
        if (saveSelJob !== job.id) { saveSel = new Set(); saveEdits = {}; saveSelJob = job.id; }
        // Merge any user edits onto the parsed blocks (key survives a rename).
        const items = parseSaves(job.id).map(s => Object.assign({}, s, saveEdits[s.key] || {}));
        if (!items.length) { savesEl.hidden = true; if (savesGrip) savesGrip.hidden = true; savesEl.innerHTML = ''; return; }
        savesEl.hidden = false;
        if (savesGrip) savesGrip.hidden = false;
        savesEl.style.height = (read(SAVES_H_KEY, 200)) + 'px';   // restore the dragged height
        const rows = items.map((s, i) => {
            const bits = [];
            if (s.value != null) bits.push('$' + Number(s.value).toLocaleString());
            if (s.percentage != null) bits.push(s.percentage + '%');
            const cp = s.relatedCompany
                ? `<span class="scan-notify-save-cp">↔ ${escapeHtml(s.relatedCompany)}${s.relatedCompanyTicker ? ' (' + escapeHtml(s.relatedCompanyTicker) + ')' : ''}</span>` : '';
            return `<div class="scan-notify-save-row">
                <input type="checkbox" data-save="${i}" ${saveSel.has(s.key) ? 'checked' : ''}>
                <span class="scan-notify-save-main" data-edit="${i}" title="Click to edit">
                    <span class="scan-notify-save-name">${escapeHtml(s.name)}</span>
                    <span class="scan-notify-save-meta">${escapeHtml(s.classification || '—')}${bits.length ? ' · ' + escapeHtml(bits.join(' · ')) : ''} ${cp}</span>
                </span></div>`;
        }).join('');
        savesEl.innerHTML =
            `<div class="scan-notify-saves-head"><span>Proposed saves</span><span>${items.length}</span></div>` +
            rows +
            `<div class="scan-notify-save-bar">
                <button class="scan-notify-save-btn" data-savebtn ${saveSel.size ? '' : 'disabled'}>Save selected (${saveSel.size})</button>
                <span class="scan-notify-save-status"></span>
            </div>`;
        // keep `items` reachable by the click handlers
        savesEl._items = items;
    }

    async function saveSelected(job) {
        const items = (savesEl._items || []).filter(s => saveSel.has(s.key));
        if (!items.length) return;
        const statusEl = savesEl.querySelector('.scan-notify-save-status');
        const btn = savesEl.querySelector('[data-savebtn]');
        if (btn) { btn.disabled = true; btn.textContent = 'Saving…'; }
        if (statusEl) statusEl.textContent = '';
        try {
            const res = await fetch('/extraction/save-batch', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({
                    companyId: job.companyId, node: job.node,
                    accession: job.accession, form: job.form, items
                })
            });
            if (!res.ok) { if (statusEl) { statusEl.style.color = 'var(--red)'; statusEl.textContent = `Save failed (${res.status})`; } return; }
            const r = await res.json();
            saveSel = new Set();   // clear ticks after a successful save
            renderSaves(job);
            const s2 = savesEl.querySelector('.scan-notify-save-status');
            if (s2) s2.textContent = `Saved ${r.saved}${r.links ? ` · ${r.links} linked` : ''}.`;
        } catch {
            if (statusEl) { statusEl.style.color = 'var(--red)'; statusEl.textContent = 'Network error'; }
        }
    }

    // The live reply bubble is rebuilt from `live` (mirrored from the server buffer) on every
    // paint, so closing the panel, Back, navigation, or a poll re-render never lose the text.
    function paintStreaming() {
        document.getElementById('scanNotifyStream')?.remove();
        if (!live || live.jobId !== openJobId) return;
        if (!live.replying && !live.reply && !live.error) return;
        const bubble = document.createElement('div');
        bubble.id = 'scanNotifyStream';
        bubble.className = 'scan-notify-bubble assistant';
        bubble.innerHTML = `<span class="role">assistant</span>`;
        if (live.think) {
            const d = document.createElement('details');
            d.className = 'scan-notify-think'; d.open = thinkOpen;   // remember the user's choice across rebuilds
            d.addEventListener('toggle', () => { thinkOpen = d.open; });
            const s = document.createElement('summary');
            s.className = 'scan-notify-think-summary'; s.textContent = 'Thinking';
            const t = document.createElement('div');
            t.className = 'scan-notify-think-body'; t.textContent = live.think;
            d.append(s, t);
            bubble.appendChild(d);
        }
        const span = document.createElement('span');
        span.textContent = live.error ? `[${live.error}]`
            : live.reply ? stripSave(live.reply)
            : '⏳ replying…';
        bubble.appendChild(span);
        logEl.appendChild(bubble);
        // No scroll here — renderChat() decides whether to pin to the bottom (respects user scroll).
    }

    function lastStoredRole(id) {
        const h = read(chatKey(id), []);
        return h.length ? h[h.length - 1].role : null;
    }
    function startChatPoll() { if (!chatTimer) chatTimer = setInterval(() => refreshReply(openJobId), 1000); }
    function stopChatPoll() { if (chatTimer) { clearInterval(chatTimer); chatTimer = null; } }

    // Mirror the server reply buffer into `live`. When generation finishes, append the final
    // answer to the stored history exactly once (guarded by "last stored turn is the user's").
    async function refreshReply(id) {
        if (!id) return;
        let s;
        try {
            const res = await fetch(`/extraction/scan-jobs/${id}/reply`);
            if (!res.ok) return;
            s = await res.json();
        } catch { return; }
        live = { jobId: id, reply: s.reply || '', think: s.think || '', replying: s.replying, error: s.error || null };
        if (s.replying) startChatPoll();
        else {
            stopChatPoll();
            if (s.reply && lastStoredRole(id) === 'user') {
                const h = read(chatKey(id), []);
                h.push({ role: 'assistant', content: s.reply });
                write(chatKey(id), h);
                live = null;   // it's in history now; render from there
            } else if (!s.error) {
                live = null;
            }
        }
        if (openJobId === id) renderChat();
        render();   // refresh chip/pill replying state
    }

    async function sendChat() {
        const job = jobById(openJobId);
        const text = inputEl.value.trim();
        if (!job || (live && live.replying) || !text) return;
        if (job.status !== 'Done') { inputEl.placeholder = 'Wait for the scan to finish…'; return; }
        inputEl.value = '';
        const h = ensureChatHistory(job);
        h.push({ role: 'user', content: text });
        write(chatKey(job.id), h);
        // Kick off detached server generation, then poll its buffer. No page-bound fetch to abort.
        live = { jobId: job.id, reply: '', think: '', replying: true, error: null };
        forceBottom = true;   // jump to the new turn when the user sends
        renderChat();
        try {
            const res = await fetch(`/extraction/scan-jobs/${job.id}/reply`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ messages: h })
            });
            if (!res.ok) { live = { jobId: job.id, reply: '', think: '', replying: false, error: `error ${res.status}` }; renderChat(); return; }
        } catch { live = { jobId: job.id, reply: '', think: '', replying: false, error: 'network error' }; renderChat(); return; }
        startChatPoll(); startTimer();   // chip poll + reply poll both run
    }

    // ── events ──
    chip.addEventListener('click', () => { panel.hidden = !panel.hidden; });
    $('scanNotifyClose').addEventListener('click', () => { panel.hidden = true; });
    $('scanNotifyBack').addEventListener('click', () => { openJobId = null; stopChatPoll(); render(); });

    listEl.addEventListener('click', e => {
        const dismiss = e.target.closest('[data-dismiss]');
        if (dismiss) {
            e.stopPropagation();
            const id = dismiss.getAttribute('data-dismiss');
            trackedIds = trackedIds.filter(x => x !== id);
            write(IDS_KEY, trackedIds);
            localStorage.removeItem(chatKey(id));
            if (live && live.jobId === id) { live = null; stopChatPoll(); }
            fetch(`/extraction/scan-jobs/dismiss/${id}`, {
                method: 'POST', headers: { 'RequestVerificationToken': token() }
            }).catch(() => {});
            poll();
            return;
        }
        const card = e.target.closest('.scan-notify-job');
        if (card) {
            openJobId = card.getAttribute('data-id');
            forceBottom = true;        // opening a job starts at the latest message
            render();
            refreshReply(openJobId);   // resume any reply still being generated server-side
        }
    });

    sendBtn.addEventListener('click', sendChat);
    inputEl.addEventListener('keydown', e => {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendChat(); }
    });

    // Tick / untick a proposed save; the Set survives the periodic re-renders.
    savesEl.addEventListener('change', e => {
        const cb = e.target.closest('[data-save]');
        if (!cb) return;
        const s = (savesEl._items || [])[Number(cb.getAttribute('data-save'))];
        if (!s) return;
        if (cb.checked) saveSel.add(s.key); else saveSel.delete(s.key);
        const btn = savesEl.querySelector('[data-savebtn]');
        if (btn) { btn.disabled = !saveSel.size; btn.textContent = `Save selected (${saveSel.size})`; }
    });
    savesEl.addEventListener('click', e => {
        if (e.target.closest('[data-savebtn]')) {
            const job = jobById(openJobId);
            if (job) saveSelected(job);
            return;
        }
        const main = e.target.closest('[data-edit]');
        if (main) {
            const s = (savesEl._items || [])[Number(main.getAttribute('data-edit'))];
            if (s) openEdit(s);
        }
    });

    // ── edit-one-object popup ──
    const field = (label, inner) => `<div class="scan-edit-field"><label>${label}</label>${inner}</div>`;
    function openEdit(s) {
        const job = jobById(openJobId);
        if (!job || !editModal) return;
        editingKey = s.key;
        const node = (job.node || 'REVENUE').toUpperCase();
        const opts = (CLASS_OPTS[node] || []).map(o =>
            `<option value="${o}" ${s.classification === o ? 'selected' : ''}>${o}</option>`).join('');
        editTitle.textContent = `Edit ${node.toLowerCase()} object`;
        let html = field('Name', `<input id="se_name" value="${escapeHtml(s.name || '')}">`)
            + field('Classification', `<select id="se_class"><option value=""></option>${opts}</select>`);
        if (node === 'RISK') {
            html += field('Note', `<textarea id="se_note">${escapeHtml(s.note || '')}</textarea>`);
        } else {
            html += field('Value (USD)', `<input id="se_value" type="number" step="any" value="${s.value != null ? s.value : ''}">`)
                + field('Percentage', `<input id="se_pct" type="number" step="any" value="${s.percentage != null ? s.percentage : ''}">`)
                + field('Related company', `<input id="se_rel" value="${escapeHtml(s.relatedCompany || '')}">`)
                + field('Related ticker', `<input id="se_tick" value="${escapeHtml(s.relatedCompanyTicker || '')}">`);
        }
        editBody.innerHTML = html;
        editModal.hidden = false;
        $('se_name').focus();
    }
    function closeEdit() { editModal.hidden = true; editingKey = null; }
    function applyEdit() {
        if (editingKey == null) return;
        const numOrNull = v => v === '' || v == null ? null : Number(v);
        const job = jobById(openJobId);
        const node = (job?.node || 'REVENUE').toUpperCase();
        const edit = {
            name: $('se_name').value.trim(),
            classification: $('se_class').value || null
        };
        if (node === 'RISK') {
            edit.note = $('se_note').value.trim() || null;
        } else {
            edit.value = numOrNull($('se_value').value);
            edit.percentage = numOrNull($('se_pct').value);
            edit.relatedCompany = $('se_rel').value.trim() || null;
            edit.relatedCompanyTicker = $('se_tick').value.trim() || null;
        }
        saveEdits[editingKey] = Object.assign({}, saveEdits[editingKey], edit);
        saveSel.add(editingKey);   // editing implies you want to keep it
        closeEdit();
        if (job) renderSaves(job);
    }
    $('scanEditApply')?.addEventListener('click', applyEdit);
    $('scanEditCancel')?.addEventListener('click', closeEdit);
    $('scanEditClose')?.addEventListener('click', closeEdit);
    editModal?.addEventListener('click', e => { if (e.target === editModal) closeEdit(); });

    // Hold-and-drag the grip to resize the checklist; dragging up makes it taller. Height persists.
    savesGrip?.addEventListener('pointerdown', e => {
        e.preventDefault();
        const startY = e.clientY, startH = savesEl.offsetHeight;
        savesGrip.setPointerCapture(e.pointerId);
        const move = ev => {
            // Cap against the chat column (not the viewport): the saves block grows by
            // stealing space from the log, so leave the log a ~160px floor.
            const cap = Math.max(140, chatEl.clientHeight - 160);
            const h = Math.max(100, Math.min(cap, startH + (startY - ev.clientY)));
            savesEl.style.height = h + 'px';
        };
        const up = () => {
            savesGrip.releasePointerCapture(e.pointerId);
            savesGrip.removeEventListener('pointermove', move);
            savesGrip.removeEventListener('pointerup', up);
            write(SAVES_H_KEY, savesEl.offsetHeight);
        };
        savesGrip.addEventListener('pointermove', move);
        savesGrip.addEventListener('pointerup', up);
    });
    openBtn.addEventListener('click', () => {
        const job = jobById(openJobId);
        if (!job) return;
        const qs = new URLSearchParams({
            companyId: job.companyId, accession: job.accession, doc: job.doc,
            node: job.node, jobId: job.id
        });
        if (job.form) qs.set('form', job.form);
        window.location.href = `/extraction?${qs}`;
    });

    // Called by the extraction page when it hands a freshly-started scan to the widget. `meta`
    // (company/filing/node) lets the window render the task instantly, before the first poll lands.
    window.startScanJob = function (jobId, meta) {
        if (!jobId) return;
        if (!trackedIds.includes(jobId)) { trackedIds.push(jobId); write(IDS_KEY, trackedIds); }
        prevStatus[jobId] = 'Running';
        if (!jobById(jobId)) {
            jobs.push(Object.assign({
                id: jobId, status: 'Running', progress: 'Starting…', found: 0,
                companyName: '', filingLabel: '', node: '', accession: '', doc: '',
                form: null, companyId: 0, summary: '', createdAt: new Date().toISOString()
            }, meta || {}));
        }
        openJobId = null;
        render();
        panel.hidden = false;   // pop the window open immediately so the user sees it working
        poll(); startTimer();
    };

    // On load: rebuild from whatever this browser is tracking.
    if (trackedIds.length) { poll(); startTimer(); }
})();

