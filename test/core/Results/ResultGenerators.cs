using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;

namespace Arsenal.Core.Tests.Results;

/// <summary>Algebraic CsCheck generators for Result testing using composition and pattern matching.</summary>
public static class ResultGenerators {
    private static Gen<T> GetGenForType<T>() where T : notnull => typeof(T) switch {
        var t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
        var t when t == typeof(string) => (Gen<T>)(object)Gen.String.Matching(s => s is not null),
        var t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
        var t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
        _ => throw new NotSupportedException($"Type {typeof(T)} not supported in generators")
    };

    /// <summary>Generates SystemError instances using algebraic domain composition.</summary>
    public static Gen<SystemError> SystemErrorGen =>
        from domain in Gen.OneOf(Gen.Const(ErrorDomain.Results), Gen.Const(ErrorDomain.Validation), Gen.Const(ErrorDomain.Geometry))
        from code in Gen.Int[1000, 9999]
        from message in Gen.String.Matching(s => !string.IsNullOrWhiteSpace(s))
        select new SystemError(domain, code, message);

    /// <summary>Generates SystemError arrays for error accumulation testing.</summary>
    public static Gen<SystemError[]> SystemErrorArrayGen =>
        SystemErrorGen.List[1, 5].Select(list => list.ToArray());

    /// <summary>Generates Result instances using algebraic state distribution (success/failure × immediate/deferred).</summary>
    public static Gen<Result<T>> ResultGen<T>() where T : notnull =>
        GetGenForType<T>().ToResultGenDeferred(SystemErrorGen);

    /// <summary>Generates successful Results using algebraic composition.</summary>
    public static Gen<Result<T>> SuccessGen<T>() where T : notnull =>
        GenEx.OneOfWeighted(
            (1, GetGenForType<T>().Select(v => ResultFactory.Create(value: v))),
            (1, GetGenForType<T>().Select(v => ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)))));

    /// <summary>Generates failed Results using algebraic composition.</summary>
    public static Gen<Result<T>> FailureGen<T>() where T : notnull =>
        GenEx.OneOfWeighted(
            (1, SystemErrorGen.Select(e => ResultFactory.Create<T>(error: e))),
            (1, SystemErrorGen.Select(e => ResultFactory.Create(deferred: () => ResultFactory.Create<T>(error: e)))));

    /// <summary>Generates nested Result instances using recursive composition.</summary>
    public static Gen<Result<Result<T>>> NestedResultGen<T>() where T : notnull =>
        ResultGen<T>().ToResultGen(SystemErrorGen);

    /// <summary>Generates Result containing collections using list composition.</summary>
    public static Gen<Result<IEnumerable<T>>> CollectionResultGen<T>() where T : notnull =>
        GetGenForType<T>().List[0, 10].ToResultGen(SystemErrorGen)
            .Select(r => r.Map(list => (IEnumerable<T>)list));

    /// <summary>Generates collections of Results for aggregation testing.</summary>
    public static Gen<IEnumerable<Result<T>>> ResultCollectionGen<T>() where T : notnull =>
        ResultGen<T>().List[0, 10].Select(list => (IEnumerable<Result<T>>)list);

    /// <summary>Generates monadic functions using algebraic success/failure distribution.</summary>
    public static Gen<Func<T, Result<TResult>>> MonadicFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        GetGenForType<TResult>().ToResultGen(SystemErrorGen)
            .Select<Func<T, Result<TResult>>>(result => _ => result);

    /// <summary>Generates pure functions using value mapping.</summary>
    public static Gen<Func<T, TResult>> PureFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        GetGenForType<TResult>().Select<Func<T, TResult>>(value => _ => value);

    /// <summary>Generates predicate functions using boolean distribution.</summary>
    public static Gen<Func<T, bool>> PredicateGen<T>() where T : notnull =>
        Gen.Bool.Select<Func<T, bool>>(result => _ => result);

    /// <summary>Generates validation tuples using Cartesian product composition.</summary>
    public static Gen<(Func<T, bool>, SystemError)[]> ValidationArrayGen<T>() where T : notnull =>
        PredicateGen<T>().Tuple(SystemErrorGen).List[1, 5].Select(list => list.ToArray());

    /// <summary>Generates binary operations using algebraic operation set.</summary>
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
