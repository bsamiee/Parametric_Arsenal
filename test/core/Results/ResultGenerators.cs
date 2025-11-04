using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Core.Tests.Results;

/// <summary>CsCheck generators for Result testing.</summary>
public static class ResultGenerators {
    /// <summary>Generates Result instances with success/error distribution.</summary>
    public static Gen<Result<T>> ResultGen<T>() where T : notnull =>
        from isSuccess in Gen.Bool
        from value in GetGenForType<T>()
        from error in SystemErrorGen
        from isDeferred in Gen.Bool
        select (isSuccess, isDeferred) switch {
            (true, false) => ResultFactory.Create(value: value),
            (true, true) => ResultFactory.Create(deferred: () => ResultFactory.Create(value: value)),
            (false, false) => ResultFactory.Create<T>(error: error),
            (false, true) => ResultFactory.Create(deferred: () => ResultFactory.Create<T>(error: error))
        };

    private static Gen<T> GetGenForType<T>() where T : notnull => typeof(T) switch {
        var t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
        var t when t == typeof(string) => (Gen<T>)(object)Gen.String,
        var t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
        var t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
        _ => throw new NotSupportedException($"Type {typeof(T)} not supported in generators")
    };

    /// <summary>Creates generator for supported types using reflection.</summary>
    public static object CreateGeneratorForType(Type type) => type switch {
        var t when t == typeof(int) => Gen.Int,
        var t when t == typeof(string) => Gen.String,
        var t when t == typeof(double) => Gen.Double,
        var t when t == typeof(bool) => Gen.Bool,
        _ => throw new NotSupportedException($"Type {type} not supported in reflection-based generators")
    };

    /// <summary>Generates SystemError instances.</summary>
    public static Gen<SystemError> SystemErrorGen =>
        Gen.OneOf<ErrorDomain>(Gen.Const(ErrorDomain.Results), Gen.Const(ErrorDomain.Validation), Gen.Const(ErrorDomain.Geometry))
        .SelectMany(domain =>
        Gen.Int[1000, 9999].SelectMany(code =>
        Gen.String.Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(message => new SystemError(domain, code, message))));

    /// <summary>Generates functions for Bind operations.</summary>
    public static Gen<Func<T, Result<TResult>>> MonadicFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        from shouldSucceed in Gen.Bool
        from value in GetGenForType<TResult>()
        from error in SystemErrorGen
        select (Func<T, Result<TResult>>)(_ => shouldSucceed
            ? ResultFactory.Create(value: value)
            : ResultFactory.Create<TResult>(error: error));

    /// <summary>Generates functions for Map operations.</summary>
    public static Gen<Func<T, TResult>> PureFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        GetGenForType<TResult>().Select(value => (Func<T, TResult>)(_ => value));
}

/// <summary>Test data for ResultFactory parameter combinations.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Meziantou.Analyzer", "MA0048:File name must match type name", Justification = "Multiple classes in single file as per design specification")]
public static class ResultTestData {
    /// <summary>Parameter combinations for Create method testing.</summary>
    public static IEnumerable<object?[]> FactoryParameterCases => [
        [42, null, null, null, null, null, true],                                                           // value only
        [null, new SystemError[] { TestError }, null, null, null, null, false],                             // errors array
        [null, null, TestError, null, null, null, false],                                                   // single error
        [null, null, null, (Func<Result<int>>)(() => ResultFactory.Create(value: 42)), null, null, true],   // deferred success
        [42, null, null, null, new[] { (Predicate, TestError) }, null, false],                              // conditional validation
    ];

    private static readonly SystemError TestError = new(ErrorDomain.Results, 1001, "Test error");
    private static readonly Func<int, bool> Predicate = x => x < 0;
}
