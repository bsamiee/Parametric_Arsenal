# Rhino SDK Math Reference (RhinoCommon / RhinoMath)

## Where the rest of the math lives in the Rhino SDK (quick map)

- **Core scalar math (`double`, `float`)**  
  Use .NET’s `System.Math` for fundamental operations such as `Sqrt`, `Sin`, `Cos`, `Tan`, `Atan2`, `Pow`, `Abs`, `Ceiling`, `Floor`, etc. `RhinoMath` is additive (tolerances, units, comparisons), not a replacement for `System.Math`.

- **Vector and point arithmetic**  
  `Rhino.Geometry.Vector3d` and `Rhino.Geometry.Point3d` provide operator overloads and methods for addition, subtraction, scaling, dot product (`Dot`), cross product (`Cross`), norms (`Length`, `Unitize`), distances (`DistanceTo`), and projections. These types also enforce Rhino-style validity (`IsValid` using `RhinoMath.IsValidDouble`).

- **Transforms and matrices**  
  `Rhino.Geometry.Transform` represents 4×4 affine transforms (translate, rotate, scale, mirror, shear, change-of-basis) with composition and point/vector multiplication via `*`. `Rhino.Geometry.Matrix` provides general-purpose m×n linear algebra for solving systems, eigen problems, and other matrix operations.

- **Geometry-derived math**  
  Many geometry types encapsulate their own “math”:  
  - Curves (`Rhino.Geometry.Curve` and subclasses) expose parametrization, derivatives, curvature, arc length, closest points, and evaluation.  
  - Surfaces and Breps (`Surface`, `Brep`) expose partial derivatives, normals, Gaussian/mean curvature, principal curvatures, and area/volume integrals.  
  - Meshes expose face normals, vertex normals, and various measurement utilities.  
  In almost all of these, tolerance and unit behaviors are consistent with `RhinoMath` (`ZeroTolerance`, `UnsetValue`, etc.).

- **Tolerances and validity logic**  
  All higher-level geometry APIs rely on `RhinoMath` for zero tests, equality checks, unit scaling, and validity sentinels. Correct usage of `RhinoMath` is critical when writing custom geometry algorithms that interoperate with core Rhino types.

---

## RhinoMath: constants and operations

### Overview

`RhinoMath` is a static utility class in RhinoCommon that provides:  
- Geometric and numeric constants tuned for Rhino’s modeling environment.  
- Robust floating-point comparison helpers.  
- Unit-conversion utilities.  
- A few advanced geometric evaluation helpers (surface normals and curvature).  
- CRC-32 hashing helpers consistent with Rhino’s internal usage.

There is no `RhinoMath.SqrtTwo`. If you need √2, use `System.Math.Sqrt(2.0)` or a literal constant. RhinoMath focuses on π-derived constants, tolerances, and sentinel values.

---

## Constants (fields)

- **DefaultAngleTolerance**  
  Default angular tolerance in radians (one degree), used when no explicit angle tolerance is provided.

- **DefaultDistanceToleranceMillimeters**  
  Default model distance tolerance in millimeters, used in many geometry operations when a document tolerance is not yet available.

- **Epsilon**  
  Machine epsilon for `double` (`DBL_EPSILON`): the smallest positive `double` such that `1.0 + Epsilon != 1.0`. Used as a baseline for purely numerical comparisons.

- **HalfPI**  
  π/2 (90 degrees), representing a quarter rotation; used wherever a right angle is required.

- **QuarterPI**  
  π/4 (45 degrees), representing an eighth rotation; often used for symmetric angular constructions or diagnostic checks.

- **SqrtEpsilon**  
  A tolerance value tuned for comparisons involving square roots and similar operations; larger than `ZeroTolerance` to accommodate accumulated numerical error in root-based computations.

- **Tau**  
  2π, representing a full turn in radians; equivalent to `TwoPI`, provided for users who prefer τ as the fundamental angular constant.

- **TwoPI**  
  2π, representing a full turn in radians; widely used in periodic angle computations and trigonometric parameterizations.

- **UnsetIntIndex**  
  Sentinel integer value used when an index is “not set” or invalid in contexts where 0 or negative indices might otherwise be valid values.

- **UnsetSingle**  
  Sentinel “unset” value for `float` (`Single`), serving a similar purpose to `UnsetValue` but for single-precision quantities.

- **UnsetValue**  
  Sentinel “unset” value for `double`, used instead of `Double.NaN` in Rhino APIs to avoid propagating NaNs through geometry and to make validity checks explicit.

- **ZeroTolerance**  
  Baseline absolute zero tolerance (2^-32) for model-space comparisons; no smaller than `Epsilon` and significantly smaller than modeling tolerances. Primarily used for low-level “is this numerically zero?” tests.

---

## Methods (operations and logic)

### Comparison, clamping, and wrapping

- **Clamp(double value, double min, double max)**  
  Constrains a `double` to the closed interval `[min, max]`, returning `min` if below range and `max` if above range.

- **Clamp(int value, int min, int max)**  
  Integer variant of `Clamp`, constraining an `int` to `[min, max]` with the same semantics.

- **EpsilonEquals(double a, double b, double epsilon)**  
  Returns `true` if two `double` values differ by no more than `epsilon`; intended for robust equality checks in geometric code.

