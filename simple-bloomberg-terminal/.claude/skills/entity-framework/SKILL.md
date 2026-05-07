---
name: entity-framework
description: Guides adding, editing, or deleting EF Core entities, navigation properties, DbContext registration, and generating migrations. Use when: adding a new entity class, changing a model property, updating relationships, or running `dotnet ef migrations add`.
---

## Step 1 — Read the semantic model first

Before touching any entity, DbContext, or migration, read `semantic-model.md` in the project root. Understand the existing tables, columns, primary keys, foreign keys, and relationships. Do not proceed until you have a clear picture of what already exists.

## Step 2 — Follow existing entity conventions

Match the conventions already present in the codebase exactly:

- Use `[Key]` on the primary key property.
- Use `[ForeignKey("ForeignKeyPropertyName")]` on navigation properties that have an explicit FK column.
- Use `[InverseProperty("NavigationPropertyName")]` when there are multiple relationships between the same two entities.
- Mark all navigation properties as `virtual`.
- Initialize `ICollection<T>` navigation properties to `[]` (not `new List<T>()`).

## Step 3 — If adding a new entity

1. Create the entity class under the appropriate `Models/` subfolder.
2. Open `AppDbContext.cs` and add a `DbSet<T>` property for the new entity.
3. If the relationship cannot be inferred by convention, register it in `OnModelCreating` using the Fluent API — keep it consistent with any existing `OnModelCreating` configuration already in the file.

## Step 4 — Run migrations

After all model and DbContext changes are in place, run these two commands in order:

```
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

Choose a descriptive `<MigrationName>` that reflects the change (e.g., `AddCompanyEntity`, `AddEventStartDateColumn`).

## Step 5 — Update the semantic model

After the migration is applied, spawn a background agent with the instruction: "Update `semantic-model.md` to reflect the current state of the database schema, including any new or modified tables, columns, and relationships."

## Step 6 — Seeding (if needed)

If the new or changed entity requires seed data, open `Data/DbSeeder.cs` and add or update the relevant seeding logic there. Follow the existing seeding style already present in that file.
