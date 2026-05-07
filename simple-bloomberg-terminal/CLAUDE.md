# Context

I am a Java/Spring Boot developer learning C#.
My mental model is Java — use it as the bridge for architecture decisions, never for syntax.

## Core rule

Only show Java equivalents for **architecture and design patterns**, not syntax.
Syntax I can read from the code. Architecture is where the mental model matters.

When to bridge:
- DI wiring (Spring vs ASP.NET Core)
- Project structure, layering, conventions
- Middleware / filters / interceptors
- Async patterns, threading model
- Data access patterns (JDBC vs EF Core)

When to stay silent:
- `var` vs `var`, `new` vs `new`, loops, conditionals
- Access modifiers, type declarations
- Method signatures, lambda expressions
- Any single-line code construct

Format when bridging:
```
// Java (Spring)
...

// C# (ASP.NET)
...
— key difference: [one sentence]
```

## Style

- Read the existing code first. Match its conventions exactly.
- Continue in the same style already present — never introduce a new pattern.
- Direct, no hand-holding. Code over prose.

## Foundation questions

Before implementing anything non-trivial, ask me 1-3 questions about the foundations
I should understand for what we're building. Skip if the task is a simple bugfix or typo.

Examples:
- "You're about to wire up EF Core DbContext. Do you know how scoped lifetime works vs singleton in ASP.NET DI?"
- "This controller pattern relies on model binding. Have you seen how `[FromBody]` differs from Spring's `@RequestBody`?"
- "We're adding middleware. Familiar with how the ASP.NET pipeline differs from Spring's filter chain?"

## Agent spawning
- when i say envoke UX agent read /front-end-design-pattern skill and /frontend-design:frontend-design skill
- whenever an EF entity is created, edited, or deleted (Models/Entities/ or Data/AppDbContext.cs), spawn a background agent to update semantic-model.md with the appropriate changes. The agent should read the current semantic-model.md and the modified entity files, then edit the document to reflect the new state (tables, properties, relationships, enums).
- whenever routing is added, changed, or removed in any controller (Controllers/*.cs), spawn a background agent to update sitemap.md with the appropriate changes. The agent should read the current sitemap.md and the modified controller files, then edit the document to reflect the new URLs, controllers, actions, and views.

