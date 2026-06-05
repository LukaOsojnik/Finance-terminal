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

