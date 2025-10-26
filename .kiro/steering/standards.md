---
inclusion: always
---

# Coding Standards

Modern, type-safe, object-oriented code is non-negotiable. Follow these standards strictly.

## C# Standards (.NET 8.0)

### Nullable Reference Types
- Nullable reference types are **enabled globally** (`<Nullable>enable</Nullable>`)
- Use `string?` when null is valid, `string` when it must not be null
- Nullable warnings are enforced as **warnings** (CS8600, CS8602, CS8603, CS8604)
  - CS8600: Converting null literal or possible null value to non-nullable type
  - CS8602: Dereference of a possibly null reference
  - CS8603: Possible null reference return
  - CS8604: Possible null reference argument
- Use null-conditional (`?.`) and null-coalescing (`??`) operators by default
- Guard public APIs with explicit null checks (`ArgumentNullException.ThrowIfNull()`)
- **Rationale**: Library code should be defensive with nullable annotations to prevent runtime null reference exceptions

### Modern Language Features (C# 12 / .NET 8)
- **Collection expressions**: Use `[...]` syntax for arrays and collections
  ```csharp
  Point3d[] points = [point1, point2, point3];
  ```
- **Pattern matching**: Use `is` pattern instead of `as` + null check
  ```csharp
  if (obj is MyType { Value: > 0 } t) { ... }
  if (goo.ScriptVariable() is not GeometryBase geom) { return; }
  ```
- **Records**: Use `record` or `record struct` for data-centric models
  ```csharp
  public record Point3d(double X, double Y, double Z);
  ```
- **Init-only properties**: Use `init` for set-once immutability
  ```csharp
  public class Config { public string Name { get; init; } = ""; }
  ```
- **File-scoped namespaces**: Reduce indentation (enforced as warning)
  ```csharp
  namespace Arsenal.Core;
  ```

### Async/Await
- Use `async` all the way up; never mix `.Result` or `.Wait()`
- Use `ConfigureAwait(false)` in library code
- Prefer `Task<T>` for general purpose; `ValueTask<T>` only when profiling shows benefit

### Strong Typing (Enforced)
- Use strongly-typed models and enums over primitives
- Leverage generics with constraints (`where T : notnull`)
- **Always use explicit types** (enforced as warning): Never use `var`
  - Enforced by: `csharp_style_var_for_built_in_types = false:warning`
  - Enforced by: `csharp_style_var_when_type_is_apparent = false:warning`
  - Enforced by: `csharp_style_var_elsewhere = false:warning`
  - **Rationale**: Explicit types improve code clarity and maintainability, especially in library code where API contracts must be clear
  ```csharp
  // ✅ Good - explicit types
  List<Point3d> list = new List<Point3d>();
  double tolerance = Tolerances.Abs();
  GeometryBase geom = GetGeometry();
  
  // ❌ Bad - triggers warning
  var list = new List<Point3d>();
  var tolerance = Tolerances.Abs();
  var geom = GetGeometry();
  ```

### Code Style (Enforced)
- **Always use braces** (enforced as warning): Even for single-line if/for/while statements
  - Enforced by: `csharp_prefer_braces = true:warning`
  - Enforced by: `dotnet_diagnostic.IDE0011.severity = warning`
  - **Rationale**: Prevents bugs from accidental scope issues and improves code consistency
  ```csharp
  // ✅ Good - braces always present
  if (condition)
  {
      DoSomething();
  }
  
  // ❌ Bad - triggers warning
  if (condition)
      DoSomething();
  ```

- **Collection expressions** (enforced as warning): Use `[...]` instead of `new[]` or `new Type[]`
  - Enforced by: `dotnet_style_prefer_collection_expression = true:warning`
  - **Rationale**: Modern C# 12 syntax that is more concise and consistent
  ```csharp
  // ✅ Good - collection expression
  Point3d[] points = [point1, point2, point3];
  
  // ❌ Bad - triggers warning
  Point3d[] points = new[] { point1, point2 };
  Point3d[] points = new Point3d[] { point1, point2 };
  ```

