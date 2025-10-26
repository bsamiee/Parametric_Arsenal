using System;
using Arsenal.Core;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Document;

/// <summary>Unit conversion utilities for AEC workflows supporting metric and imperial systems.</summary>
public static class UnitConverter
{
    /// <summary>Common AEC unit systems mapped to RhinoCommon UnitSystem.</summary>
    public enum Units
    {
        /// <summary>Millimeters unit system.</summary>
        Millimeters,
        /// <summary>Centimeters unit system.</summary>
        Centimeters,
        /// <summary>Meters unit system.</summary>
        Meters,
        /// <summary>Kilometers unit system.</summary>
        Kilometers,
        /// <summary>Inches unit system.</summary>
        Inches,
        /// <summary>Feet unit system.</summary>
        Feet,
        /// <summary>Yards unit system.</summary>
        Yards,
        /// <summary>Miles unit system.</summary>
        Miles
    }

    /// <summary>Maps custom Units enum to RhinoCommon UnitSystem enum.</summary>
    private static Result<UnitSystem> ToUnitSystem(Units units)
    {
        UnitSystem unitSystem = units switch
        {
            Units.Millimeters => UnitSystem.Millimeters,
            Units.Centimeters => UnitSystem.Centimeters,
            Units.Meters => UnitSystem.Meters,
            Units.Kilometers => UnitSystem.Kilometers,
            Units.Inches => UnitSystem.Inches,
            Units.Feet => UnitSystem.Feet,
            Units.Yards => UnitSystem.Yards,
            Units.Miles => UnitSystem.Miles,
            _ => UnitSystem.None
        };

        return unitSystem == UnitSystem.None
            ? Result<UnitSystem>.Fail($"Invalid unit system: {units}")
            : Result<UnitSystem>.Success(unitSystem);
    }

    /// <summary>Maps RhinoCommon UnitSystem enum to custom Units enum.</summary>
    private static Result<Units> FromUnitSystem(UnitSystem unitSystem)
    {
        Units units = unitSystem switch
        {
            UnitSystem.Millimeters => Units.Millimeters,
            UnitSystem.Centimeters => Units.Centimeters,
            UnitSystem.Meters => Units.Meters,
            UnitSystem.Kilometers => Units.Kilometers,
            UnitSystem.Inches => Units.Inches,
            UnitSystem.Feet => Units.Feet,
            UnitSystem.Yards => Units.Yards,
            UnitSystem.Miles => Units.Miles,
            _ => (Units)(-1) // Invalid marker
        };

        return (int)units == -1
            ? Result<Units>.Fail($"Unsupported unit system: {unitSystem}")
            : Result<Units>.Success(units);
    }

    /// <summary>Converts a value from one unit system to another using RhinoCommon SDK.</summary>
    public static Result<double> Convert(double value, Units from, Units to)
    {
        Result<double> valueValidation = Guard.Require(value, double.IsFinite, "Value must be finite");
        if (!valueValidation.Ok)
        {
            return valueValidation;
        }

        if (from == to)
        {
            return Result<double>.Success(value);
        }

        Result<UnitSystem> fromUnitSystem = ToUnitSystem(from);
        if (!fromUnitSystem.Ok)
        {
            return Result<double>.Fail(fromUnitSystem.Error!);
        }

        Result<UnitSystem> toUnitSystem = ToUnitSystem(to);
        if (!toUnitSystem.Ok)
        {
            return Result<double>.Fail(toUnitSystem.Error!);
        }

        // Use RhinoCommon SDK for conversion
        double scaleFactor = RhinoMath.UnitScale(fromUnitSystem.Value, toUnitSystem.Value);
        double result = value * scaleFactor;

        return Result<double>.Success(result);
    }

    /// <summary>Gets the unit system from a Rhino document with fallback to active document or default.</summary>
    public static Units GetDocumentUnits(RhinoDoc? doc = null)
    {
        RhinoDoc? activeDoc = doc ?? RhinoDoc.ActiveDoc;
        if (activeDoc is null)
        {
            return Units.Meters; // Default fallback following Tolerances pattern
        }

        Result<Units> unitsResult = FromUnitSystem(activeDoc.ModelUnitSystem);
        return unitsResult.Ok ? unitsResult.Value : Units.Meters;
    }

