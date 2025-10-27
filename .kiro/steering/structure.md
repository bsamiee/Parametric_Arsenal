---
inclusion: always
---

# Project Structure

## Top-Level Organization

```
Parametric_Arsenal/
├── libs/                    # Shared C# libraries (polymorphic foundation for all plugins)
├── grasshopper/            # Grasshopper plugins (C#)
├── rhino/                  # Rhino plugins (C#)
├── .kiro/                  # Kiro IDE configuration
└── Parametric_Arsenal.sln  # Visual Studio solution
```

## Shared Libraries (`libs/`) - The Polymorphic Foundation

All plugins are built on top of these shared C# libraries using composition and interface-based design. Libraries leverage each other through well-defined contracts:

- **core/**: Core interfaces, patterns, and utilities
  - Namespace: `Arsenal.Core`
  - Contains: `Result<T>` pattern, core interfaces, functional utilities, composition helpers
  - Dependencies: .NET 8.0 only
  - **Design**: Pure interfaces and abstract contracts, minimal concrete implementations
  
- **rhino/**: Rhino-specific operations with polymorphic geometry handling
  - Namespace: `Arsenal.Rhino`
  - Contains: Geometry interfaces, tolerance management, document utilities
  - References: `Arsenal.Core`, RhinoCommon SDK
  - **Design**: Composes `Arsenal.Core` patterns with Rhino SDK capabilities
  
- **grasshopper/**: Grasshopper component infrastructure with composition-based architecture
  - Namespace: `Arsenal.Grasshopper`
  - Contains: Component base classes, data handling interfaces, solver patterns
  - References: `Arsenal.Core`, `Arsenal.Rhino`, Grasshopper SDK
  - **Design**: Builds upon both `Arsenal.Core` and `Arsenal.Rhino` through composition

All libraries target .NET 8.0 with nullable reference types enabled and strict adherence to composition over inheritance.

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

### Library Design Principles (Composition-Based)
1. **Layered Dependencies**: `Core` → `Rhino` → `Grasshopper` through interface contracts
2. **Interface-First Design**: All major functionality exposed through interfaces
3. **Composition Over Inheritance**: Libraries compose capabilities rather than inherit them
4. **SDK Integration**: Always use RhinoCommon/Grasshopper SDK methods, compose with our interfaces
5. **Functional Core**: Pure functions for calculations, immutable data structures where possible

### Plugin Design Principles (Polymorphic)
1. **Interface Dependencies**: All plugins depend on `Arsenal.*` interfaces, not concrete classes
2. **Thin Plugin Layer**: Plugins orchestrate through interfaces; libraries implement
3. **Composition Root**: Plugins wire up concrete implementations at startup
4. **Consistent Patterns**: Use `Result<T>`, `TryXxx`, and interface-based error handling

### C# Code Patterns (Professional Standards)
- **Interfaces + Composition**: Primary design pattern for all major functionality
- **Sealed Classes**: Mark leaf implementations `sealed` for performance and clarity
- **Pure Functions**: Business logic implemented as static pure functions where possible
- **Immutable Data**: Use `record` types for data transfer and value objects
- **Pattern Matching**: Use `switch` expressions and property patterns
- **Result<T> Pattern**: For operations that can fail without exceptions
- **TryXxx Pattern**: For expected control flow scenarios
- **Nullable Reference Types**: Enabled throughout with proper annotations

## Development Workflow (Interface-Driven)

When adding new functionality:
1. **Define Interface First**: Create interface contract in appropriate `libs/` layer
2. **Check SDK Capabilities**: Research RhinoCommon/Grasshopper SDK (use MCP servers)
3. **Implement Through Composition**: Create sealed implementation that composes SDK functionality
4. **Pure Functions for Logic**: Extract business rules into static pure functions
5. **Plugin Integration**: Plugins depend on interfaces, wire concrete implementations

### Composition Guidelines
- **Never duplicate code**: Extract to `libs/` interfaces and implementations
- **Prefer small interfaces**: Follow Interface Segregation Principle
- **Use dependency injection**: Constructor injection for interface dependencies
- **Mark classes sealed**: Unless specifically designed for inheritance
- **Validate at boundaries**: Use pure functions for core logic, validate at API boundaries