- **EpsilonEquals(float a, float b, float epsilon)**  
  `float` (`Single`) variant of `EpsilonEquals`, providing the same behavior for single-precision comparisons.

- **Wrap(double x, double min, double max)**  
  Wraps a `double` value into the cyclic interval `[min, max)`, typically used to normalize angles into ranges such as `[0, 2π)` or `(-π, π]`.

### Validity and sentinel handling

- **IsValidDouble(double x)**  
  Returns `true` if `x` is a valid Rhino `double` (not `UnsetValue`, not `NaN`, not `Infinity`); this is the canonical validity check for scalar values in RhinoCommon geometry code.

- **IsValidSingle(float x)**  
  Returns `true` if a `float` is a valid Rhino `Single` (not `UnsetSingle`, not `NaN`, not `Infinity`); used in APIs dealing with single-precision data.

- **IntIndexToString(int i)**  
  Converts an integer index to a user-facing string representation, typically for UI, diagnostic messages, and formatted reports.

### Unit and scale management

- **MetersPerUnit(UnitSystem unitSystem)**  
  Returns the scale factor (meters per unit) for a given `UnitSystem`, enabling conversions to SI (meters) for internal calculations, storage, or interop.

- **UnitScale(UnitSystem from, UnitSystem to)**  
  Computes a scalar factor to convert lengths from one `UnitSystem` to another, ignoring any document-scale modifiers.

- **UnitScale(UnitSystem from, double modelScalingFrom, UnitSystem to, double modelScalingTo)**  
  Extended unit scale computation that includes document model-scaling factors, providing the full scalar needed when documents are scaled beyond pure unit changes.

### Angle conversions

- **ToDegrees(double radians)**  
  Converts an angle from radians to degrees; useful at UI boundaries, reporting, and when interfacing with APIs that expect degrees.

- **ToRadians(double degrees)**  
  Converts an angle from degrees to radians; used before passing values to trigonometric functions and geometry constructors that operate in radians.

### Parsing numeric expressions

- **ParseNumber(string expression)**  
  Evaluates a simple numeric expression (as used in Rhino’s command-line input) and returns the resulting `double`; supports basic arithmetic syntax instead of just literal numbers.

- **TryParseNumber(string expression, out double value)**  
  Safe variant of `ParseNumber`; returns a `bool` indicating success or failure without throwing, and provides the parsed `double` by `out` parameter.

### Hashing and checksums

- **CRC32(uint seed, byte[] buffer)**  
  Computes or updates a CRC-32 checksum from a seed and a byte buffer; used for consistent hashing of binary data.

- **CRC32(uint seed, double value)**  
  CRC-32 helper specialized for `double` values, enabling stable hashes for floating-point parameters and scalar attributes.

- **CRC32(uint seed, int value)**  
  CRC-32 helper for `int` values, used to fold indices and discrete parameters into a hash in a consistent manner.

### Advanced surface/curvature evaluation

These methods are low-level and intended for advanced users who work directly with surface derivatives.

- **EvaluateNormal(double du, double dv, double[] derivatives, out double[] normal)**  
  Computes a unit surface normal from first-order partial derivatives (`du`, `dv` components) contained in the derivatives array; output is a normalized 3D vector.

- **EvaluateNormalPartials(double du, double dv, double[] derivatives, out double[] normalPartials)**  
  Computes partial derivatives of the unit normal itself with respect to the surface parameters, providing higher-order differential information (e.g., for shading, curvature analysis).

- **EvaluateSectionalCurvature(double du, double dv, double[] derivatives, double[] unitNormal, out double curvature)**  
  Computes sectional curvature in the direction determined by `(du, dv)` on a surface, using derivatives and a supplied unit normal; useful for curvature diagnostics and advanced analysis tools.

---

## Further consideration and best practices

- **Sentinels vs. NaN in geometry code**  
  Rhino intentionally avoids NaNs in its geometry layer, preferring `UnsetValue`/`UnsetSingle` and explicit validity checks (`IsValidDouble`, `IsValidSingle`). When writing algorithms that integrate with Rhino geometry, prefer these sentinels and checks over relying on `Double.IsNaN` propagation.

- **Tolerances: choosing the right scale**  
  Use `ZeroTolerance` for low-level “is zero?” checks, and consider `SqrtEpsilon` where square roots or accumulated floating-point error might dominate (e.g., normalized vectors, distances derived from squared quantities). Document-level tolerances (e.g., model distance tolerance) should still be used for geometric decisions at modeling scale.

- **Angle normalization with `Wrap`**  
  For any API that expects angles in a specific range (e.g., `[0, 2π)` or `(-π, π]`), prefer `Wrap` over custom modulo logic to keep behavior consistent and robust against negative and out-of-range inputs.

- **Unit safety at API boundaries**  
  Always use `UnitScale` or `MetersPerUnit` when transferring geometry or measurements between documents, Grasshopper definitions, or external services. This avoids latent scaling bugs when document units differ (e.g., inches vs. meters).

- **Stable hashing for caching and change detection**  
  When building caches keyed on geometry parameters, use the `CRC32` helpers to create stable 32-bit hashes from doubles, ints, and byte arrays. This is particularly effective for detecting when expensive computations (e.g., meshing, analysis fields) need to be recomputed.
