# C# Class Design Fundamentals

Use this skill when working with C# class structure: properties, constructors, methods, exception handling, interfaces, or generics/collections.

## Properties (Svojstva)

Backing data lives in private members. Expose via properties, not public fields.

```csharp
// Auto-property — preferred default
public int SvojstvoX { get; set; }

// Full property with backing field — when validation/logic is needed
private int _svojstvoX;
public int SvojstvoX
{
    get => _svojstvoX;
    set
    {
        if (value < 0) throw new ArgumentException("Must be >= 0");
        _svojstvoX = value;
    }
}
```

Access levels: `public` or `protected` are standard. `private` properties are rare.

## Constructors

Default constructor is auto-generated if none is defined. Add an explicit constructor when complex members need initialization to avoid `NullReferenceException`:

```csharp
public class A : B
{
    public List<C> ObjektiKlaseC { get; set; }

    public A()
    {
        ObjektiKlaseC = new List<C>();
    }
}
```

Without the constructor, callers must remember to initialize the list themselves — a common bug source.

## Methods

Defined by: return type, name (CamelCase), parameters (type + count).

```csharp
public int PrebrojiKolikoJeUListi()
{
    int rezultat = 0;
    foreach (var x in ObjektiKlaseC)
        rezultat++;
    return rezultat;
}
```

Access: `private`, `protected`, `public`. Can be instance or `static`.

## Exceptions (Iznimke)

All throwable/catchable exceptions inherit from `Exception`. The .NET framework provides many built-in exceptions; custom exceptions are just classes inheriting from `Exception` or any derived class.

```csharp
// Throwing
throw new InvalidOperationException("Something went wrong");

// Catching
try
{
    // risky code
}
catch (SpecificException ex)
{
    // handle
}
finally
{
    // always runs — cleanup
}
```

## Interfaces (Sučelja)

An interface defines a contract — what a class must implement. Convention: prefix with `I`.

```csharp
public interface IDisposable
{
    void Dispose();
}
```

A class implementing `IDisposable` signals it holds resources that must be released after use.

## Collections (Kolekcije / Generics)

Prefer strongly-typed generic collections over untyped ones.

| Type | Description | Access |
|---|---|---|
| `List<int>` | Only `int` values allowed | `mojaIntLista[2]` |
| `List<ContactData>` | Typed object list | `lista[0].Name` |
| `Dictionary<int, string>` | Key-value pairs | `mojDict[2] = "Dva"` |
| `List` (untyped) | Stores `object` — avoid | `(Type)mojaLista[5]` |

`List<T>` is the most commonly used collection. Iterate with `foreach`.