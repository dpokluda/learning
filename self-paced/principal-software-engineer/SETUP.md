# Setup

Every hands-on exercise in this book is designed to run locally on your machine with **zero cloud accounts, zero external databases, zero container daemons, and zero network access at drill time**.
All distributed-systems primitives, storage engines, hashing rings, probabilistic sketches, rate limiters, and graph/spatial algorithms are modeled as deterministic, in-memory C# components verified by state-based unit tests.

Budget about five minutes to verify your .NET SDK and warm the local NuGet cache once.

## 1. Install or verify the .NET SDK

The runnable exercise suite targets **.NET 10.0 (`net10.0`)** with C# `latest` and nullable reference types enabled.
Verify your installed SDK in PowerShell or bash:

```powershell
dotnet --version
```

You should see `10.0.100` or newer (this book was verified against `10.0.404` on Windows).
If you do not have .NET 10 installed:
- **Windows**: Download the .NET 10 SDK installer from [dot.net](https://dotnet.microsoft.com/download) or run `winget install Microsoft.DotNet.SDK.10`.
- **macOS**: Run `brew install --cask dotnet-sdk` or use the official pkg installer.
- **Linux**: Follow the Microsoft package repository instructions for your distribution or use the `dotnet-install.sh` script.

If you only have .NET 8 or .NET 9 installed and cannot install .NET 10 right now, you can change `<TargetFramework>net10.0</TargetFramework>` to `net8.0` or `net9.0` in `exercises/PrincipalEngineerDrills/PrincipalEngineerDrills.csproj`; the code uses standard BCL types and compiles cleanly on all three.

## 2. Warm the test runner cache once (online)

The exercise project uses **MSTest** (`MSTest` `4.0.2` and `Microsoft.NET.Test.Sdk` `18.8.1`), which is the standard unit-testing framework used across the sibling C# study books in this repository.
Because MSTest is distributed as a NuGet package, you need to run `dotnet test` **once while connected to the network** so NuGet caches those two packages under `~/.nuget/packages` (or `%USERPROFILE%\.nuget\packages` on Windows):

```powershell
cd E:\projects\learning\self-paced\principal-software-engineer\exercises\PrincipalEngineerDrills
dotnet test
```

Once that initial restore completes, every subsequent build and test run works completely offline:

```powershell
dotnet test --no-restore
```

## 3. Project layout and C# conventions

The runnable verification project lives in [`exercises/PrincipalEngineerDrills/`](./exercises/PrincipalEngineerDrills/) and follows the engineering conventions in `OPINIONS.md`:
- **File-scoped namespaces** (`namespace PrincipalEngineerDrills;`) and explicit block braces on all control-flow statements (`if`, `else`, `for`, ` foreach`, `while`).
- **Classical, state-based unit tests**: No mocking frameworks (`Moq`, `NSubstitute`, etc.) are used anywhere. Where time, network transport, or storage faults are needed to prove an invariant (such as lease expiration, token-bucket refill, or zombie-leader fencing), the tests use small, deterministic in-memory fakes (`FakeClock`, `SimulatedStorageServer`, etc.).
- **Self-contained chapter files**: Each chapter's test suite (`Chapter01.cs` through `Chapter29.cs`) lives in the same project and uses C#'s `file` access modifier on helper types so that domain concepts like `Node`, `LogEntry`, or `ConsistentHashRing` never collide across chapters.
- **Parallel test execution**: `MSTestSettings.cs` enables `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`, so the entire suite executes in well under a second.

To run the tests for a single module while working through [`exercises/EXERCISE-COMPANION.md`](./exercises/EXERCISE-COMPANION.md), filter by class name:

```powershell
dotnet test --no-restore --filter ClassName~Chapter04
```

## 4. Pinned versions

| Component | Version verified |
| --- | --- |
| .NET SDK | 10.0.404 (`net10.0`) |
| `Microsoft.NET.Test.Sdk` | 18.8.1 |
| `MSTest` | 4.0.2 |
| External runtime dependencies | None (BCL only) |

---

Once `dotnet test` succeeds in `exercises/PrincipalEngineerDrills`, proceed to [`00-START-HERE.md`](./00-START-HERE.md) or jump straight into [Module 01](./01-distributed-systems-models-and-failure-modes.md).
