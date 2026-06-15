# Design System — BBT Terminal UI

Read this before any styling / view work so edits stay on-theme. The look is a **dark financial
terminal** (Bloomberg-style) with an opt-in **light mode**. All of it is driven by CSS variables in
`wwwroot/css/site.css` — use the tokens, never hardcode.

## Golden rules

1. **Use tokens, not hex.** Color/spacing comes from the `:root` variables below. A raw hex value is a
   bug: it won't recolor in light mode and breaks the system. The only place literal hex is acceptable
   is inside the `[data-theme="light"]` override block (the hand-fixes for things tokens can't reach).
2. **Don't add raw Bootstrap.** `.form-control`, `.form-select`, `.btn-primary/secondary`,
   `.btn-outline-*`, `.alert-*`, `.text-muted`, `.form-text` are already themed in `site.css`. Reuse
   those classes — they inherit the terminal look. Don't restyle them per-view.
3. **Light-mode parity.** If you introduce a new component with a hardcoded dark color (a dark fill,
   a heavy shadow, an `a:hover{color:#fff}`), add a matching `[data-theme="light"]` override in the
   "LIGHT MODE" section of `site.css`. Test both themes via the navbar toggle.
4. **Two fonts.** `var(--font-display)` (Rajdhani) for headings/labels/buttons — uppercase, letter-
   spaced. `var(--font-mono)` (JetBrains Mono) for data, numbers, body, inputs.
5. **Scope control.** Don't restyle unrelated components while in a view. Match the surrounding
   markup's existing classes.

## Tokens (`:root` in site.css)

| Token | Dark | Role |
|-------|------|------|
| `--bg-base` | `#0a0e13` | page background |
| `--bg-surface` | `#0f1520` | inputs, nav, raised bars |
| `--bg-card` | `#121c2b` | cards, dropdowns, modals |
| `--bg-row-hover` | `#162030` | hover rows, ghost-button hover |
| `--border-subtle` | `#1e2d42` | default borders/dividers |
| `--border-accent` | `#00d4ff33` | accent-tinted borders |
| `--accent` | `#00d4ff` | primary accent (links, focus, primary btn) |
| `--accent-dim` | `#00d4ff66` | focus rings, dim accent |
| `--green` / `--red` | `#39d353` / `#f85149` | up/positive · down/negative |
| `--muted` | `#4a6278` | de-emphasized text |
| `--text-primary` | `#c9d8e8` | main text |
| `--text-secondary` | `#7a9ab5` | labels, secondary text |
| `--text-code` | `#e2eaf4` | numeric/code cells |
| `--on-accent` | `#04121b` | **text/icons sitting on an accent fill** (white in light mode) |
| `--radius-sm/md` | `4px / 6px` | corner radii |
| `--transition` | `140ms ease` | standard transition |

Light mode redefines these tokens under `[data-theme="light"]`; components follow automatically.

## Component patterns

- **Forms** — wrap each field: `<label class="form-label">` + `.form-control` / `.form-select` /
  `textarea.form-control`. Grouping cards: `.form-card` > `.form-card-title` > `.form-grid`/`.form-stack`.
  Sticky save bar: `.form-actions` with `.btn-primary` (submit) + `.btn-secondary` (cancel).
- **Buttons** — `.btn-primary` (accent fill, `--on-accent` ink), `.btn-secondary` (ghost outline),
  `.btn-outline-*` for table actions, `.btn-sm` for compact. All auto-uppercase via the base `.btn` rule.
- **Tables** — `.terminal-table` with cell classes `.code-cell` (accent), `.num-cell` (right, tabular),
  `.muted-cell`, `.name-cell`. Live/past row states: `.row-live` / `.row-past`.
- **Detail pages** — `.detail-hero`, `.detail-metric-card`, `.detail-card` + `.detail-card-title`.
- **Badges/pills** — `.impact-badge` (`.impact-high/medium/low`), `.type-badge`, `.map-status`.
- **Empty states** — `.empty-state` (dashed border, muted).
- **Alerts** — `.alert .alert-success/warning/danger/info` are themed; just use them.

## Theme toggle

- Navbar button `#themeToggle` (right of the profile pic in `_Layout.cshtml`); moon icon in dark, sun
  in light (swapped by CSS).
- Stored in `localStorage` key `bbt-theme` (`"light"` / `"dark"`; absent = dark default).
- Applied **pre-paint** by an inline `<head>` script in `_Layout` (FOUC guard). Click handler lives at
  the end of `wwwroot/js/site.js`. Theme flag sits on `<html data-theme>`.

## Where things live

- Tokens + all component CSS: `wwwroot/css/site.css` (light-mode block at the bottom).
- Auth pages: `wwwroot/css/auth.css`.
- Layout / nav / toggle: `Views/Shared/_Layout.cshtml`.
- Behavior: `wwwroot/js/site.js`.