    /// <summary>Scales geometry to convert between unit systems.</summary>
    public static Result<GeometryBase> ScaleToUnits(GeometryBase? geometry, Units from, Units to, RhinoDoc? doc = null)
    {
        Result<GeometryBase> geometryValidation = Guard.RequireNonNull(geometry, nameof(geometry));
        if (!geometryValidation.Ok)
        {
            return Result<GeometryBase>.Fail(geometryValidation.Error!);
        }

        if (!geometryValidation.Value!.IsValid)
        {
            return Result<GeometryBase>.Fail("Geometry must be valid");
        }

        if (from == to)
        {
            return Result<GeometryBase>.Success(geometryValidation.Value.Duplicate());
        }

        Result<double> scaleFactorResult = GetScaleFactor(from, to);
        if (!scaleFactorResult.Ok)
        {
            return Result<GeometryBase>.Fail(scaleFactorResult.Error!);
        }

        GeometryBase scaled = geometryValidation.Value.Duplicate();
        Transform scaleTransform = Transform.Scale(Point3d.Origin, scaleFactorResult.Value);

        if (!scaled.Transform(scaleTransform))
        {
            return Result<GeometryBase>.Fail("Failed to apply scale transformation to geometry");
        }

        return Result<GeometryBase>.Success(scaled);
    }

    /// <summary>Gets the scale factor to convert between unit systems using RhinoCommon SDK.</summary>
    public static Result<double> GetScaleFactor(Units from, Units to)
    {
        if (from == to)
        {
            return Result<double>.Success(1.0);
        }

        Result<UnitSystem> fromUnitSystem = ToUnitSystem(from);
        if (!fromUnitSystem.Ok)
        {
            return Result<double>.Fail(fromUnitSystem.Error!);
        }

        Result<UnitSystem> toUnitSystem = ToUnitSystem(to);
        if (!toUnitSystem.Ok)
        {
            return Result<double>.Fail(toUnitSystem.Error!);
        }

        // Use RhinoCommon SDK for scale factor calculation
        double scaleFactor = RhinoMath.UnitScale(fromUnitSystem.Value, toUnitSystem.Value);
        return Result<double>.Success(scaleFactor);
    }

    /// <summary>Formats a value with appropriate unit suffix.</summary>
    public static Result<string> Format(double value, Units units, int decimalPlaces = 2)
    {
        if (!double.IsFinite(value))
        {
            return Result<string>.Fail("Value must be finite");
        }

        Result<int> decimalValidation = Guard.RequireInRange(decimalPlaces, 0, 15, nameof(decimalPlaces));
        if (!decimalValidation.Ok)
        {
            return Result<string>.Fail(decimalValidation.Error!);
        }

        string formatted = value.ToString($"F{decimalPlaces}");
        string suffix = units switch
        {
            Units.Millimeters => "mm",
            Units.Centimeters => "cm",
            Units.Meters => "m",
            Units.Kilometers => "km",
            Units.Inches => "\"",
            Units.Feet => "'",
            Units.Yards => "yd",
            Units.Miles => "mi",
            _ => ""
        };

        if (string.IsNullOrEmpty(suffix))
        {
            return Result<string>.Fail($"Invalid unit system: {units}");
        }

        return Result<string>.Success($"{formatted}{suffix}");
    }

    /// <summary>Converts area value between unit systems.</summary>
    public static Result<double> ConvertArea(double area, Units from, Units to)
    {
        Result<double> areaValidation = Guard.Require(area, double.IsFinite, "Area must be finite");
        if (!areaValidation.Ok)
        {
            return areaValidation;
        }

        Result<double> nonNegativeValidation = Guard.RequireNonNegative(area, nameof(area));
        if (!nonNegativeValidation.Ok)
        {
            return nonNegativeValidation;
        }

        if (from == to)
        {
            return Result<double>.Success(area);
        }

        Result<double> scaleFactorResult = GetScaleFactor(from, to);
        if (!scaleFactorResult.Ok)
        {
            return Result<double>.Fail(scaleFactorResult.Error!);
        }

        double scaleFactor = scaleFactorResult.Value;
        double convertedArea = area * scaleFactor * scaleFactor; // Area scales with square of linear scale

        return Result<double>.Success(convertedArea);
    }

