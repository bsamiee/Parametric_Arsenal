using Arsenal.Core.Errors;
using Arsenal.Core.Results;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Algebraic test data builders using dispatch and zero duplication.</summary>
public static class TestData {
    /// <summary>Creates test case from arguments using collection expression.</summary>
    public static object[] Case(params object?[] args) => args!;

    /// <summary>Unified FromGen using delegate dispatch for arity polymorphism.</summary>
    public static IEnumerable<object[]> FromGen<T>(Gen<T> gen, Delegate mapper, int count = 10) =>
        mapper switch {
            Func<T, object[]> map => gen.Array[count].Single().Select(map),
            _ => throw new ArgumentException($"Unsupported mapper type: {mapper.GetType()}", nameof(mapper)),
        };

    /// <summary>Boolean partition using collection expression.</summary>
    public static IEnumerable<object[]> BooleanPartition => [Case(true), Case(false)];

    /// <summary>Result state partition using collection expression and pattern.</summary>
    public static IEnumerable<object[]> ResultStatePartition<T>(T successValue, SystemError failureError) =>
        [Case(ResultFactory.Create(value: successValue), true), Case(ResultFactory.Create<T>(error: failureError), false)];
}
