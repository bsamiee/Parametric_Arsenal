using Arsenal.Core.Errors;

namespace Arsenal.Core.Results;

/// <summary>Result system errors (1000-1999) - aliases for backward compatibility.</summary>
[Obsolete("Use ErrorFactory.Results instead", error: false)]
public static class ResultErrors {
    /// <summary>Result creation and manipulation errors (1000-1099).</summary>
    internal static class Factory {
        public static readonly SystemError NoValueProvided = ErrorFactory.Results.NoValueProvided();
        public static readonly SystemError InvalidCreateParameters = ErrorFactory.Results.InvalidCreateParameters();
        public static readonly SystemError InvalidValidateParameters = ErrorFactory.Results.InvalidValidateParameters();
        public static readonly SystemError InvalidLiftParameters = ErrorFactory.Results.InvalidLiftParameters();
    }

    /// <summary>State access errors (1100-1199).</summary>
    internal static class State {
        public static readonly SystemError InvalidAccess = ErrorFactory.Results.InvalidAccess();
    }
}
