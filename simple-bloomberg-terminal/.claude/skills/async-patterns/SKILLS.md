# Async/Await & Task Patterns

Use this skill when working with async methods, `Task`, `Task<T>`, threading, or background execution in C#.

## Core Concepts

`async`/`await` enable non-blocking execution using background threads. Key benefits:
- Desktop/mobile: keeps UI thread responsive during long operations
- Server: frees threads to handle other requests while awaiting I/O (database calls, HTTP, file access)

## Task — manual background work

```csharp
Task t1 = Task.Run(() =>
{
    Console.WriteLine("Sleeping started");
    Thread.Sleep(1000);
    Console.WriteLine("Sleeping completed");
});

Console.WriteLine("Waiting on task..");
t1.Wait(); // blocks until t1 completes
```

Execution flow:
1. `Task.Run()` starts the lambda on a background thread immediately
2. Main thread continues to `Console.WriteLine`
3. `t1.Wait()` blocks the main thread until the task finishes

Wait alternatives:
- `t1.Wait()` — single task
- `Task.WaitAll(t1, t2, t3)` — wait for multiple tasks

## Async Methods

```csharp
private static async Task DoSomeSleepingAsync()
{
    Console.WriteLine("Sleeping started");
    await Task.Delay(1000);  // non-blocking sleep
    Console.WriteLine("Sleeping completed");
}

static void Main(string[] args)
{
    Task t1 = DoSomeSleepingAsync();
    Console.WriteLine("Waiting on task..");
    t1.Wait();
}
```

Execution flow:
1. Calling `DoSomeSleepingAsync()` starts the method and returns a `Task` handle
2. Code before the first `await` runs synchronously on the caller's thread
3. At `await Task.Delay(1000)`, a background thread takes over the delay — the caller continues
4. After the delay, execution resumes after `await` (possibly on a different thread)
5. `t1.Wait()` in Main blocks until everything completes

## Critical Rules

| Do | Don't |
|---|---|
| `await Task.Delay(ms)` | `Thread.Sleep(ms)` in async methods |
| Return `Task` or `Task<T>` | Return `void` (except event handlers) |
| Suffix with `Async` | Name without suffix (convention) |
| `await` async calls | `.Result` or `.Wait()` inside async methods (deadlock risk) |

## Return Types

```csharp
// No return value
async Task DoWorkAsync() { ... }

// Returns a value
async Task<int> CalculateAsync()
{
    await Task.Delay(100);
    return 42; // async keyword handles wrapping into Task<int>
}

// Usage
int result = await CalculateAsync();
```

The `async` keyword means: the method can use `await`, and the compiler wraps the return value into a `Task<T>` automatically — no explicit `return new Task(...)` needed.