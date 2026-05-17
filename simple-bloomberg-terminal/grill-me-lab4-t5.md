# Grill Me — Lab 4 Task 5

Self-quiz over the custom date+time picker partial view.

---

## Architecture

### Q1. Why a partial view + `DateTimePickerModel` ViewModel instead of just inlining `<input type="text">` + per-form JS?

One render path, one JS init, one CSS file. Six forms share it (Events Create/Edit, Companies Create/Edit, TradeBlocs Create/Edit). Inline would mean six near-duplicate blocks of markup + validation wiring — and any tweak (new locale, time toggle, accent change) would need six edits. The partial also keeps validation attrs (`data-val-*`) generated once in C# rather than copy-pasted in Razor.

### Q2. Why visible text input + hidden ISO input instead of one input?

Locale display vs. server contract conflict. Visible input shows `17.05.2026 14:30` (hr) or `05/17/2026 02:30 PM` (en) — what the user reads. Hidden input always emits `2026-05-17T14:30` — what the ASP.NET Core `DateTime` model binder parses culture-invariant. Without the hidden input, server would try parse the locale text and fail when `CurrentCulture` differs from the user's input format. Two inputs = clean separation of presentation and contract.

### Q3. Why does the hidden input carry the `data-val-*` attributes, not the visible one?

Hidden carries the binding `name="Date"`, so it carries the validation contract. jQuery Validate associate errors with the named field — the same name the server uses in `ModelState`. The visible input is presentation only and has no `name`, so it's not part of the form post.

---

## Localization

### Q4. What does `UseRequestLocalization` actually do?

Reads the `Accept-Language` header on each request, negotiates against `SupportedCultures`, and sets `Thread.CurrentThread.CurrentCulture` + `CurrentUICulture` for the duration of the request. Downstream code that calls `DateTime.ToShortDateString()`, `decimal.ToString()`, `CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern`, etc. picks up the negotiated culture automatically — no parameter plumbing.

### Q5. Why is `DefaultRequestCulture` set to `hr` and not `en-US`?

Lab spec: `DefaultRequestCulture = new RequestCulture("hr")`. Faculty default, Croatian-first. Any other locale (en, de, fr) falls back to `hr` unless `Accept-Language: en-US` matches a registered supported culture. Two cultures registered: `hr` and `en-US`.

### Q6. Why does `parseLocalizedDate` derive separator + order from the `fmt` string instead of hardcoding `dd.MM.yyyy` / `MM/dd/yyyy`?

Forward-compat. Adding a third locale (e.g. `de` = `dd.MM.yyyy`, `ja` = `yyyy/MM/dd`) requires only registering it in `Program.cs` — the parser handles any single-separator pattern with `d`, `m`, `y` tokens. Hardcoded branches would scale poorly past two locales.

### Q7. Why does `parseLocalizedDate` reject 2-digit years?

`17.05.26` is ambiguous: is that 1926, 2026, or just bad input? Different libraries pivot at different years. Strict `yyyy` avoids the ambiguity entirely — bad input returns `null`, caller restores last valid value. Trade-off: user can't type shorthand, but typo recovery is cleaner.

---

## Validation

### Q8. Why call `jQuery(hidden).valid()` manually after picker commit instead of letting jQuery Validate auto-run?

jQuery Validate's default config is `ignore: ":hidden"` — it skips hidden inputs on auto-validation (`form.valid()` on submit, focusout-triggered checks). Since our binding field IS the hidden input, auto-validate would silently skip it. Explicit `.valid()` bypass the ignore filter on a single element. Same trick used in `_AutocompletePicker` (see `site.js:62`).

### Q9. How does the `dategte` (end-date ≥ start-date) client-side check find the other field?

