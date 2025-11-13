## 🏛️ RhinoMath Class

The `RhinoMath` class is a static class that provides fundamental mathematical constants and utility functions, supplementing the standard `System.Math` library.

### Constants (Fields)

* **`Epsilon`**: A very small value (1.0e-32) used to check if a single-precision float is essentially zero.
* **`ZeroTolerance`**: The standard tolerance (2.32e-10) used throughout the SDK for checking if a double-precision number is zero.
* **`SqrtEpsilon`**: A tolerance (1.49e-8) used specifically for square root comparisons.
* **`UnsetValue`**: A special "unset" or "invalid" value (`-1.23432101234321e+308`) for double-precision numbers.
* **`UnsetSingle`**: The "unset" value for single-precision floats.
* **`UnsetIntIndex`**: The "unset" value for integers (`-1`).
* **`PI`**: The ratio of a circle's circumference to its diameter (3.14159...).
* **`TwoPI`**: $2 \times \pi$, or 360 degrees in radians.
* **`HalfPI`**: $\pi / 2$, or 90 degrees in radians.
* **`QuarterPI`**: $\pi / 4$, or 45 degrees in radians.
* **`DefaultAngleTolerance`**: The default tolerance used for angle comparisons (0.01745... radians, or 1 degree).
* **`DefaultDistanceTolerance`**: The default tolerance used in model units (0.01).

### Core Methods

* **`ToRadians(double degrees)`**: Converts an angle from degrees to radians.
* **`ToDegrees(double radians)`**: Converts an angle from radians to degrees.
* **`Clamp(double val, double min, double max)`**: Restricts a value to be within a specified range.
* **`EpsilonEquals(double a, double b, double epsilon)`**: Compares two numbers to see if they are "equal" within a specified tolerance.
* **`IsValidDouble(double d)`**: Checks if a double is a valid number (not `UnsetValue` or `NaN`).
* **`UnitScale(UnitSystem from, UnitSystem to)`**: Returns the conversion factor between two unit systems (e.g., millimeters to inches).
* **`Wrap(double value, double min, double max)`**: Wraps a value around an interval (e.g., wrapping 370 degrees to 10 degrees).

---

## 🧮 Other Math & Logic Operations in the SDK

The majority of math operations in Rhino are found within the geometry types themselves, located in the **`Rhino.Geometry`** namespace.

### Point & Vector Math (`Point3d` and `Vector3d`)

These structs handle 3D coordinate and vector algebra. They also support standard operator overloading (e.g., `pt1 + vec1`).

* **`Add` / `+`**: Adds a vector to a point to get a new point, or adds two vectors.
* **`Subtract` / `-`**: Subtracts two points to get a vector, or subtracts two vectors.
* **`Multiply` / `*`**: Scales a vector by a number (scalar multiplication).
* **`Divide` / `/`**: Scales a vector by the inverse of a number.
* **`DistanceTo(Point3d other)`**: Calculates the straight-line distance between two points.
* **`Vector3d.Length`**: Gets the magnitude (length) of a vector.
* **`Vector3d.Unitize()`**: Modifies a vector to have a length of 1 (makes it a unit vector).
* **`Vector3d.IsParallelTo(Vector3d other)`**: Checks if two vectors are parallel within a tolerance.
* **`Vector3d.IsPerpendicularTo(Vector3d other)`**: Checks if two vectors are perpendicular (orthogonal) within a tolerance.
* **`Vector3d.CrossProduct(Vector3d a, Vector3d b)`**: Computes the vector perpendicular to two other vectors.
* **`Vector3d.DotProduct(Vector3d a, Vector3d b)`**: Computes the scalar dot product of two vectors (used for angle calculations).
* **`Vector3d.Rotate(double angle, Vector3d axis)`**: Rotates a vector around an axis vector by a given angle.
* **`Vector3d.IsZero`**: Checks if the vector's length is within `ZeroTolerance`.
* **`EpsilonEquals(Point3d other, double epsilon)`**: Checks if two points are in the same location within a tolerance.

### Transformation & Matrix Math (`Transform` and `Matrix`)

These types handle geometric transformations (Move, Rotate, Scale) and linear algebra. `Transform` is a 4x4 matrix optimized for 3D graphics, while `Matrix` is a general-purpose n-by-n matrix.

* **`Transform.Multiply(Transform a, Transform b)` / `*`**: Combines two transformations into a single one.
* **`Transform.Translation(Vector3d motion)`**: Creates a "Move" transformation.
* **`Transform.Rotation(double angle, Vector3d axis, Point3d center)`**: Creates a "Rotate" transformation around an axis.
* **`Transform.Scale(Point3d center, double scaleFactor)`**: Creates a uniform "Scale" transformation from a center point.
* **`Transform.TryGetInverse(out Transform inverse)`**: Gets the transformation that reverses this one (e.g., turns a "Move" into an "Undo Move").
* **`Point3d.Transform(Transform xform)`**: Applies a transformation to a point, moving it to a new location.
* **`Matrix.Transpose()`**: Flips the matrix's rows and columns.
* **`Matrix.Invert(double zeroTolerance)`**: Computes the inverse of the matrix.
* **`Matrix.RowReduce(double zeroTolerance, ...)`**: Solves a system of linear equations using Gaussian elimination.
