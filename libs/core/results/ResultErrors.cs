using Arsenal.Core.Errors;

namespace Arsenal.Core.Results;

/// <summary>Result system errors (1000-1999) - aliases to E.Results for backward compatibility during transition.</summary>
public static class ResultErrors {
    /// <summary>Result creation and manipulation errors (1000-1099).</summary>
    internal static class Factory {
        public static readonly SystemError NoValueProvided = E.Results.NoValueProvided;
        public static readonly SystemError InvalidCreateParameters = E.Results.InvalidCreate;
        public static readonly SystemError InvalidValidateParameters = E.Results.InvalidValidate;
        public static readonly SystemError InvalidLiftParameters = E.Results.InvalidLift;
    }

    /// <summary>State access errors (1100-1199).</summary>
    internal static class State {
        public static readonly SystemError InvalidAccess = E.Results.InvalidAccess;
    }
}
