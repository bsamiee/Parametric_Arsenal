---
inclusion: always
---

# Coding Standards

Professional, polymorphic, composition-based C# code with functional programming integration is mandatory. These standards are non-negotiable.

## 0. File-Scoped Namespaces (MANDATORY)

**ALL C# files MUST use file-scoped namespace declarations:**
```csharp
namespace Arsenal.Core.Result;

// File content - NO braces around namespace
```

**NEVER use traditional namespace blocks:**
```csharp
// ❌ FORBIDDEN
namespace Arsenal.Core.Result
{
    // content
}
```

## I. Fundamental Design Principles (Critical)

### Polymorphism as Foundation
- **All major functionality MUST be exposed through interfaces**
- **Program to contracts, never to concrete implementations**
- **Swap implementations behind common interfaces at compile or runtime**
- **Use inheritance only for stable taxonomies where substitutability is guaranteed**

### Composition Over Inheritance (Mandatory)
- **Default to composition ("has-a", "uses-a") over inheritance ("is-a")**
- **Inheritance is fragile - hierarchy changes break dependent code**
- **Composition is stable, testable, and encourages interface design**
- **Mark all leaf classes `sealed` unless specifically designed for inheritance**

### SOLID Principles (Strictly Enforced)
- **S**: One class, one responsibility, one reason to change
- **O**: Open for extension via interfaces, closed for modification
- **L**: Subtypes must be substitutable for base types without breaking contracts
- **I**: Many small, focused interfaces over large, general ones
- **D**: Depend on abstractions (interfaces), never on concretions

### Functional Programming Integration
- **Pure functions for all business logic and calculations**
- **Immutable data structures using `record` and `readonly`**
- **Higher-order functions with `Func<T>` and `Action<T>`**
- **Pattern matching over conditional logic**
- **TryXxx pattern instead of exceptions for control flow**

## II. Documentation Standards (Mandatory)

### XML Documentation (Required for All Public APIs)
- **All public types, members, and methods MUST have XML documentation**
- **Summaries MUST be exactly one line** - concise, accurate, factual, and relevant
- **NEVER use `<param>`, `<returns>`, or other XML tags** - ONLY `<summary>` allowed
- **No redundant parameter descriptions** - method signatures are self-documenting
- **Document intent and behavior, not implementation details**
- **Keep documentation current** - outdated docs are worse than no docs

### Code Comments Policy (Anti-Spam)
- **Strong policy against comment litter** - only add comments when truly justified
- **Comments explain WHY, not WHAT** - code should be self-documenting through clear naming
- **Remove obvious comments** - `// Increment counter` adds no value
- **Justify complex algorithms** - when business logic requires explanation
- **No TODO comments in production code** - use issue tracking instead

### Documentation Examples
```csharp
/// <summary>Calculates area using width and height dimensions.</summary>
public double Area() => Width * Height;

/// <summary>Processes payment using the configured tax policy.</summary>
public decimal CalculateTotal(IReadOnlyList<ProductLine> lines, decimal taxRate)
{
    // Complex tax calculation requires policy delegation
    decimal subtotal = OrderCalculations.Subtotal(lines);
    return subtotal + _taxPolicy.ComputeTax(subtotal, taxRate);
}
```

## III. C# Type System and Language Requirements

### Type Selection Guidelines
- **`interface`**: Primary design tool for contracts and polymorphism
- **`class`**: Reference types with identity and behavior, mark `sealed` by default
- **`record`/`record struct`**: Value-centric types with structural equality
- **`struct`**: Small, immutable value types with low copy cost
- **`delegate`**: Function types for higher-order functions
- **`enum`**: Closed sets of named constants

### Nullable Reference Types (Mandatory)
- **Enabled globally** with `<Nullable>enable</Nullable>`
- **Use `string?` for nullable, `string` for non-nullable**
- **Guard public APIs** with `ArgumentNullException.ThrowIfNull()`
- **Use null-conditional (`?.`) and null-coalescing (`??`) operators**
- **Nullable warnings enforced as build warnings**

### Modern C# Features (Required)
- **File-scoped namespaces**: `namespace Arsenal.Core;`
- **Collection expressions**: `[item1, item2, item3]`
- **Pattern matching**: `is` patterns over `as` + null checks
- **Records**: For immutable data and value equality
- **Init-only properties**: `{ get; init; }` for set-once immutability

### Type Safety (Enforced)
- **Never use `var`** - explicit types required for clarity
- **Strongly-typed models over primitives**
- **Generic constraints**: `where T : notnull`
- **Always use braces** for control structures
- **Enforced by compiler warnings and analyzers**

## IV. Implementation Guidelines

### Interface Design (Critical)
- **Define interfaces first, implementations second**
- **Keep interfaces small and focused (Interface Segregation)**
- **Separate read/write concerns**: `IReadable<T>` vs `IWritable<T>`
- **No empty interfaces** - must provide meaningful contracts
- **Use generic interfaces for collections**

### Class Implementation Rules
- **All classes `sealed` by default** unless designed for inheritance
- **Constructor injection for dependencies**
- **Validate invariants in constructors**
- **Minimal public surface area**
- **Immutable by default** - use `readonly`, `init`, `record`

