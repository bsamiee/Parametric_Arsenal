namespace Arsenal.Core.Errors;

/// <summary>Error domain categorization for system-wide error classification.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte storage for memory efficiency")]
public enum ErrorDomain : byte {
    Unknown = 0,
    Results = 10,
    Geometry = 20,
    Validation = 30,
    Spatial = 40,
    Topology = 50,
}
