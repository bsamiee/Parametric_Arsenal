using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Core.Tests.Results;

/// <summary>CsCheck generators for Result testing with comprehensive coverage.</summary>
public static class ResultGenerators {
    /// <summary>Generates Result instances with success/error/deferred distribution.</summary>
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

    /// <summary>Generates successful Results only.</summary>
    public static Gen<Result<T>> SuccessGen<T>() where T : notnull =>
        from value in GetGenForType<T>()
        from isDeferred in Gen.Bool
        select isDeferred
            ? ResultFactory.Create(deferred: () => ResultFactory.Create(value: value))
            : ResultFactory.Create(value: value);

    /// <summary>Generates failed Results only.</summary>
    public static Gen<Result<T>> FailureGen<T>() where T : notnull =>
        from error in SystemErrorGen
        from isDeferred in Gen.Bool
        select isDeferred
            ? ResultFactory.Create(deferred: () => ResultFactory.Create<T>(error: error))
            : ResultFactory.Create<T>(error: error);

    /// <summary>Generates nested Result instances for testing flatten operations.</summary>
    public static Gen<Result<Result<T>>> NestedResultGen<T>() where T : notnull =>
        from outerSuccess in Gen.Bool
        from innerResult in ResultGen<T>()
        from outerError in SystemErrorGen
        select outerSuccess
            ? ResultFactory.Create(value: innerResult)
            : ResultFactory.Create<Result<T>>(error: outerError);

    /// <summary>Generates Result containing collections for traversal testing.</summary>
    public static Gen<Result<IEnumerable<T>>> CollectionResultGen<T>() where T : notnull =>
        from items in GetGenForType<T>().List[0, 10]
        from isSuccess in Gen.Bool
        from error in SystemErrorGen
        select isSuccess
            ? ResultFactory.Create<IEnumerable<T>>(value: items)
            : ResultFactory.Create<IEnumerable<T>>(error: error);

    /// <summary>Generates collections of Results for aggregate testing.</summary>
    public static Gen<IEnumerable<Result<T>>> ResultCollectionGen<T>() where T : notnull =>
        ResultGen<T>().List[0, 10].Select(list => (IEnumerable<Result<T>>)list);

    private static Gen<T> GetGenForType<T>() where T : notnull => typeof(T) switch {
        var t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
        var t when t == typeof(string) => (Gen<T>)(object)Gen.String.Where(s => s is not null),
        var t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
        var t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
        _ => throw new NotSupportedException($"Type {typeof(T)} not supported in generators")
    };

    /// <summary>Generates SystemError instances across all domains.</summary>
    public static Gen<SystemError> SystemErrorGen =>
        Gen.OneOf<ErrorDomain>(Gen.Const(ErrorDomain.Results), Gen.Const(ErrorDomain.Validation), Gen.Const(ErrorDomain.Geometry))
        .SelectMany(domain =>
        Gen.Int[1000, 9999].SelectMany(code =>
        Gen.String.Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(message => new SystemError(domain, code, message))));

    /// <summary>Generates arrays of SystemErrors for multi-error testing.</summary>
    public static Gen<SystemError[]> SystemErrorArrayGen =>
        SystemErrorGen.List[1, 5].Select(list => list.ToArray());

    /// <summary>Generates functions for Bind operations with controlled success/failure distribution.</summary>
    public static Gen<Func<T, Result<TResult>>> MonadicFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        from shouldSucceed in Gen.Bool
        from value in GetGenForType<TResult>()
        from error in SystemErrorGen
        select (Func<T, Result<TResult>>)(_ => shouldSucceed
            ? ResultFactory.Create(value: value)
            : ResultFactory.Create<TResult>(error: error));

    /// <summary>Generates pure functions for Map operations.</summary>
    public static Gen<Func<T, TResult>> PureFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        GetGenForType<TResult>().Select(value => (Func<T, TResult>)(_ => value));

    /// <summary>Generates predicate functions for Filter/Ensure operations.</summary>
    public static Gen<Func<T, bool>> PredicateGen<T>() where T : notnull =>
        Gen.Bool.Select(result => (Func<T, bool>)(_ => result));

    /// <summary>Generates validation tuples for Ensure operations.</summary>
    public static Gen<(Func<T, bool>, SystemError)[]> ValidationArrayGen<T>() where T : notnull =>
        from validations in Gen.Select(PredicateGen<T>(), SystemErrorGen).List[1, 5]
        select validations.ToArray();

    /// <summary>Generates functions that lift into Result context for Lift testing.</summary>
    public static Gen<Func<int, int, int>> BinaryFunctionGen =>
        Gen.OneOf(
            Gen.Const<Func<int, int, int>>((a, b) => a + b),
            Gen.Const<Func<int, int, int>>((a, b) => a * b),
            Gen.Const<Func<int, int, int>>((a, b) => a - b));
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
