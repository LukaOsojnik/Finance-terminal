# LINQ, Lambda Expressions & Functional Objects

Use this skill when writing LINQ queries, lambda expressions, or working with `Func<>`, `Predicate<>`, `Action<>` objects in C#.

## Lambda Expressions

Anonymous/inline functions. Core syntax:

```csharp
// Expression lambda — single expression, implicit return
p => p.Id < 3

// Statement lambda — multiple statements, explicit return
p =>
{
    if (p.Id < 3)
        return true;
    return false;
}
```

Left of `=>`: input parameter(s). Right of `=>`: expression or block. The type of `p` is inferred from the collection.

## LINQ Operations

Each LINQ call transforms a collection into a new collection (or value). They chain together.

### Where — filter

```csharp
var result = listaKvizova.Where(p => p.Id < 3);
```

Returns only elements where the lambda returns `true`.

### ToList — materialize

```csharp
var list = kolekcija.Where(p => p.Active).ToList();
```

Forces evaluation into a new `List<T>`.

### Element extraction

| Method | Empty collection | Multiple elements |
|---|---|---|
| `First()` | Throws exception | Returns first |
| `FirstOrDefault()` | Returns default (`null`/`0`) | Returns first |
| `Single()` | Throws exception | Throws exception |
| `SingleOrDefault()` | Returns default | Throws exception |

All four accept an optional lambda filter: `.First(p => p.Id == 5)`

### OrderBy / OrderByDescending — sorting

```csharp
var sorted = lista.OrderBy(p => p.Name);
var desc = lista.OrderByDescending(p => p.CreatedDate);
```

### Count

```csharp
int total = lista.Count();
int filtered = lista.Count(p => p.Active);
```

### Subqueries (podupiti)

Nest LINQ inside LINQ to filter by child collections:

```csharp
var result = roditelji
    .Where(r => r.Djeca.Any(d => d.Dob > 18));
```

### Chaining — combine freely

```csharp
var result = lista
    .Where(p => p.Active)
    .OrderBy(p => p.Name)
    .Take(10)
    .ToList();
```

## Func, Predicate, Action

Store functions in variables — C#'s equivalent of function pointers.

| Object | Signature | Use |
|---|---|---|
| `Func<int, int, int>` | Two `int` params → returns `int` | General-purpose. Last type param is always the return type. |
| `Predicate<T>` | One `T` param → returns `bool` | Filtering/conditions |
| `Action<T>` | One `T` param → returns `void` | Side effects, no return |

### Example — returning a function

```csharp
public class Calculator
{
    public Func<int, int, int> GetRandomOperation()
    {
        var rand = new Random();
        switch (rand.Next() % 3)
        {
            case 0: return (a, b) => a + b;
            case 1: return (a, b) => a - b;
            case 2: return (a, b) => a * b;
        }
        return null;
    }
}

// Usage
Func<int, int, int> fOp = calc.GetRandomOperation();
int result = fOp(10, 5); // call like a function
```

### Example — passing a function as parameter

```csharp
static void PrintOperation(int x, int y, Func<int, int, int> op)
{
    int rez = op(x, y);
    Console.WriteLine(rez);
}

PrintOperation(10, 5, (a, b) => a + b);
```

LINQ methods internally use these types — `Where` takes a `Func<T, bool>`, `OrderBy` takes a `Func<T, TKey>`, etc.