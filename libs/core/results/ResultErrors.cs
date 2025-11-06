using Arsenal.Core.Errors;

namespace Arsenal.Core.Results;

/// <summary>DEPRECATED: Use CoreErrors.Results instead. Maintained for backward compatibility only.</summary>
[Obsolete("Use CoreErrors.Results.* instead. This type will be removed in a future version.", error: false)]
public static class ResultErrors {
    /// <summary>DEPRECATED: Use CoreErrors.Results instead.</summary>
    [Obsolete("Use CoreErrors.Results.* instead.", error: false)]
    internal static class Factory {
        public static readonly SystemError NoValueProvided = CoreErrors.Results.NoValueProvided;
        public static readonly SystemError InvalidCreateParameters = CoreErrors.Results.InvalidCreateParameters;
        public static readonly SystemError InvalidValidateParameters = CoreErrors.Results.InvalidValidateParameters;
        public static readonly SystemError InvalidLiftParameters = CoreErrors.Results.InvalidLiftParameters;
    }

    /// <summary>DEPRECATED: Use CoreErrors.Results instead.</summary>
    [Obsolete("Use CoreErrors.Results.* instead.", error: false)]
    internal static class State {
        public static readonly SystemError InvalidAccess = CoreErrors.Results.InvalidAccess;
    }
}
