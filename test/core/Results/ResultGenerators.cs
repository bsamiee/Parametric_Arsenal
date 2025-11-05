using System.Globalization;
using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;

namespace Arsenal.Core.Tests.Results;

/// <summary>Algebraic generators using zero-allocation static lambdas, inline type dispatch, and modern C# patterns.</summary>
public static class ResultGenerators {
    /// <summary>Generates SystemError using LINQ composition with static lambdas.</summary>
    public static Gen<SystemError> SystemErrorGen =>
        from domain in Gen.OneOf(Gen.Const(ErrorDomain.Results), Gen.Const(ErrorDomain.Validation), Gen.Const(ErrorDomain.Geometry))
        from code in Gen.Int[1000, 9999]
        from message in Gen.String.Matching(static s => !string.IsNullOrWhiteSpace(s))
        select new SystemError(domain, code, message);

    /// <summary>Generates SystemError arrays using zero-allocation conversion.</summary>
    public static Gen<SystemError[]> SystemErrorArrayGen => SystemErrorGen.List[1, 5].Select(static list => list.ToArray());

    /// <summary>Generates Result with algebraic state distribution (2×2 matrix: success/failure × immediate/deferred).</summary>
    public static Gen<Result<T>> ResultGen<T>() where T : notnull => (typeof(T) switch {
        Type t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
        Type t when t == typeof(string) => (Gen<T>)(object)Gen.String.Matching(static s => s is not null),
        Type t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
        Type t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
        _ => throw new NotSupportedException($"Type {typeof(T)} not supported"),
    }).ToResultGenDeferred(SystemErrorGen);

    /// <summary>Generates successful Results with immediate/deferred distribution using inline type dispatch.</summary>
    public static Gen<Result<T>> SuccessGen<T>() where T : notnull => GenEx.OneOfWeighted(
        (1, (typeof(T) switch {
            Type t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
            Type t when t == typeof(string) => (Gen<T>)(object)Gen.String.Matching(static s => s is not null),
            Type t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
            Type t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
            _ => throw new NotSupportedException($"Type {typeof(T)} not supported"),
        }).Select(static v => ResultFactory.Create(value: v))),
        (1, (typeof(T) switch {
            Type t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
            Type t when t == typeof(string) => (Gen<T>)(object)Gen.String.Matching(static s => s is not null),
            Type t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
            Type t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
            _ => throw new NotSupportedException($"Type {typeof(T)} not supported"),
        }).Select(static v => ResultFactory.Create(deferred: () => ResultFactory.Create(value: v)))));

    /// <summary>Generates failed Results with immediate/deferred distribution.</summary>
    public static Gen<Result<T>> FailureGen<T>() where T : notnull => GenEx.OneOfWeighted(
        (1, SystemErrorGen.Select(static e => ResultFactory.Create<T>(error: e))),
        (1, SystemErrorGen.Select(static e => ResultFactory.Create(deferred: () => ResultFactory.Create<T>(error: e)))));

    /// <summary>Generates nested Result using recursive composition.</summary>
    public static Gen<Result<Result<T>>> NestedResultGen<T>() where T : notnull => ResultGen<T>().ToResultGen(SystemErrorGen);

    /// <summary>Generates Result containing collections using monadic map and inline type dispatch.</summary>
    public static Gen<Result<IEnumerable<T>>> CollectionResultGen<T>() where T : notnull => (typeof(T) switch {
        Type t when t == typeof(int) => (Gen<T>)(object)Gen.Int,
        Type t when t == typeof(string) => (Gen<T>)(object)Gen.String.Matching(static s => s is not null),
        Type t when t == typeof(double) => (Gen<T>)(object)Gen.Double,
        Type t when t == typeof(bool) => (Gen<T>)(object)Gen.Bool,
        _ => throw new NotSupportedException($"Type {typeof(T)} not supported"),
    }).List[0, 10].ToResultGen(SystemErrorGen).Select(static r => r.Map(static list => (IEnumerable<T>)list));

    /// <summary>Generates collections of Results using static cast.</summary>
    public static Gen<IEnumerable<Result<T>>> ResultCollectionGen<T>() where T : notnull => ResultGen<T>().List[0, 10].Select(static list => (IEnumerable<Result<T>>)list);

