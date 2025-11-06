# GitHub Copilot Instructions for Parametric Arsenal

This file provides instructions for GitHub Copilot coding agent when working on this repository.

## Repository Overview

**Parametric Arsenal** is a .NET 8.0 project combining C# libraries with Python scripting for Rhino 8 and Grasshopper parametric design tools. The repository follows a monorepo structure with strict code quality standards enforced through analyzers and custom coding patterns.

## Project Structure

```
Parametric_Arsenal/
├── libs/                           # Core C# libraries
│   ├── core/                       # Core library with Result monad, validation, operations
│   ├── rhino/                      # Rhino-specific geometry and spatial operations
│   └── grasshopper/                # Grasshopper component library
├── rhino/                          # Rhino plugins (Python/C#)
│   └── plugins/                    # Rhino plugin implementations
├── test/                           # Test projects
│   ├── core/                       # xUnit + CsCheck tests for core (Arsenal.Core.Tests)
│   ├── rhino/                      # NUnit + Rhino.Testing tests (Arsenal.Rhino.Tests)
│   └── shared/                     # Shared test utilities (Arsenal.Tests.Shared)
├── grasshopper/                    # Grasshopper components
├── CLAUDE.md                       # Detailed C# coding standards (MANDATORY READ)
├── Directory.Build.props           # MSBuild configuration
└── pyproject.toml                  # Python tooling configuration
```

## Build and Test Commands

### C# / .NET
```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build libs/core/

# Run all tests
dotnet test

# Run specific test
dotnet test --filter "FullyQualifiedName~Result"

# Clean build artifacts
dotnet clean
```

### Python
```bash
# Format Python code
uv run ruff format .

# Lint Python code
uv run ruff check .

# Type check with mypy
uv run mypy .

# Type check with basedpyright
uv run basedpyright .
```

## Critical Coding Standards

### C# Code Requirements (MANDATORY)

**Read `CLAUDE.md` thoroughly** - it contains mandatory patterns and rules. Key requirements:

1. **Code Density**: Super dense algebraic code only. Study exemplars in:
   - `libs/core/validation/ValidationRules.cs`
   - `libs/core/results/ResultFactory.cs`
   - `libs/core/operations/UnifiedOperation.cs`

