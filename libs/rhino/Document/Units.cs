using System;
using System.Globalization;
using Arsenal.Core.Result;
using Rhino;
using Rhino.Geometry;

namespace Arsenal.Rhino.Document;

/// <summary>Unit conversion and formatting utilities backed by RhinoCommon.</summary>
public static class Units
{
    /// <summary>Supported unit systems for dimensional calculations.</summary>
    public enum UnitType
    {
        /// <summary>Millimeter units (mm).</summary>
        Millimeters,
        /// <summary>Centimeter units (cm).</summary>
        Centimeters,
        /// <summary>Meter units (m).</summary>
        Meters,
        /// <summary>Kilometer units (km).</summary>
        Kilometers,
        /// <summary>Inch units (in).</summary>
        Inches,
        /// <summary>Foot units (ft).</summary>
        Feet,
        /// <summary>Yard units (yd).</summary>
        Yards,
        /// <summary>Mile units (mi).</summary>
        Miles
    }

    /// <summary>Converts value between unit systems.</summary>
    public static Result<double> Convert(double value, UnitType from, UnitType to)
    {
        if (!double.IsFinite(value))
        {
            return Result<double>.Fail(new Failure("units.invalidValue", "Value must be finite."));
        }

        if (from == to)
        {
            return Result<double>.Success(value);
        }

        Result<UnitSystem> fromSystem = ToUnitSystem(from);
        if (!fromSystem.IsSuccess)
        {
            return Result<double>.Fail(fromSystem.Failure!);
        }

        Result<UnitSystem> toSystem = ToUnitSystem(to);
        if (!toSystem.IsSuccess)
        {
            return Result<double>.Fail(toSystem.Failure!);
        }

        double scale = RhinoMath.UnitScale(fromSystem.Value, toSystem.Value);
        return Result<double>.Success(value * scale);
    }

    /// <summary>Converts area between unit systems.</summary>
    public static Result<double> ConvertArea(double area, UnitType from, UnitType to)
    {
        if (!double.IsFinite(area) || area < 0)
        {
            return Result<double>.Fail(new Failure("units.invalidValue", "Area must be a non-negative finite number."));
        }

        Result<double> scale = Convert(1.0, from, to);
        return scale.IsSuccess
            ? Result<double>.Success(area * scale.Value * scale.Value)
            : Result<double>.Fail(scale.Failure!);
    }

    /// <summary>Converts volume between unit systems.</summary>
    public static Result<double> ConvertVolume(double volume, UnitType from, UnitType to)
    {
        if (!double.IsFinite(volume) || volume < 0)
        {
            return Result<double>.Fail(new Failure("units.invalidValue", "Volume must be a non-negative finite number."));
        }

        Result<double> scale = Convert(1.0, from, to);
        return scale.IsSuccess
            ? Result<double>.Success(volume * scale.Value * scale.Value * scale.Value)
            : Result<double>.Fail(scale.Failure!);
    }

    /// <summary>Gets scale factor between unit systems.</summary>
    public static Result<double> GetScaleFactor(UnitType from, UnitType to)
    {
        if (from == to)
        {
            return Result<double>.Success(1.0);
        }

        Result<UnitSystem> fromSystem = ToUnitSystem(from);
        if (!fromSystem.IsSuccess)
        {
            return Result<double>.Fail(fromSystem.Failure!);
        }

        Result<UnitSystem> toSystem = ToUnitSystem(to);
        if (!toSystem.IsSuccess)
        {
            return Result<double>.Fail(toSystem.Failure!);
        }

        return Result<double>.Success(RhinoMath.UnitScale(fromSystem.Value, toSystem.Value));
    }

    /// <summary>Gets unit system from Rhino document.</summary>
    public static UnitType GetDocumentUnits(RhinoDoc? doc = null)
    {
        RhinoDoc? target = doc ?? RhinoDoc.ActiveDoc;
        if (target is null)
        {
            return UnitType.Meters;
        }

        Result<UnitType> result = FromUnitSystem(target.ModelUnitSystem);
        return result.IsSuccess ? result.Value : UnitType.Meters;
    }

    /// <summary>Scales geometry between unit systems.</summary>
    public static Result<GeometryBase> ScaleGeometry(GeometryBase geometry, UnitType from, UnitType to)
    {
        Result<double> scale = GetScaleFactor(from, to);
        if (!scale.IsSuccess)
        {
            return Result<GeometryBase>.Fail(scale.Failure!);
        }

        GeometryBase copy = geometry.Duplicate();
        Transform transform = Transform.Scale(Point3d.Origin, scale.Value);

        if (!copy.Transform(transform))
        {
            return Result<GeometryBase>.Fail(new Failure("units.transform", "Failed to scale geometry."));
        }

        return Result<GeometryBase>.Success(copy);
    }

