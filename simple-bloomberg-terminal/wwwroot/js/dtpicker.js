(function () {
  'use strict';

  const MONTHS = {
    'hr': ['Siječanj','Veljača','Ožujak','Travanj','Svibanj','Lipanj','Srpanj','Kolovoz','Rujan','Listopad','Studeni','Prosinac'],
    'en': ['January','February','March','April','May','June','July','August','September','October','November','December']
  };
  const DOW = {
    'hr': ['Po','Ut','Sr','Če','Pe','Su','Ne'], // Monday-first
    'en': ['Su','Mo','Tu','We','Th','Fr','Sa']  // Sunday-first
  };

  function langKey(locale) {
    return (locale || 'en').toLowerCase().startsWith('hr') ? 'hr' : 'en';
  }

  function firstDayOfWeek(locale) {
    return langKey(locale) === 'hr' ? 1 : 0;
  }

  function pad(n) { return String(n).padStart(2, '0'); }

  function formatDate(date, fmt) {
    return fmt
      .replace(/yyyy/g, date.getFullYear())
      .replace(/MM/g, pad(date.getMonth() + 1))
      .replace(/dd/g, pad(date.getDate()))
      .replace(/M/g, date.getMonth() + 1)
      .replace(/d/g, date.getDate());
  }

  function formatDisplay(date, fmt, showTime) {
    const base = formatDate(date, fmt);
    if (!showTime) return base;
    return base + ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes());
  }

  function toIso(date, showTime) {
    const base = date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate());
    if (!showTime) return base;
    return base + 'T' + pad(date.getHours()) + ':' + pad(date.getMinutes());
  }

  function parseIso(iso) {
    if (!iso) return null;
    const d = new Date(iso);
    return isNaN(d.getTime()) ? null : d;
  }

  function parseLocalizedDate(text, fmt, showTime) {
    if (!text) return null;
    const sep = (fmt.match(/[^a-zA-Z]/) || ['.'])[0];
    const order = fmt.split(sep).map(t => t[0].toLowerCase()); // ['d','m','y'] or ['m','d','y']
    let datePart = text, timePart = '';
    const spaceIdx = text.indexOf(' ');
    if (spaceIdx > 0) { datePart = text.slice(0, spaceIdx); timePart = text.slice(spaceIdx + 1); }
    const parts = datePart.split(sep);
    if (parts.length !== 3) return null;
    const map = {};
    for (let i = 0; i < 3; i++) map[order[i]] = parseInt(parts[i], 10);
    const y = map.y, m = map.m, d = map.d;
    if (!Number.isFinite(y) || !Number.isFinite(m) || !Number.isFinite(d)) return null;
    if (String(map.y).length !== 4 || m < 1 || m > 12 || d < 1 || d > 31) return null;
    let hh = 0, mm = 0;
    if (showTime && timePart) {
      const tp = timePart.split(':');
      hh = parseInt(tp[0], 10); mm = parseInt(tp[1], 10);
      if (!Number.isFinite(hh) || !Number.isFinite(mm) || hh < 0 || hh > 23 || mm < 0 || mm > 59) return null;
    }
    const dt = new Date(y, m - 1, d, hh, mm);
    if (dt.getFullYear() !== y || dt.getMonth() !== m - 1 || dt.getDate() !== d) return null;
    return dt;
  }

  function renderPanel(state) {
    const { panel, current, selected, locale, showTime } = state;
    const lk = langKey(locale);
    const fdow = firstDayOfWeek(locale);
    const monthName = MONTHS[lk][current.getMonth()];
    const year = current.getFullYear();

    const firstOfMonth = new Date(year, current.getMonth(), 1);
    const startDay = (firstOfMonth.getDay() - fdow + 7) % 7;
    const daysInMonth = new Date(year, current.getMonth() + 1, 0).getDate();
    const prevMonthDays = new Date(year, current.getMonth(), 0).getDate();

    let html = `
      <div class="dtpicker-head">
        <button type="button" class="dtpicker-nav" data-act="prev">&lt;</button>
        <span class="dtpicker-title">${monthName} ${year}</span>
        <button type="button" class="dtpicker-nav" data-act="next">&gt;</button>
      </div>
      <div class="dtpicker-grid">
    `;

    const dowLabels = DOW[lk];
    for (let i = 0; i < 7; i++) {
      html += `<div class="dtpicker-dow">${dowLabels[i]}</div>`;
    }

    const today = new Date();
    const isSameDay = (a, b) => a && b &&
      a.getFullYear() === b.getFullYear() &&
      a.getMonth() === b.getMonth() &&
      a.getDate() === b.getDate();

    for (let i = 0; i < startDay; i++) {
      const d = prevMonthDays - startDay + 1 + i;
      const dt = new Date(year, current.getMonth() - 1, d);
      html += `<div class="dtpicker-day is-other-month" data-iso="${toIso(dt, false)}">${d}</div>`;
    }
    for (let d = 1; d <= daysInMonth; d++) {
      const dt = new Date(year, current.getMonth(), d);
      const cls = ['dtpicker-day'];
      if (isSameDay(dt, today)) cls.push('is-today');
      if (isSameDay(dt, selected)) cls.push('is-selected');
      html += `<div class="${cls.join(' ')}" data-iso="${toIso(dt, false)}">${d}</div>`;
    }
    const totalCells = startDay + daysInMonth;
    const trailing = (7 - (totalCells % 7)) % 7;
    for (let i = 1; i <= trailing; i++) {
      const dt = new Date(year, current.getMonth() + 1, i);
      html += `<div class="dtpicker-day is-other-month" data-iso="${toIso(dt, false)}">${i}</div>`;
    }

    html += `</div>`;

    if (showTime) {
      const h = selected ? pad(selected.getHours()) : '00';
      const m = selected ? pad(selected.getMinutes()) : '00';
      html += `
        <div class="dtpicker-time">
          <input type="text" class="dtpicker-hh" maxlength="2" value="${h}"/>
          <span class="dtpicker-time-sep">:</span>
          <input type="text" class="dtpicker-mm" maxlength="2" value="${m}"/>
        </div>
      `;
    }

    html += `
      <div class="dtpicker-foot">
        <button type="button" class="dtpicker-btn" data-act="today">Today</button>
        <button type="button" class="dtpicker-btn" data-act="clear">Clear</button>
      </div>
    `;

    panel.innerHTML = html;
  }

  function setValue(state, date) {
    const { display, hidden, fmt, showTime } = state;
    if (!date) {
      state.selected = null;
      display.value = '';
      hidden.value = '';
    } else {
      state.selected = date;
      display.value = formatDisplay(date, fmt, showTime);
      hidden.value = toIso(date, showTime);
    }
    hidden.dispatchEvent(new Event('blur'));
    if (window.jQuery) {
      try { jQuery(hidden).valid && jQuery(hidden).valid(); } catch (e) {}
    }
  }

  function attachPanelEvents(state) {
    const { panel } = state;
    panel.addEventListener('mousedown', (e) => {
      if (!e.target.closest('input')) e.preventDefault();
    });
    panel.addEventListener('click', (e) => {
      e.stopPropagation();
      const day = e.target.closest('.dtpicker-day');
      if (day) {
        const iso = day.dataset.iso;
        const parts = iso.split('-');
        const newDate = new Date(+parts[0], +parts[1] - 1, +parts[2]);
        if (state.showTime && state.selected) {
          newDate.setHours(state.selected.getHours(), state.selected.getMinutes());
        }
        setValue(state, newDate);
        if (!state.showTime) {
          hide(state);
        } else {
          renderPanel(state);
        }
        return;
      }
      const nav = e.target.closest('.dtpicker-nav');
      if (nav) {
        if (nav.dataset.act === 'prev') {
          state.current = new Date(state.current.getFullYear(), state.current.getMonth() - 1, 1);
        } else {
          state.current = new Date(state.current.getFullYear(), state.current.getMonth() + 1, 1);
        }
        renderPanel(state);
        return;
      }
      const btn = e.target.closest('.dtpicker-btn');
      if (btn) {
        if (btn.dataset.act === 'today') {
          const t = new Date();
          state.current = new Date(t.getFullYear(), t.getMonth(), 1);
          setValue(state, t);
          if (!state.showTime) hide(state); else renderPanel(state);
        } else if (btn.dataset.act === 'clear') {
          setValue(state, null);
          hide(state);
        }
      }
    });

    panel.addEventListener('input', (e) => {
      if (!state.showTime || !state.selected) return;
      const hh = panel.querySelector('.dtpicker-hh');
      const mm = panel.querySelector('.dtpicker-mm');
      if (!hh || !mm) return;
      let h = Math.max(0, Math.min(23, parseInt(hh.value, 10) || 0));
      let m = Math.max(0, Math.min(59, parseInt(mm.value, 10) || 0));
      const d = new Date(state.selected);
      d.setHours(h, m);
      state.selected = d;
      state.display.value = formatDisplay(d, state.fmt, true);
      state.hidden.value = toIso(d, true);
    });
  }

  function show(state) {
    if (!state.panel.parentNode) state.root.appendChild(state.panel);
    state.panel.hidden = false;
    state.current = state.selected
      ? new Date(state.selected.getFullYear(), state.selected.getMonth(), 1)
      : new Date();
    renderPanel(state);
  }

  function hide(state) {
    state.panel.hidden = true;
  }

  function initOne(root) {
    const display = root.querySelector('.dtpicker-input');
    const hidden = root.querySelector('.dtpicker-hidden');
    if (!display || !hidden) return;

    const locale = root.dataset.dtpickerLocale || 'en-US';
    const fmt = root.dataset.dtpickerFormat || 'MM/dd/yyyy';
    const showTime = root.dataset.dtpickerShowtime === '1';

    const panel = document.createElement('div');
    panel.className = 'dtpicker-panel';
    panel.hidden = true;

    const state = {
      root, display, hidden, panel,
      locale, fmt, showTime,
      selected: parseIso(hidden.value),
      current: new Date()
    };

    if (state.selected) {
      display.value = formatDisplay(state.selected, fmt, showTime);
    }

    display.addEventListener('focus', () => show(state));
    display.addEventListener('click', () => show(state));

    display.addEventListener('change', () => {
      const parsed = parseLocalizedDate(display.value.trim(), fmt, showTime);
      if (parsed) {
        setValue(state, parsed);
      } else if (display.value.trim() === '') {
        setValue(state, null);
      } else if (state.selected) {
        display.value = formatDisplay(state.selected, fmt, showTime);
      }
    });

    display.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') { hide(state); display.blur(); }
      else if (e.key === 'Enter') {
        e.preventDefault();
        display.dispatchEvent(new Event('change'));
        hide(state);
      }
    });

    document.addEventListener('click', (e) => {
      if (!root.contains(e.target) && !panel.contains(e.target)) {
        hide(state);
      }
    });

    attachPanelEvents(state);
  }

  function initAll() {
    document.querySelectorAll('.dtpicker').forEach(initOne);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initAll);
  } else {
    initAll();
  }
})();