    /// <summary>Generates monadic functions with actual transformations using value-dependent logic.</summary>
    public static Gen<Func<T, Result<TResult>>> MonadicFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        (typeof(T), typeof(TResult)) switch {
            (Type t, Type r) when t == typeof(int) && r == typeof(string) =>
                Gen.Int.Tuple(Gen.Bool).Select(static (offset, succeeds) =>
                    (Func<T, Result<TResult>>)(object)new Func<int, Result<string>>(x =>
                        succeeds ? ResultFactory.Create(value: (x + offset).ToString(CultureInfo.InvariantCulture))
                                 : ResultFactory.Create<string>(error: new SystemError(ErrorDomain.Results, 9001, string.Create(CultureInfo.InvariantCulture, $"Failed at {x}"))))),
            (Type t, Type r) when t == typeof(int) && r == typeof(double) =>
                Gen.Double.Tuple(Gen.Bool).Select(static (multiplier, succeeds) =>
                    (Func<T, Result<TResult>>)(object)new Func<int, Result<double>>(x =>
                        succeeds ? ResultFactory.Create(value: x * multiplier) : ResultFactory.Create<double>(error: new SystemError(ErrorDomain.Results, 9002, "Transform failed")))),
            (Type t, Type r) when t == typeof(string) && r == typeof(double) =>
                Gen.Double.Tuple(Gen.Bool).Select(static (offset, succeeds) =>
                    (Func<T, Result<TResult>>)(object)new Func<string, Result<double>>(s =>
                        succeeds && double.TryParse(s, CultureInfo.InvariantCulture, out double val) ? ResultFactory.Create(value: val + offset)
                                                                                                       : ResultFactory.Create<double>(error: new SystemError(ErrorDomain.Results, 9003, "Parse failed")))),
            (Type t, Type r) when t == typeof(string) && r == typeof(int) =>
                Gen.Int.Tuple(Gen.Bool).Select(static (offset, succeeds) =>
                    (Func<T, Result<TResult>>)(object)new Func<string, Result<int>>(s =>
                        succeeds ? ResultFactory.Create(value: s.Length + offset) : ResultFactory.Create<int>(error: new SystemError(ErrorDomain.Results, 9004, "Length calc failed")))),
            _ => SystemErrorGen.Select(err => new Func<T, Result<TResult>>(_ => ResultFactory.Create<TResult>(error: err))),
        };

    /// <summary>Generates pure functions with actual transformations using value-dependent operations.</summary>
    public static Gen<Func<T, TResult>> PureFunctionGen<T, TResult>() where T : notnull where TResult : notnull =>
        (typeof(T), typeof(TResult)) switch {
            (Type t, Type r) when t == typeof(int) && r == typeof(string) =>
                Gen.Int.Select(static offset => (Func<T, TResult>)(object)new Func<int, string>(x => (x + offset).ToString(CultureInfo.InvariantCulture))),
            (Type t, Type r) when t == typeof(int) && r == typeof(double) =>
                Gen.Double.Select(static multiplier => (Func<T, TResult>)(object)new Func<int, double>(x => x * multiplier)),
            (Type t, Type r) when t == typeof(int) && r == typeof(int) =>
                Gen.Int.Select(static offset => (Func<T, TResult>)(object)new Func<int, int>(x => x + offset)),
            (Type t, Type r) when t == typeof(string) && r == typeof(int) =>
                Gen.Int.Select(static offset => (Func<T, TResult>)(object)new Func<string, int>(s => s.Length + offset)),
            _ => throw new NotSupportedException(string.Create(CultureInfo.InvariantCulture, $"Pure function generation not supported for {typeof(T)} -> {typeof(TResult)}")),
        };

    /// <summary>Generates predicates using constant boolean capture.</summary>
    public static Gen<Func<T, bool>> PredicateGen<T>() where T : notnull => Gen.Bool.SelectMany(result => Gen.Const<Func<T, bool>>(_ => result));

    /// <summary>Generates validation arrays using Cartesian composition.</summary>
    public static Gen<(Func<T, bool>, SystemError)[]> ValidationArrayGen<T>() where T : notnull => PredicateGen<T>().Tuple(SystemErrorGen).List[1, 5].Select(static list => list.ToArray());

    /// <summary>Generates binary operations using static function set.</summary>
    public static Gen<Func<int, int, int>> BinaryFunctionGen => Gen.OneOf(
        Gen.Const<Func<int, int, int>>(static (a, b) => a + b),
        Gen.Const<Func<int, int, int>>(static (a, b) => a * b),
        Gen.Const<Func<int, int, int>>(static (a, b) => a - b));
}
