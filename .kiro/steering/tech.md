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

## Development Principles

1. **Leverage SDKs First**: Always use RhinoCommon and Grasshopper SDK functionality. Never reimplement what the SDKs provide.
2. **Build on Shared Libraries**: Use and extend `libs/` for common functionality. Libraries should build upon each other (e.g., `Arsenal.Rhino` uses `Arsenal.Core`).
3. **Plugins Use Libraries**: All plugins should reference and leverage the shared libraries rather than duplicating code.

## Research & Documentation Tools

When researching APIs, understanding SDKs, or gathering information, use the available MCP servers:

- **Perplexity MCP**: For general technical research and API documentation
- **Tavily MCP**: For web search and documentation lookup
- **GitHub MCP**: For exploring SDK repositories and examples
- **Filesystem MCP**: For examining local SDK documentation and examples

Always research SDK capabilities before implementing custom solutions.