`DateGreaterThanOrEqualAttribute.AddValidation` emits `data-val-dategte-other="Date"` on the EndDate hidden input. The adapter in `_ValidationScriptsPartial.cshtml` registers via `addSingleVal('dategte', 'other')` — jQuery Validate unobtrusive then call the custom method with `otherName = "Date"`. The method `$('[name="Date"]').val()` find the sibling by `name`. Both fields must be in the same form.

### Q10. Why does `DateGreaterThanOrEqualAttribute` have separate branches for `DateOnly` and `DateTime` instead of one common path?

`DateOnly` and `DateTime` are distinct CLR types — no common base for `<` comparison without boxing/conversion. Two narrow branches handle the two real cases (Company.AsOf = `DateOnly`, Event.Date/EndDate = `DateTime`). A reflection-based `Comparable` path would work but adds runtime cost and obscures intent.

---

## Picker UX bugs (the ones that bit me)

### Q11. Why did clicking the month-next arrow originally close the picker?

`renderPanel()` reassigns `panel.innerHTML`. When the user clicks the nav `<button>`:

1. Panel's click handler fires first → `renderPanel()` → original button node replaced.
2. Click bubbles to `document` → outside-click detector runs `panel.contains(e.target)`.
3. `e.target` is the original button — now orphaned, no longer a descendant of any DOM tree.
4. `panel.contains(orphan)` returns `false` → outside-click detector calls `hide()`.

Fix: `e.stopPropagation()` in the panel click handler. Internal handlers run; document handler never sees the click.

### Q12. Why is there `mousedown` preventDefault on the panel for non-input targets?

Without it, clicking a nav button blurs the visible input → `blur` listener (older version) ran → closed the picker. Standard pattern: `preventDefault` on `mousedown` stops the default focus-change. Excluded `<input>` targets (time hh/mm boxes) because those legitimately need focus. After moving the parse logic to `change` and removing the blur-close, this is belt-and-braces — keeps visible input focused for cleaner UX during nav.

### Q13. Why use `change` event instead of `blur` to commit manually-typed dates?

`blur` is too eager — fires on any focus loss including clicks inside the panel. Even with `mousedown` preventDefault, race conditions exist (e.g. Tab key, programmatic blur). `change` fires only when value commits (Enter or focus-loss with mutation), no setTimeout dance, no `activeElement` checks.

---

## Scope decisions

### Q14. Why upgrade `Event.Date`/`EndDate` to `DateTime` but leave `Company.AsOf` and `TradeBloc.FoundedDate` as `DateOnly`?

Lab requires "datum+vrijeme" — but only Events have a real time-of-day semantic (an earnings call at 14:30, a sanction announcement at 09:00). `AsOf` is a snapshot date; `FoundedDate` is calendar-day granularity. Upgrading those would invent precision the data doesn't carry. Picker `ShowTime` flag toggles the time UI per call site.

### Q15. What happens to existing event rows in the DB after the `date → datetime(6)` migration?

MySQL `ALTER COLUMN` cast preserves the day, sets time to `00:00:00.000000`. No row deleted. EF down-migration cast back to `date` truncating any non-zero time — reversible for fresh-cut date rows, lossy for any post-migration data entered with real times. Acceptable trade-off; one-way migration in practice.

### Q16. Why isn't EndDate marked `[Required]` when Date is?

Business: events with no defined end are valid ("Brexit announcement" — start date, no end). Database: nullable column (`datetime(6) NULL`). ViewModel: `DateTime? EndDate` no `[Required]`. Picker: `Required = false` default. Client validator `dategte` short-circuits `return true` on empty value. Server attribute returns `ValidationResult.Success` when value null. All four layers agree.

### Q17. Why no `[Required]` on the picker's hidden input even when `Required = true` is passed?

It is — see `_DateTimePicker.cshtml`:

```csharp
if (Model.Required) {
    attrs.Append($"data-val-required=\"{Model.ErrorMessage}\" ");
}
```

The attribute is emitted only when `Required = true`. Server enforces via `[Required]` on the ViewModel property. Both layers required for defense-in-depth; client-side alone is bypassable.
