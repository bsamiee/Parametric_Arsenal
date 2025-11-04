using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;

namespace Arsenal.Core.Tests.Results;

/// <summary>Algebraic generators using zero-allocation static lambdas and modern C# patterns.</summary>
public static class ResultGenerators {
    private static Gen<T> GetGenForType<T>() where T : notnull => typeof(T) switch {
        var t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
        var t when t == typeof(string) => (Gen<T>)(object)Gen.String.Matching(static s => s is not null),
        var t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
        var t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
        _ => throw new NotSupportedException($"Type {typeof(T)} not supported")
    };

    /// <summary>Generates SystemError using LINQ composition with static lambdas.</summary>
    public static Gen<SystemError> SystemErrorGen =>
        from domain in Gen.OneOf(Gen.Const(ErrorDomain.Results), Gen.Const(ErrorDomain.Validation), Gen.Const(ErrorDomain.Geometry))
        from code in Gen.Int[1000, 9999]
        from message in Gen.String.Matching(static s => !string.IsNullOrWhiteSpace(s))
        select new SystemError(domain, code, message);

    /// <summary>Generates SystemError arrays using zero-allocation conversion.</summary>
    public static Gen<SystemError[]> SystemErrorArrayGen => SystemErrorGen.List[1, 5].Select(static list => list.ToArray());

    /// <summary>Generates Result with algebraic state distribution (2×2 matrix: success/failure × immediate/deferred).</summary>
    public static Gen<Result<T>> ResultGen<T>() where T : notnull => GetGenForType<T>().ToResultGenDeferred(SystemErrorGen);

    /// <summary>Generates successful Results with immediate/deferred distribution.</summary>
    public static Gen<Result<T>> SuccessGen<T>() where T : notnull =>
        GenEx.OneOfWeighted(
            (1, GetGenForType<T>().Select(static v => ResultFactory.Create(value: v))),
            (1, GetGenForType<T>().Select(static v => ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)))));

    /// <summary>Generates failed Results with immediate/deferred distribution.</summary>
    public static Gen<Result<T>> FailureGen<T>() where T : notnull =>
        GenEx.OneOfWeighted(
            (1, SystemErrorGen.Select(static e => ResultFactory.Create<T>(error: e))),
            (1, SystemErrorGen.Select(static e => ResultFactory.Create(deferred: () => ResultFactory.Create<T>(error: e)))));

    /// <summary>Generates nested Result using recursive composition.</summary>
    public static Gen<Result<Result<T>>> NestedResultGen<T>() where T : notnull => ResultGen<T>().ToResultGen(SystemErrorGen);

    /// <summary>Generates Result containing collections using monadic map.</summary>
    public static Gen<Result<IEnumerable<T>>> CollectionResultGen<T>() where T : notnull =>
        GetGenForType<T>().List[0, 10].ToResultGen(SystemErrorGen).Select(static r => r.Map(static list => (IEnumerable<T>)list));

    /// <summary>Generates collections of Results using static cast.</summary>
    public static Gen<IEnumerable<Result<T>>> ResultCollectionGen<T>() where T : notnull =>
        ResultGen<T>().List[0, 10].Select(static list => (IEnumerable<Result<T>>)list);

    /// <summary>Generates monadic functions using constant result capture.</summary>
    public static Gen<Func<T, Result<TResult>>> MonadicFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        GetGenForType<TResult>().ToResultGen(SystemErrorGen).Select<Func<T, Result<TResult>>>(static result => _ => result);

    /// <summary>Generates pure functions using constant value capture.</summary>
    public static Gen<Func<T, TResult>> PureFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        GetGenForType<TResult>().Select<Func<T, TResult>>(static value => _ => value);

    /// <summary>Generates predicates using constant boolean capture.</summary>
    public static Gen<Func<T, bool>> PredicateGen<T>() where T : notnull =>
        Gen.Bool.Select<Func<T, bool>>(static result => _ => result);

    /// <summary>Generates validation arrays using Cartesian composition.</summary>
    public static Gen<(Func<T, bool>, SystemError)[]> ValidationArrayGen<T>() where T : notnull =>
        PredicateGen<T>().Tuple(SystemErrorGen).List[1, 5].Select(static list => list.ToArray());

    /// <summary>Generates binary operations using static function set.</summary>
    public static Gen<Func<int, int, int>> BinaryFunctionGen => Gen.OneOf(
        Gen.Const<Func<int, int, int>>(static (a, b) => a + b),
        Gen.Const<Func<int, int, int>>(static (a, b) => a * b),
        Gen.Const<Func<int, int, int>>(static (a, b) => a - b));
}
