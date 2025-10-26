---
inclusion: always
---

# Project Structure

## Top-Level Organization

```
Parametric_Arsenal/
├── libs/                    # Shared C# libraries (foundation for all plugins)
├── grasshopper/            # Grasshopper plugins (C#)
├── rhino/                  # Rhino plugins (C#)
├── .kiro/                  # Kiro IDE configuration
└── Parametric_Arsenal.sln  # Visual Studio solution
```

## Shared Libraries (`libs/`) - The Foundation

All plugins are built on top of these shared C# libraries. Libraries should also leverage each other:

- **core/**: Core utilities and types (e.g., `Result<T>` pattern, common interfaces)
  - Namespace: `Arsenal.Core`
  - No external dependencies beyond .NET
  
- **rhino/**: Rhino-specific operations (geometry, curves, tolerances, document utilities)
  - Namespace: `Arsenal.Rhino`
  - References: `Arsenal.Core`, RhinoCommon SDK
  - Builds upon `Arsenal.Core` patterns
  
- **grasshopper/**: Grasshopper component base classes and utilities
  - Namespace: `Arsenal.Grasshopper`
  - References: `Arsenal.Core`, `Arsenal.Rhino`, Grasshopper SDK
  - Builds upon both `Arsenal.Core` and `Arsenal.Rhino`

All libraries target .NET 8.0 with nullable reference types enabled.

## Grasshopper (`grasshopper/`)

```
grasshopper/
├── definitions/        # .gh definition files
└── plugins/           # C# Grasshopper plugins
    └── HopperHelper/  # Example plugin
        ├── components/     # Component implementations
        └── HopperHelper.csproj
```

### Grasshopper Plugin Conventions
- Language: C# (.NET 8.0)
- Output: `.gha` files (Grasshopper Assembly)
- Must reference: `Arsenal.Core`, `Arsenal.Rhino`, `Arsenal.Grasshopper`
- Leverage Grasshopper SDK for all component functionality
- Auto-deploy to Grasshopper Libraries after build

## Rhino (`rhino/`)

```
rhino/
├── plugins/           # C# Rhino plugins
│   └── SomePlugin/   # Example plugin structure
│       ├── commands/      # Command implementations
│       └── SomePlugin.csproj
└── scripts/          # Standalone Rhino scripts
```

### Rhino Plugin Conventions
- Language: C# (.NET 8.0)
- Output: `.rhp` files (Rhino Plugin)
- Must reference: `Arsenal.Core`, `Arsenal.Rhino`
- Leverage RhinoCommon SDK for all Rhino functionality
- Commands should be thin wrappers around library functionality

## Architecture Patterns

### Library Design Principles
1. **Layered Dependencies**: `Core` → `Rhino` → `Grasshopper`
2. **SDK First**: Always use RhinoCommon/Grasshopper SDK methods before creating custom implementations
3. **Shared Patterns**: Use `Result<T>` for operations that can fail
4. **Minimal Base Classes**: Provide common functionality through inheritance (e.g., `GhComponentBase`)

### Plugin Design Principles
1. **Leverage Libraries**: All plugins must reference and use the shared libraries
2. **Thin Plugin Layer**: Plugins orchestrate; libraries implement
3. **SDK Integration**: Use SDK types and methods directly; don't wrap unnecessarily
4. **Consistent Error Handling**: Use patterns from `Arsenal.Core`

### C# Code Patterns
- Use `Result<T>` pattern for operations that can fail
- Base classes provide common functionality (e.g., `GhComponentBase`)
- Sealed `SolveInstance` with abstract `GuardedSolve` for uniform error handling
- Document tolerance access via `DocTolerance` property
- Nullable reference types enabled throughout

## Development Workflow

When adding new functionality:
1. Check if SDK provides the capability (use MCP servers to research)
2. Check if `libs/` already provides the functionality
3. If needed, add to appropriate library in `libs/`
4. Plugin code should leverage the library functionality

Never duplicate code between plugins - extract to `libs/` instead.
