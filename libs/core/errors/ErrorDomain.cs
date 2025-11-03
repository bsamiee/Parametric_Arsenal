namespace Arsenal.Core.Errors;

/// <summary>Hierarchical error domain classification for system-wide error categorization.</summary>
public enum ErrorDomain {
    None = 0,
    Results = 1000,
    Geometry = 2000,
    Validation = 3000,
}
