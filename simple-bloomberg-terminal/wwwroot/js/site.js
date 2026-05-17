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

