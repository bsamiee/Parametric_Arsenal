---
inclusion: always
---

# Parametric Arsenal

A collection of C# plugins and tools for Rhino 8 and Grasshopper 3D, targeting the AEC (Architecture, Engineering, Construction) community.

## Components

- **Rhino Plugins**: C# plugins for Rhino 8, leveraging RhinoCommon SDK
- **Grasshopper Plugins**: C# components for Grasshopper, leveraging Grasshopper SDK
- **Shared Libraries**: Reusable C# libraries in `libs/` that provide core functionality for all plugins

## Architecture Philosophy

All plugins are built on top of the shared libraries in `libs/`. The libraries provide:
- Core utilities and patterns (`libs/core/`)
- Rhino-specific operations (`libs/rhino/`)
- Grasshopper component infrastructure (`libs/grasshopper/`)

Plugins should leverage these libraries and the official SDKs rather than reimplementing functionality.

## Target Platform

- Rhino 8 (macOS and Windows) - macOS is the target/primary OS
- Grasshopper 3D
- .NET 8.0 with latest C# language features, and best modern practices
