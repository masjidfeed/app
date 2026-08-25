---
description: "Use when writing or editing C# code in the Masjid App (.NET MAUI). Covers naming, nullability, async/await, MVVM, dependency injection, and resilient JSON parsing conventions."
applyTo: "**/*.cs"
---
# C# / .NET MAUI Conventions

Project-wide architecture and safety rules live in [copilot-instructions.md](../copilot-instructions.md). This file covers C# coding style only.

## Language & Style

- Enable **nullable reference types** (`<Nullable>enable</Nullable>`) and `<ImplicitUsings>enable</ImplicitUsings>`; never disable nullability to silence a warning.
- Treat analyzer/formatter output as errors in CI (`QA-01`). Match the `.editorconfig`; do not hand-format against it.
- Naming: `PascalCase` for types/methods/properties, `camelCase` for locals/parameters, `_camelCase` for private fields, `IPascalCase` for interfaces.
- Prefer `var` when the type is apparent; use explicit types when it aids clarity.
- Use file-scoped namespaces, expression-bodied members where they improve readability, and target-typed `new()`.
- Prefer immutable shapes: `record` / `record struct` for DTOs and value objects, `readonly` fields, `init` setters.

## Async

- Suffix async methods with `Async` and return `Task`/`Task<T>` (or `ValueTask`). Avoid `async void` except for event handlers.
- Always flow a `CancellationToken` through API/service calls; honor it.
- Use `ConfigureAwait(false)` in the platform-agnostic core library; omit it in UI-bound MAUI code.
- Never block on async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`).

## MVVM & DI

- Use `CommunityToolkit.Mvvm`: `[ObservableProperty]` and `[RelayCommand]` instead of hand-written `INotifyPropertyChanged` and `ICommand`.
- ViewModels receive dependencies via **constructor injection**; register them and services in `MauiProgram`. No service locator, no `new`-ing up services inside ViewModels.
- Keep View code-behind thin - no business logic. Bind to ViewModel commands/properties.
- Services that touch I/O (API client, cache, storage) are defined as interfaces in the core library and injected.

## JSON & Resilience

- Use `System.Text.Json` with source-generated `JsonSerializerContext` for DTOs (trimming/AOT-friendly, faster).
- Parse tolerantly: ignore unknown/optional fields, tolerate missing values, never throw on malformed/partial payloads - degrade to cached or empty state (`API-07`, `API-09`, `NFR-RL-01`).
- Wrap external calls in typed results or caught exceptions; surface a non-blocking retry state rather than crashing (`API-09`).

## Domain Logic

- Time math uses `TimeZoneInfo`/`NodaTime` with the masjid's IANA timezone from the SalahAPI document, never the device timezone (`FR-PT-06`).
- Keep SalahAPI parsing, prayer/iqama calculation, timezone/DST handling, and RRULE expansion in the platform-agnostic core library so they stay unit-testable (`QA-02`, `QA-06`).

## Tests

- Use **xUnit** with `FluentAssertions`. Name tests `Method_State_ExpectedBehavior`.
- Cover core domain logic; validate prayer-time output against known reference values per calculation method (`QA-06`).
- **Every code change must ship with passing unit tests and a warnings-as-errors build.** Resolve all failures and report the results (`QA-01`, `QA-02`).
