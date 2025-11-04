using Arsenal.Core.Errors;

namespace Arsenal.Core.Results;

/// <summary>Result system errors (1000-1999).</summary>
public static class ResultErrors {
    /// <summary>Result creation and manipulation errors (1000-1099).</summary>
    internal static class Factory {
        public static readonly SystemError NoValueProvided = new(ErrorDomain.Results, 1001, "No value provided");
        public static readonly SystemError InvalidCreateParameters = new(ErrorDomain.Results, 1002, "Invalid Create parameters");
        public static readonly SystemError InvalidValidateParameters = new(ErrorDomain.Results, 1003, "Invalid validation parameters");
        public static readonly SystemError InvalidValidateResult = new(ErrorDomain.Results, 1004, "Invalid Validate parameters");
        public static readonly SystemError InvalidLiftParameters = new(ErrorDomain.Results, 1005, "Invalid Lift parameters");
    }

    /// <summary>State access errors (1100-1199).</summary>
    internal static class State {
        public static readonly SystemError InvalidAccess = new(ErrorDomain.Results, 1100, "Cannot access value in error state or error in success state");
    }
}
