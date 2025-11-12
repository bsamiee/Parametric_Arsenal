using Arsenal.Core.Results;
using Arsenal.Tests.Common;
using CsCheck;
using Xunit;

namespace Arsenal.Core.Tests.SharedUtilities;

/// <summary>Verification tests for shared test utilities with property-based testing.</summary>
public sealed class SharedUtilitiesTests {
    /// <summary>Verifies Test.ForAll universal quantification.</summary>
    [Fact]
    public void TestAssert_ForAll_VerifiesProperty() =>
        Test.ForAll(Gen.Int, x => unchecked(x + 0) == x, iter: 50);

    /// <summary>Verifies Test.Exists finds witness existentially.</summary>
    [Fact]
    public void TestAssert_Exists_FindsWitness() =>
        Test.Exists(Gen.Int[0, 1000], x => x > 950, maxAttempts: 200);

    /// <summary>Verifies Test.Implies logical implication.</summary>
    [Fact]
    public void TestAssert_Implies_VerifiesImplication() =>
        Test.Implies(Gen.Int, x => x > 10, x => x > 0, iter: 50);

    /// <summary>Verifies Test.Compare FrozenDictionary dispatch.</summary>
    [Fact]
    public void TestAssert_Compare_VerifiesComparison() {
        Test.Compare(5, 10, "LessThan");
        Test.Compare(10, 5, "GreaterThan");
        Test.Compare(5, 5, "Equal");
    }

    /// <summary>Verifies Test.EqualWithin floating-point tolerance.</summary>
    [Fact]
    public void TestAssert_EqualWithin_VerifiesTolerance() =>
        Test.EqualWithin(1.0, 1.001, tolerance: 0.01);

    /// <summary>Verifies Test.Success Result validation.</summary>
    [Fact]
    public void TestAssert_Success_VerifiesResultSuccess() {
        Result<int> result = ResultFactory.Create(value: 42);
        Test.Success(result, value => value > 0);
    }

    /// <summary>Verifies Test.All collection predicate assertions.</summary>
    [Fact]
    public void TestAssert_All_VerifiesCollectionPredicate() =>
        Test.All([2, 4, 6, 8,], x => x % 2 == 0);

    /// <summary>Verifies Test.Count predicate counting.</summary>
    [Fact]
    public void TestAssert_Count_VerifiesPredicateCount() =>
        Test.Count([1, 2, 3, 4, 5,], x => x > 2, expectedCount: 3);

    /// <summary>Verifies Test.Increasing ordering verification.</summary>
    [Fact]
    public void TestAssert_Increasing_VerifiesOrder() =>
        Test.Increasing([1, 2, 3, 4, 5,]);

    /// <summary>Verifies TestBench.Measure performance timing.</summary>
    [Fact]
    public void TestBench_Measure_RecordsTiming() {
        TestBench.Measurement m = TestBench.Measure(() => { int x = 0; for (int i = 0; i < 100; i++) { x += i; } }, iterations: 10);
        Assert.True(m.ElapsedTicks > 0);
        Assert.Equal(10, m.Iterations);
    }

    /// <summary>Verifies TestBench.Benchmark statistical analysis.</summary>
    [Fact]
    public void TestBench_Benchmark_ComputesStatistics() {
        TestBench.Statistics stats = TestBench.Benchmark(() => { int x = 0; for (int i = 0; i < 10; i++) { x += i; } }, runs: 5, iterationsPerRun: 10);
        Assert.True(stats.Mean > 0.0);
        Assert.True(stats.Min <= stats.Median);
        Assert.True(stats.Median <= stats.Max);
        Assert.True(stats.P95 <= stats.Max);
    }

    /// <summary>Verifies TestBench.Compare performance comparison.</summary>
    [Fact]
    public void TestBench_Compare_ComputesSpeedup() {
        static void Baseline() { int sum = 0; for (int i = 0; i < 100; i++) { sum += i; } }
        static void Optimized() { int sum = 0; for (int i = 0; i < 100; i++) { sum += i; } }
        double ratio = TestBench.Compare(Baseline, Optimized, runs: 3, iterationsPerRun: 10);
        Assert.True(ratio is > 0.5 and < 2.0);
    }

    /// <summary>Verifies TestBench.Throughput operations per second.</summary>
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

    /// <summary>Verifies Test.Law category theory functor identity.</summary>
    [Fact]
    public void TestLaw_Verify_ChecksFunctorIdentity() {
        Gen<Result<int>> gen = Gen.Int.ToResult(Gen.Const(new Errors.SystemError(domain: 1, code: 1000, message: "test")), successWeight: 1, failureWeight: 0);
        Test.Law<int>("FunctorIdentity", gen, 50);
    }

    /// <summary>Verifies Test.Combine assertion composition.</summary>
    [Fact]
    public void TestAssert_Combine_ExecutesAll() {
        const int x = 2;
        Test.Combine(() => x > 1, () => 1 + 1 == 2, () => "test".Length == 4);
    }

    /// <summary>Verifies Test.ExactlyOne exclusive OR logic.</summary>
    [Fact]
    public void TestAssert_ExactlyOne_VerifiesExclusiveOr() =>
        Test.ExactlyOne(() => false, () => true, () => false);
}