- **Pattern matching** (enforced as warning): Use `is` pattern instead of `as` + null check
  - Enforced by: `csharp_style_pattern_matching_over_as_with_null_check = true:warning`
  - Enforced by: `csharp_style_pattern_matching_over_is_with_cast_check = true:warning`
  - **Rationale**: More concise, type-safe, and eliminates redundant null checks
  ```csharp
  // ✅ Good - pattern matching
  if (goo.ScriptVariable() is not GeometryBase geom)
  {
      return Result.Fail("Cannot convert");
  }
  
  if (obj is MyType { Value: > 0 } typed)
  {
      ProcessTyped(typed);
  }
  
  // ❌ Bad - triggers warning
  GeometryBase geom = goo.ScriptVariable() as GeometryBase;
  if (geom == null)
  {
      return Result.Fail("Cannot convert");
  }
  ```

### Object-Oriented Design & SOLID Principles (Enforced)
- **Single Responsibility**: One class, one purpose
- **Open/Closed**: Open for extension, closed for modification
- **Liskov Substitution**: Derived classes must be substitutable for base classes
- **Interface Segregation**: Many specific interfaces over one general interface
  - Enforced by: `dotnet_diagnostic.CA1040.severity = warning` (Avoid empty interfaces)
  - **Rationale**: Empty interfaces provide no contract and violate interface segregation principle
- **Dependency Inversion**: Depend on abstractions, not concretions
- **Encapsulation**: Hide implementation details; expose minimal API surface
- **Collections**: Implement generic interfaces
  - Enforced by: `dotnet_diagnostic.CA1010.severity = warning` (Collections should implement generic interface)
  - **Rationale**: Generic interfaces provide type safety and better API usability
- **Namespaces**: All types must be in namespaces
  - Enforced by: `dotnet_diagnostic.CA1050.severity = error` (Declare types in namespaces)
  - **Rationale**: Required for proper code organization and avoiding naming conflicts
- **Immutability**: Default to `readonly`, `init`, and `record` types
- **Separation of concerns**: Layer architecture (domain, application, infrastructure)

### Error Handling
- Use `Result<T>` pattern from `Arsenal.Core` for operations that can fail
- Throw exceptions only for exceptional conditions
- Never swallow exceptions without logging or handling

## Python Standards (3.9+)

### Type Annotations
- **Fully annotate** all function signatures and class attributes
  ```python
  def process_data(items: list[str]) -> dict[str, int]:
      ...
  ```
- Use abstract types in parameters (`Iterable`, `Mapping`), concrete in returns
- Use `object` instead of `Any` to maintain type safety
- Avoid union return types; refactor to clearer models
- Use `TypedDict` or `Literal` for structured/enumerated types

### Static Type Checking
- Configure mypy/basedpyright in **strict mode**
- Enable `--disallow-untyped-defs` (all functions must be typed)
- Avoid `# type: ignore` except for documented, justified cases
- Run type checkers in CI/CD as build gates

### Data Modeling
- **Dataclasses**: Use for internal, trusted data structures
  ```python
  @dataclass
  class Point3d:
      x: float
      y: float
      z: float
  ```
- **Pydantic**: Use for external APIs or runtime validation needs
- Never use mutable default arguments

### Protocol-Oriented Design
- Use `Protocol` for interface contracts (duck typing)
  ```python
  from typing import Protocol
  
  class Drawable(Protocol):
      def draw(self) -> None: ...
  ```
- Prefer protocols over inheritance hierarchies
- Supports better mocking and decoupling

### Async Patterns
- Use `async def` and `await` for I/O-bound operations
- Annotate async functions with proper return types
- Never mix blocking I/O with async code

### Anti-Patterns to Avoid
- ❌ Using `Any` (disables type checking)
- ❌ Ignoring type checker warnings
- ❌ Mutable default arguments
- ❌ Complex union return types
- ❌ Missing annotations on public APIs

## Universal Principles

### Code Quality
- **Readability over cleverness**: Code is read more than written
- **Explicit over implicit**: Make intent clear
- **Fail fast**: Validate early, fail with clear messages
- **DRY**: Don't repeat yourself; extract to shared libraries

### Testing
- Write tests that validate behavior, not implementation
- Use dependency injection for testability
- Mock external dependencies, not internal logic

### Documentation
- Use XML docs (C#) or docstrings (Python) for public APIs
- Keep it concise: one-line summaries for simple members, brief explanations for complex ones
- Document why, not what (code should be self-documenting)
- Avoid redundant parameter/return descriptions that just restate the obvious
- Skip example code blocks unless the usage is genuinely non-obvious
- Keep comments up-to-date or remove them

### Performance
- Measure before optimizing
- Prefer clarity over premature optimization
- Use profiling tools to identify actual bottlenecks