2. **Strict Rules**:
   - **NO if/else** - Use pattern matching and switch expressions only
   - **NO var** - Explicit types always
   - **NO helpers/extracting** - Improve logic instead (300 LOC hard limit)
   - **NO old patterns** - Use target-typed new, collection expressions, tuple deconstruction
   - **K&R brace style** - Opening braces on same line
   - **Always leverage libs/** - Never handroll what Result monad provides

3. **Result Monad (ALWAYS USE)**:
   ```csharp
   Result<T>                          // Lazy evaluation, monadic composition
   ResultFactory.Create(value: x)     // Never new Result
   .Map(x => transform)               // Functor transform
   .Bind(x => Result<Y>)              // Monadic chain
   .Apply(Result<Func>)               // Applicative parallel
   .Filter(predicate, error)          // Validation
   .OnError(recover: x => value)      // Recovery
   ```

4. **Pattern Matching for Polymorphism**:
   ```csharp
   UnifiedOperation.Apply(
       input,
       (Func<object, Result<IReadOnlyList<T>>>)(item => item switch {
           GeometryBase g => Strategies.Process(g, method, context),
           _ => ResultFactory.Create<IReadOnlyList<T>>(error: ValidationErrors.Geometry.Invalid),
       }),
       new OperationConfig<object, T> { Context = context, ValidationMode = ValidationMode.None })
   ```

5. **FrozenDictionary for Configuration**:
   ```csharp
   private static readonly FrozenDictionary<(SpatialMethod, Type), (ValidationMode Mode, Func<object, RTree?>? TreeFactory)> _config =
       new Dictionary<(SpatialMethod, Type), (ValidationMode, Func<object, RTree?>?)> {
           [(SpatialMethod.PointsRange, typeof(Point3d[]))] = (ValidationMode.Standard, s => RTree.CreateFromPointArray((Point3d[])s)),
       }.ToFrozenDictionary();
   ```

6. **Error Pattern**: Each folder has its own errors class:
   ```csharp
   // libs/core/validation/ValidationErrors.cs
   public static class ValidationErrors {
       public static class Geometry {
           public static readonly SystemError Invalid = new(ErrorDomain.Validation, 3001, "...");
       }
   }
   ```

### Python Code Requirements

1. **Type Annotations**: All functions must have complete type annotations
2. **Rhino Interop**: Use `Rhino.*` types correctly, understanding they're .NET assemblies
3. **Style**: Follow Ruff formatting and linting rules in `pyproject.toml`
4. **Imports**: Keep Rhino-specific imports separate from standard library

### Analyzers Enforced

These are automatically enforced during build:
- **MA0051**: Method length max 120 lines
- **IDE0301-0305**: Collection expressions required
- **IDE0290**: Primary constructors required
- **File-scoped namespaces**: Mandatory

## Key Techniques and Patterns

1. **ConditionalWeakTable** - For auto-memory managed caching
2. **ArrayPool<T>** - For zero-allocation buffers
3. **Expression.Compile()** - For runtime validator generation
4. **FrozenDictionary** - For compile-time lookups
5. **ValueTuple patterns** - For multi-value dispatch

## Testing Guidelines

### C# Tests
- **Core library**: Use xUnit + CsCheck for property-based testing
- **Rhino library**: Use NUnit + Rhino.Testing framework
- **Shared utilities**: Available in Arsenal.Tests.Shared

### Python Tests
- Tests should follow Rhino plugin testing patterns
- Use type stubs from `rhino-stubs` package

## Common Tasks

### Adding a New C# Class
1. Use file-scoped namespaces
2. Apply primary constructors where applicable
3. Use explicit types (no `var`)
4. Return `Result<T>` for operations that can fail
5. Use pattern matching instead of if/else
6. Keep methods under 120 lines
7. Add corresponding error definitions if needed

### Adding a New Rhino Plugin Command (Python)
1. Place in `rhino/plugins/[PluginName]/commands/`
2. Add complete type annotations
3. Use `Rhino.*` imports for geometry
4. Follow Ruff formatting standards
5. Add docstrings in Google style

### Modifying Validation Logic
- **Never handwrite validators** - Use ValidationRules expression tree compilation
- Add new rules through the ValidationRules DSL

## Artifact Locations

Build artifacts are output to:
- C# libraries: `/artifacts/[ProjectName]/debug/`
- Example: `/artifacts/Core/debug/Arsenal.Core.dll`
- Note: Configuration is lowercase `debug` (not `Debug`)

## Dependencies and Package Management

### C# (.NET)
- Managed via `.csproj` files
- NuGet packages restored automatically with `dotnet restore`

### Python
- Managed via `pyproject.toml` with UV
- Install with: `uv sync`
- Lock file: `uv.lock`

## Branch Strategy

- Main branch: `main`
- Feature branches: Use descriptive names
- Commits: Follow Conventional Commits (enforced)

## Important Notes

1. **Never modify working code unnecessarily** - Make surgical, minimal changes
2. **Consult CLAUDE.md** for all C# code - it contains the authoritative coding standards
3. **Run tests before committing** - Always validate changes don't break existing functionality
4. **Build artifacts go to /artifacts/** - Never commit build outputs
5. **Platform**: .NET 8.0, C# preview features, Rhino 8 SDK, Python 3.9+

## Security and Quality

- All changes must pass build and tests
- Security vulnerabilities in dependencies are tracked
- Code must compile without warnings
- Analyzer rules are non-negotiable

## Getting Help

1. Read `CLAUDE.md` for detailed C# patterns
2. Read `README.md` for project overview
3. Check `pyproject.toml` for Python configuration
4. Review `Directory.Build.props` for MSBuild settings
5. Examine existing code in the relevant library for patterns

## When in Doubt

- **For C# code**: Default to the patterns in `CLAUDE.md` and existing core library code
- **For Python code**: Follow Ruff rules and existing plugin patterns
- **For architecture**: Use Result monad, avoid exceptions for control flow
- **For testing**: Match the testing style of the target library (xUnit vs NUnit)