    /// <summary>Converts volume value between unit systems.</summary>
    public static Result<double> ConvertVolume(double volume, Units from, Units to)
    {
        Result<double> volumeValidation = Guard.Require(volume, double.IsFinite, "Volume must be finite");
        if (!volumeValidation.Ok)
        {
            return volumeValidation;
        }

        Result<double> nonNegativeValidation = Guard.RequireNonNegative(volume, nameof(volume));
        if (!nonNegativeValidation.Ok)
        {
            return nonNegativeValidation;
        }

        if (from == to)
        {
            return Result<double>.Success(volume);
        }

        Result<double> scaleFactorResult = GetScaleFactor(from, to);
        if (!scaleFactorResult.Ok)
        {
            return Result<double>.Fail(scaleFactorResult.Error!);
        }

        double scaleFactor = scaleFactorResult.Value;
        double convertedVolume = volume * scaleFactor * scaleFactor * scaleFactor; // Volume scales with cube of linear scale

        return Result<double>.Success(convertedVolume);
    }

    /// <summary>Parses common AEC dimension strings (e.g., "5'-6\"", "1.5m").</summary>
    public static Result<double> ParseDimension(string? input, Units defaultUnits)
    {
        Result<string> inputValidation = Guard.RequireNonWhiteSpace(input, nameof(input));
        if (!inputValidation.Ok)
        {
            return Result<double>.Fail(inputValidation.Error!);
        }

        string trimmedInput = inputValidation.Value!.Trim();

        // Handle feet and inches notation (e.g., "5'-6\"" or "5'6\"")
        if (trimmedInput.Contains('\'') || trimmedInput.Contains('"'))
        {
            return ParseFeetInches(trimmedInput);
        }

        // Handle metric with suffix (e.g., "1.5m", "150cm", "1500mm")
        if (TryParseWithSuffix(trimmedInput, out double value, out Units? detectedUnits))
        {
            if (detectedUnits.HasValue && detectedUnits.Value != defaultUnits)
            {
                Result<double> conversion = Convert(value, detectedUnits.Value, defaultUnits);
                return conversion;
            }

            return Result<double>.Success(value);
        }

        // Try parsing as plain number
        if (double.TryParse(trimmedInput, out double plainValue))
        {
            return Result<double>.Success(plainValue);
        }

        return Result<double>.Fail($"Cannot parse dimension string: {trimmedInput}");
    }

    /// <summary>Parses feet and inches notation.</summary>
    private static Result<double> ParseFeetInches(string input)
    {
        double totalInches = 0;

        // Split by feet marker
        string[] parts = input.Split('\'');

        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
        {
            // Parse feet
            string feetStr = parts[0].Trim();
            if (!double.TryParse(feetStr, out double feet))
            {
                return Result<double>.Fail($"Invalid feet value: {feetStr}");
            }

            if (feet < 0)
            {
                return Result<double>.Fail("Feet value cannot be negative");
            }

            totalInches += feet * 12;
        }

        if (parts.Length > 1)
        {
            // Parse inches
            string inchesStr = parts[1].Replace("\"", "").Trim();
            if (!string.IsNullOrWhiteSpace(inchesStr))
            {
                if (!double.TryParse(inchesStr, out double inches))
                {
                    return Result<double>.Fail($"Invalid inches value: {inchesStr}");
                }

                if (inches < 0)
                {
                    return Result<double>.Fail("Inches value cannot be negative");
                }

                totalInches += inches;
            }
        }

        return Result<double>.Success(totalInches);
    }

    /// <summary>Tries to parse a value with unit suffix using collection expressions.</summary>
    private static bool TryParseWithSuffix(string input, out double value, out Units? units)
    {
        value = 0;
        units = null;

        // Check for common suffixes
        string[] suffixes = ["mm", "cm", "m", "km", "in", "ft", "yd", "mi"];

        foreach (string suffix in suffixes)
        {
            if (input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                string numberPart = input[..^suffix.Length].Trim();
                if (double.TryParse(numberPart, out value) && value >= 0)
                {
                    units = suffix.ToLowerInvariant() switch
                    {
                        "mm" => Units.Millimeters,
                        "cm" => Units.Centimeters,
                        "m" => Units.Meters,
                        "km" => Units.Kilometers,
                        "in" => Units.Inches,
                        "ft" => Units.Feet,
                        "yd" => Units.Yards,
                        "mi" => Units.Miles,
                        _ => null
                    };

                    return units.HasValue;
                }
            }
        }

        return false;
    }
}
