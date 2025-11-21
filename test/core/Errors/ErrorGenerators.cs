using Arsenal.Core.Errors;
using CsCheck;

namespace Arsenal.Core.Tests.Errors;

/// <summary>CsCheck generators for SystemError property-based testing.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Test generators used across test classes")]
public static class ErrorGenerators {
    /// <summary>Generates domain bytes 0-5.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<byte> DomainGen => Gen.Byte[0, 5];

    /// <summary>Generates codes in valid range for domain.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<int> CodeForDomainGen(byte domain) => domain switch {
        1 => Gen.Int[1000, 1999],
        2 => Gen.Int[2000, 2999],
        3 => Gen.Int[3000, 3999],
        4 => Gen.Int[4000, 4999],
        5 => Gen.Int[5000, 5999],
        _ => Gen.Int[0, 999],
    };

    /// <summary>Generates non-empty context strings.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<string> ContextGen => Gen.String[1, 50].Where(static s => !string.IsNullOrWhiteSpace(s));

    /// <summary>Generates random SystemError with valid domain/code/message.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<SystemError> SystemErrorGen =>
        from domain in DomainGen
        from code in CodeForDomainGen(domain)
        from message in Gen.String[1, 100].Where(static s => !string.IsNullOrWhiteSpace(s))
        select new SystemError(domain, code, message);

    /// <summary>Generates pairs with at least one differing field.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<(SystemError, SystemError)> DifferentErrorPairGen =>
        from error1 in SystemErrorGen
        from differenceType in Gen.Int[0, 2]
        from error2 in differenceType switch {
            0 => from d in DomainGen.Where(d => d != error1.Domain)
                 from c in CodeForDomainGen(d)
                 from m in Gen.String[1, 100].Where(static s => !string.IsNullOrWhiteSpace(s))
                 select new SystemError(d, c, m),
            1 => from c in CodeForDomainGen(error1.Domain).Where(c => c != error1.Code)
                 from m in Gen.String[1, 100].Where(static s => !string.IsNullOrWhiteSpace(s))
                 select new SystemError(error1.Domain, c, m),
            _ => from m in Gen.String[1, 100].Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, error1.Message, StringComparison.Ordinal))
                 select new SystemError(error1.Domain, error1.Code, m),
        }
        select (error1, error2);

    /// <summary>Generates triples of identical errors for transitivity testing.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<(SystemError, SystemError, SystemError)> EqualErrorTripleGen =>
        from domain in DomainGen
        from code in CodeForDomainGen(domain)
        from message in Gen.String[1, 100].Where(static s => !string.IsNullOrWhiteSpace(s))
        let error = new SystemError(domain, code, message)
        select (error, error, error);

    /// <summary>Generates codes within specified range.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<int> CodeInRangeGen(int min, int max) => Gen.Int[min, max];

    /// <summary>Generates codes outside all defined domain ranges.</summary>
    [System.Diagnostics.Contracts.Pure]
    public static Gen<int> CodeOutsideRangesGen =>
        Gen.OneOf(
            Gen.Int[-1000, -1],
            Gen.Int[6000, 10000]);
}
