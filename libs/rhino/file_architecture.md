# File Architecture: The 4-File Pattern

## Overview

The "algebraic" architecture organizes each domain folder into **4 files** that form distinct layers:

- `X.cs` (main / API)
- `XConfig.cs`
- `XCore.cs`
- `XCompute.cs`

---

## Layer 1: Main (`X.cs`) - Public API + Domain Model

**Purpose**: Single public entrypoint type (e.g., `Topology`, `Spatial`)

### Structure

All public algebraic types for this folder are **nested** inside the main type:
- Requests
- Strategies
- Modes
- Diagnoses
- etc.

### Responsibilities

- **Owns the "language" of the folder**: Types like `TopologyHealingStrategy`, `TopologyDiagnosis`, `ClusteringRequest`, etc.
- **Exposes public static methods** that:
  - Accept these algebraic types
  - Forward into `XCore` / `XCompute` via `UnifiedOperation` + `Result<T>`

### Key Principle

**No new top-level public types, no new files.** All domain variants are nested records under the main API type, maintaining **one top-level type and 4 files**.

---

## Layer 2: Config (`XConfig.cs`) - Constants + Metadata

**Purpose**: Constants and meta-mapping from algebraic types

### Structure

- Internal constants, tuned parameters (tolerance multipliers, max iterations, seeds)
- Internal metadata for `UnifiedOperation` and validation:
  - Maps from geometry type and algebraic variant → `V` flags, operation name, buffer sizes, etc.

### Responsibilities

- **No domain meaning** on its own
- Answers: "How do we run this operation and validate it?"
- Contains any logic that maps from the nested algebraic type to `V`/name/buffer

---

## Layer 3: Core (`XCore.cs`) - Orchestration

**Purpose**: Orchestration over `Result<T>` + `UnifiedOperation`

### Structure

Internal orchestration logic that:
- Applies validation
- Chooses correct operation delegate
- Calls `XCompute`'s raw geometry methods

### Responsibilities

- Understands `OperationConfig<TIn, TOut>`
- **Does not contain heavy geometry algorithms**; it wires them up
- Works with algebraic types as arguments and transforms them into the right calls into `XCompute`

---

## Layer 4: Compute (`XCompute.cs`) - Raw Algorithms

**Purpose**: Raw Rhino SDK workhorses

### Structure

Dense, algorithmic methods that actually perform:
- Repairs, joins, targeted joins (topology)
- DBSCAN, KMeans, hierarchical clustering (spatial)
- Anything that is "loop + math + RhinoCommon"

### Responsibilities

- Have **strongly typed parameters** (no `object`, no byte strategy, no open tuples)
- Be **reusable**, with no coupling to `Result<T>`, `UnifiedOperation`, or error codes except via their inputs and outputs
