---
inclusion: always
---

# Parametric Arsenal

A collection of professional C# plugins and tools for Rhino 8 and Grasshopper 3D, targeting the AEC (Architecture, Engineering, Construction) community.

## Components

- **Rhino Plugins**: C# plugins for Rhino 8, leveraging RhinoCommon SDK
- **Grasshopper Plugins**: C# components for Grasshopper, leveraging Grasshopper SDK  
- **Shared Libraries**: Reusable C# libraries in `libs/` that provide core functionality for all plugins

## Architecture Philosophy

**Polymorphism as Foundation**: All code must be built on polymorphic principles with interfaces and composition as the primary design patterns. Inheritance is used sparingly and only for stable taxonomies.

**SOLID + DRY + Composition Over Inheritance**: Strict adherence to SOLID principles, DRY methodology, and composition-based design patterns. Functional programming techniques are integrated where appropriate for pure calculations and immutable data handling.

All plugins are built on top of the shared libraries in `libs/`. The libraries provide:
- Core utilities, interfaces, and patterns (`libs/core/`)
- Rhino-specific operations with polymorphic geometry handling (`libs/rhino/`)
- Grasshopper component infrastructure with composition-based architecture (`libs/grasshopper/`)

## Code Paradigm Requirements

- **OOP with Polymorphism**: Interfaces and abstract contracts drive all design decisions
- **Composition Over Inheritance**: "has-a" and "uses-a" relationships preferred over "is-a"
- **Functional Programming Integration**: Pure functions for calculations, immutable types for data
- **Professional C# Standards**: Modern .NET 8.0 features, nullable reference types, advanced patterns

## Target Platform

- Rhino 8 (macOS and Windows) - macOS is the target/primary OS
- Grasshopper 3D
- .NET 8.0 with latest C# language features and professional development practices
