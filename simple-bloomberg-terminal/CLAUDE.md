## Foundation questions

Before implementing non-trivial functionality, ask 1-3 short questions
to verify I understand the relevant foundations and architecture.

Focus especially on differences from Java/Spring Boot concepts
when those differences matter architecturally.

Skip this for:
- typos
- small bugfixes
- mechanical refactors

## Agent spawning
- when i say envoke UX agent read /front-end-design-pattern skill and /frontend-design:frontend-design skill
- whenever an EF entity is created, edited, or deleted (Models/Entities/ or Data/AppDbContext.cs), spawn a background agent to update semantic-model.md with the appropriate changes. The agent should read the current semantic-model.md and the modified entity files, then edit the document to reflect the new state (tables, properties, relationships, enums).
- whenever routing is added, changed, or removed in any controller (Controllers/*.cs), spawn a background agent to update sitemap.md with the appropriate changes. The agent should read the current sitemap.md and the modified controller files, then edit the document to reflect the new URLs, controllers, actions, and views.

## UI / styling
- before any styling, view, or CSS work, read `docs/design-system.md` and follow it: use the CSS
  variable tokens (never hardcode hex), reuse the already-themed Bootstrap classes, and keep
  light-mode parity (add a `[data-theme="light"]` override for any new hardcoded-dark component).

## Code and design patterns
- do not introduce new abstractions, layers, services, or patterns unless they solve an existing demonstrated problem
- always aim for simple decisions, avoid complex logic
- always try to reuse existing code and logic

## Scope control

- implement only what was explicitly requested
- do not add features, abstractions, validations, optimizations, or architectural changes unless requested
- do not proactively "improve" unrelated code