### Composition Patterns
- **Dependency injection through constructor parameters**
- **Prefer interfaces over concrete types in dependencies**
- **Wire concrete implementations at composition root**
- **Use strategy pattern for varying behavior**
- **Avoid deep object hierarchies**

## V. Functional Programming Integration

### Pure Functions (Mandatory for Business Logic)
- **All business calculations must be pure functions**
- **No side effects - same input always produces same output**
- **Static methods in static classes for stateless operations**
- **Take immutable parameters, return immutable results**

### Immutable Data Structures
- **Use `record` and `record struct` for data transfer objects**
- **Use `with` expressions for non-destructive updates**
- **Prefer `readonly` fields and `init` properties**
- **Collections should be `IReadOnlyList<T>`, `IReadOnlyDictionary<K,V>`**

### Higher-Order Functions
- **Use `Func<T>` and `Action<T>` delegates**
- **LINQ for collection transformations (map, filter, reduce)**
- **Avoid mutable state in lambda expressions**
- **Compose functions rather than inherit behavior**

### Pattern Matching
- **Use `switch` expressions over if-else chains**
- **Exhaustive matching with sealed type hierarchies**
- **Property patterns for object deconstruction**
- **Guard clauses with `when` conditions**

## VI. Error Handling Strategy

### Result Pattern (Primary)
- **Use `Result<T>` from `Arsenal.Core` for operations that can fail**
- **Never throw exceptions for expected failure scenarios**
- **Chain operations with `Map`, `Bind`, `Match` methods**

### TryXxx Pattern (Secondary)
- **Use for simple success/failure scenarios**
- **Return `bool` success flag with `out` parameter for result**
- **Prefer over exceptions for control flow**

### Exceptions (Exceptional Only)
- **Only for truly exceptional conditions**
- **Never catch and ignore without logging**
- **Use specific exception types, not generic `Exception`**

## VII. Async Programming

### Async/Await Rules
- **Use `async` all the way up the call stack**
- **Never use `.Result` or `.Wait()` - causes deadlocks**
- **Use `ConfigureAwait(false)` in library code**
- **Prefer `Task<T>` over `ValueTask<T>` unless profiling shows benefit**

## VIII. Quality Standards

### Code Quality (Non-Negotiable)
- **Readability over cleverness** - code is read more than written
- **Explicit over implicit** - make intent clear through types and names
- **Fail fast** - validate early, provide clear error messages
- **DRY principle** - extract common functionality to shared libraries

### Testing Standards
- **Test behavior, not implementation details**
- **Use dependency injection for testability**
- **Mock external dependencies, never internal logic**
- **Arrange-Act-Assert pattern for test structure**

### Performance Guidelines
- **Measure before optimizing**
- **Prefer clarity over premature optimization**
- **Use profiling tools to identify actual bottlenecks**
- **Consider memory allocation patterns in hot paths**

## IX. Integration Example

Complete example demonstrating all principles working together:

```csharp
// 1. Interface-first design (Polymorphism)
public interface IShape { double Area(); }
public interface ITaxPolicy { decimal ComputeTax(decimal subtotal, decimal rate); }

// 2. Sealed implementations (Composition over inheritance)
public sealed class Rectangle : IShape
{
    public double Width { get; init; }
    public double Height { get; init; }
    public Rectangle(double width, double height) => (Width, Height) = (width, height);
    public double Area() => Width * Height;
}

// 3. Value objects with immutability (Functional)
public readonly record struct ProductLine(int Id, int Qty, decimal UnitPrice);

// 4. Composition with interface dependencies (SOLID)
public sealed class OrderProcessor
{
    private readonly ITaxPolicy _taxPolicy;
    public OrderProcessor(ITaxPolicy taxPolicy) => _taxPolicy = taxPolicy;
    
    public decimal CalculateTotal(IReadOnlyList<ProductLine> lines, decimal taxRate)
    {
        decimal subtotal = OrderCalculations.Subtotal(lines);
        decimal tax = _taxPolicy.ComputeTax(subtotal, taxRate);
        return subtotal + tax;
    }
}

// 5. Pure functions for business logic (Functional core)
public static class OrderCalculations
{
    public static decimal Subtotal(IEnumerable<ProductLine> lines) =>
        lines.Sum(x => x.Qty * x.UnitPrice);
}

// 6. Pattern matching for control flow (Modern C#)
public abstract record PaymentMethod;
public record Card(string Last4) : PaymentMethod;
public record Cash : PaymentMethod;

public static string FormatPayment(PaymentMethod method) => method switch
{
    Card c => $"Card ending in {c.Last4}",
    Cash => "Cash payment",
    _ => "Unknown payment method"
};
```

**This demonstrates:**
- **Polymorphism**: Interface contracts with multiple implementations
- **Composition**: Dependencies injected through constructor
- **SOLID**: Single responsibility, interface segregation, dependency inversion
- **Functional**: Pure functions, immutable data, pattern matching
- **Modern C#**: Records, pattern matching, init properties
