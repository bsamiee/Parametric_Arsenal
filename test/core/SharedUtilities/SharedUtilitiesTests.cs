using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.SharedUtilities;

/// <summary>Verification tests for shared test utilities demonstrating new capabilities.</summary>
public sealed class SharedUtilitiesTests {
    /// <summary>Verifies TestAssert.ForAll with simple property.</summary>
    [Fact]
    public void TestAssert_ForAll_VerifiesProperty() =>
        TestAssert.ForAll(Gen.Int, x => unchecked(x + 0) == x, iter: 50);

    /// <summary>Verifies TestAssert.Exists finds witness.</summary>
    [Fact]
    public void TestAssert_Exists_FindsWitness() =>
        TestAssert.Exists(Gen.Int[0, 1000], x => x > 950, maxAttempts: 200);

    /// <summary>Verifies TestAssert.Implies with logical implication.</summary>
    [Fact]
    public void TestAssert_Implies_VerifiesImplication() =>
        TestAssert.Implies(Gen.Int, x => x > 10, x => x > 0, iter: 50);

    /// <summary>Verifies TestAssert.Compare with FrozenDictionary dispatch.</summary>
    [Fact]
    public void TestAssert_Compare_VerifiesComparison() {
        TestAssert.Compare(5, 10, "LessThan");
        TestAssert.Compare(10, 5, "GreaterThan");
        TestAssert.Compare(5, 5, "Equal");
    }

    /// <summary>Verifies TestAssert.EqualWithin for floating-point tolerance.</summary>
    [Fact]
    public void TestAssert_EqualWithin_VerifiesTolerance() =>
        TestAssert.EqualWithin(1.0, 1.001, tolerance: 0.01);

    /// <summary>Verifies TestAssert.Success for Result validation.</summary>
    [Fact]
    public void TestAssert_Success_VerifiesResultSuccess() {
        Result<int> result = ResultFactory.Create(value: 42);
        TestAssert.Success(result, value => value > 0);
    }

    /// <summary>Verifies TestAssert.All for collection assertions.</summary>
    [Fact]
    public void TestAssert_All_VerifiesCollectionPredicate() =>
        TestAssert.All([2, 4, 6, 8,], x => x % 2 == 0);

    /// <summary>Verifies TestAssert.Count for predicate counting.</summary>
    [Fact]
    public void TestAssert_Count_VerifiesPredicateCount() =>
        TestAssert.Count([1, 2, 3, 4, 5,], x => x > 2, expectedCount: 3);

    /// <summary>Verifies TestAssert.Increasing for ordering.</summary>
    [Fact]
    public void TestAssert_Increasing_VerifiesOrder() =>
        TestAssert.Increasing([1, 2, 3, 4, 5,]);

    /// <summary>Verifies TestBench.Measure for performance measurement.</summary>
    [Fact]
    public void TestBench_Measure_RecordsTiming() {
        TestBench.Measurement m = TestBench.Measure(() => { int x = 0; for (int i = 0; i < 100; i++) { x += i; } }, iterations: 10);
        Assert.True(m.ElapsedTicks > 0);
        Assert.True(m.Iterations == 10);
    }

    /// <summary>Verifies TestBench.Benchmark for statistical analysis.</summary>
    [Fact]
    public void TestBench_Benchmark_ComputesStatistics() {
        TestBench.Statistics stats = TestBench.Benchmark(() => { int x = 0; for (int i = 0; i < 10; i++) { x += i; } }, runs: 5, iterationsPerRun: 10);
        Assert.True(stats.Mean > 0.0);
        Assert.True(stats.Min <= stats.Median);
        Assert.True(stats.Median <= stats.Max);
        Assert.True(stats.P95 <= stats.Max);
    }

    /// <summary>Verifies TestBench.Compare for performance comparison.</summary>
    [Fact]
    public void TestBench_Compare_ComputesSpeedup() {
        static void Baseline() { int sum = 0; for (int i = 0; i < 100; i++) { sum += i; } }
        static void Optimized() { int sum = 0; for (int i = 0; i < 100; i++) { sum += i; } }
        double ratio = TestBench.Compare(Baseline, Optimized, runs: 3, iterationsPerRun: 10);
        Assert.True(ratio is > 0.5 and < 2.0);
    }

    /// <summary>Verifies TestBench.Throughput for ops/sec measurement.</summary>
    [Fact]
    public void TestBench_Throughput_MeasuresOpsPerSecond() {
        double throughput = TestBench.Throughput(() => { int x = 0; for (int i = 0; i < 10; i++) { x += i; } }, iterations: 100);
        Assert.True(throughput > 0.0);
    }

    /// <summary>Verifies Test.RunAll executes all assertions.</summary>
    [Fact]
    public void TestGen_RunAll_ExecutesAllAssertions() {
        int count = 0;
        Test.RunAll(
            () => count++,
            () => count++,
            () => count++);
        Assert.Equal(3, count);
    }

    /// <summary>Verifies Test.Law for category theory laws.</summary>
    [Fact]
    public void TestLaw_Verify_ChecksFunctorIdentity() {
        Gen<Result<int>> gen = Gen.Int.ToResult(Gen.Const(new Arsenal.Core.Errors.SystemError(Arsenal.Core.Errors.ErrorDomain.Results, 1000, "test")), successWeight: 1, failureWeight: 0);
        Test.Law<int>("FunctorIdentity", gen, 50);
    }

    /// <summary>Verifies TestAssert.Combine for assertion composition.</summary>
    [Fact]
    public void TestAssert_Combine_ExecutesAll() =>
        TestAssert.Combine(() => true, () => 1 + 1 == 2, () => "test".Length == 4);

    /// <summary>Verifies TestAssert.ExactlyOne for exclusive OR.</summary>
    [Fact]
    public void TestAssert_ExactlyOne_VerifiesExclusiveOr() =>
        TestAssert.ExactlyOne(() => false, () => true, () => false);
}