    /// <summary>Formats numeric value with unit suffix.</summary>
    public static Result<string> Format(double value, UnitType units, int decimals = 2)
    {
        if (!double.IsFinite(value))
        {
            return Result<string>.Fail(new Failure("units.invalidValue", "Value must be finite."));
        }

        if (decimals is < 0 or > 15)
        {
            return Result<string>.Fail(new Failure("units.decimals", "Decimals must be between 0 and 15."));
        }

        string suffix = units switch
        {
            UnitType.Millimeters => "mm",
            UnitType.Centimeters => "cm",
            UnitType.Meters => "m",
            UnitType.Kilometers => "km",
            UnitType.Inches => "in",
            UnitType.Feet => "ft",
            UnitType.Yards => "yd",
            UnitType.Miles => "mi",
            _ => string.Empty
        };

        if (suffix.Length == 0)
        {
            return Result<string>.Fail(new Failure("units.invalid", "Unsupported unit system."));
        }

        string formatted = value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        return Result<string>.Success($"{formatted}{suffix}");
    }

    /// <summary>Parses dimension string with optional unit suffix.</summary>
    public static Result<double> ParseDimension(string? input, UnitType defaultUnits)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<double>.Fail(new Failure("units.parse", "Input cannot be null or whitespace."));
        }

        string text = input.Trim();

        Result<double> imperial = ParseFeetInches(text);
        if (imperial.IsSuccess)
        {
            Result<double> converted = Convert(imperial.Value, UnitType.Inches, defaultUnits);
            return converted.IsSuccess ? converted : Result<double>.Fail(converted.Failure!);
        }

        Result<(double value, UnitType units)> suffix = ParseWithSuffix(text);
        if (suffix.IsSuccess)
        {
            Result<double> converted = Convert(suffix.Value.value, suffix.Value.units, defaultUnits);
            return converted.IsSuccess ? converted : Result<double>.Fail(converted.Failure!);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric))
        {
            return Result<double>.Success(numeric);
        }

        return Result<double>.Fail(new Failure("units.parse", $"Unable to parse dimension '{input}'."));
    }

    private static Result<UnitSystem> ToUnitSystem(UnitType units)
    {
        UnitSystem system = units switch
        {
            UnitType.Millimeters => UnitSystem.Millimeters,
            UnitType.Centimeters => UnitSystem.Centimeters,
            UnitType.Meters => UnitSystem.Meters,
            UnitType.Kilometers => UnitSystem.Kilometers,
            UnitType.Inches => UnitSystem.Inches,
            UnitType.Feet => UnitSystem.Feet,
            UnitType.Yards => UnitSystem.Yards,
            UnitType.Miles => UnitSystem.Miles,
            _ => UnitSystem.None
        };

        return system == UnitSystem.None
            ? Result<UnitSystem>.Fail(new Failure("units.invalid", $"Unsupported unit system: {units}"))
            : Result<UnitSystem>.Success(system);
    }

    private static Result<UnitType> FromUnitSystem(UnitSystem system)
    {
        UnitType units = system switch
        {
            UnitSystem.Millimeters => UnitType.Millimeters,
            UnitSystem.Centimeters => UnitType.Centimeters,
            UnitSystem.Meters => UnitType.Meters,
            UnitSystem.Kilometers => UnitType.Kilometers,
            UnitSystem.Inches => UnitType.Inches,
            UnitSystem.Feet => UnitType.Feet,
            UnitSystem.Yards => UnitType.Yards,
            UnitSystem.Miles => UnitType.Miles,
            _ => (UnitType)(-1)
        };

        return (int)units == -1
            ? Result<UnitType>.Fail(new Failure("units.invalid", $"Unsupported Rhino unit system: {system}"))
            : Result<UnitType>.Success(units);
    }

    private static Result<double> ParseFeetInches(string input)
    {
        double totalInches = 0;
        string[] parts = input.Split('\'');

        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
        {
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double feet) || feet < 0)
            {
                return Result<double>.Fail(new Failure("units.parse", "Invalid feet component."));
            }

            totalInches += feet * 12;
        }

        if (parts.Length > 1)
        {
            string inchesText = parts[1].Replace("\"", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(inchesText))
            {
                if (!double.TryParse(inchesText, NumberStyles.Float, CultureInfo.InvariantCulture, out double inches) || inches < 0)
                {
                    return Result<double>.Fail(new Failure("units.parse", "Invalid inches component."));
                }

                totalInches += inches;
            }
        }

        return Result<double>.Success(totalInches);
    }

    private static Result<(double value, UnitType units)> ParseWithSuffix(string input)
    {
        string[] suffixes = ["mm", "cm", "m", "km", "in", "ft", "yd", "mi"];

        foreach (string suffix in suffixes)
        {
            if (input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                string numericPart = input[..^suffix.Length].Trim();
                if (!double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
                {
                    return Result<(double, UnitType)>.Fail(new Failure("units.parse", $"Invalid numeric portion: {numericPart}"));
                }

                UnitType units = suffix.ToLowerInvariant() switch
                {
                    "mm" => UnitType.Millimeters,
                    "cm" => UnitType.Centimeters,
                    "m" => UnitType.Meters,
                    "km" => UnitType.Kilometers,
                    "in" => UnitType.Inches,
                    "ft" => UnitType.Feet,
                    "yd" => UnitType.Yards,
                    "mi" => UnitType.Miles,
                    _ => UnitType.Meters
                };

                return Result<(double, UnitType)>.Success((value, units));
            }
        }

        return Result<(double, UnitType)>.Fail(new Failure("units.parse", "No unit suffix detected."));
    }
}
