---
inclusion: always
---

# Technology Stack

## Languages & Frameworks

- **C#**: .NET 8.0 with latest language features, nullable reference types enabled
- **Build System**: MSBuild via Visual Studio solution (Parametric_Arsenal.sln)

## Key Dependencies

### Official SDKs (Always Leverage These)
- **RhinoCommon SDK** (Rhino 8): Primary API for Rhino geometry, documents, and commands
- **Grasshopper SDK** (8.16+): Primary API for Grasshopper components and data structures
- System.Drawing.Common

### Shared Libraries (`libs/`)
All plugins must build upon and leverage the shared libraries:
- `Arsenal.Core`: Core utilities, patterns (e.g., `Result<T>`), and common functionality
- `Arsenal.Rhino`: Rhino-specific operations, geometry utilities, tolerance management
- `Arsenal.Grasshopper`: Grasshopper component base classes and utilities

## Build & Development

### Building Projects
```bash
# Build entire solution
dotnet build Parametric_Arsenal.sln

# Build specific library
dotnet build libs/core/Core.csproj
dotnet build libs/rhino/Rhino.csproj
dotnet build libs/grasshopper/Grasshopper.csproj

# Build specific plugin
dotnet build grasshopper/plugins/HopperHelper/HopperHelper.csproj
dotnet build rhino/plugins/SomePlugin/SomePlugin.csproj

# Build configurations
dotnet build -c Debug
dotnet build -c Release
```

## Project Configuration

### C# Naming Conventions
- Root namespaces: `Arsenal.Core`, `Arsenal.Rhino`, `Arsenal.Grasshopper`
- Assembly names match root namespaces
- Grasshopper plugins use `.gha` extension
- Rhino plugins use `.rhp` extension

### Auto-Deployment
Grasshopper plugins automatically deploy to Rhino's Grasshopper Libraries folder after build (macOS path configured in .csproj).

## Development Principles (Polymorphic & Compositional)

1. **Interface-First Design**: Define contracts before implementations. All major functionality exposed through interfaces.
2. **Composition Over Inheritance**: Use "has-a" and "uses-a" relationships. Avoid deep inheritance hierarchies.
3. **Leverage SDKs Through Composition**: Compose RhinoCommon and Grasshopper SDK functionality behind our interfaces.
4. **Functional Core, Imperative Shell**: Pure functions for business logic, OOP for boundaries and lifetime management.
5. **Build on Shared Libraries**: Libraries compose each other through interface contracts (e.g., `Arsenal.Rhino` composes `Arsenal.Core` interfaces).
6. **Plugins as Composition Roots**: Plugins wire up concrete implementations and orchestrate through interfaces.

## Research & Documentation Tools

When researching APIs, understanding SDKs, or gathering information, use the available MCP servers:

- **Perplexity MCP**: For general technical research and API documentation
- **Tavily MCP**: For web search and documentation lookup
- **GitHub MCP**: For exploring SDK repositories and examples
- **Filesystem MCP**: For examining local SDK documentation and examples

Always research SDK capabilities before implementing custom solutions.
