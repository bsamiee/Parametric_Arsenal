using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CsCheck;

namespace Arsenal.Tests.Common;

/// <summary>Performance benchmarking for property-based tests with zero-allocation measurements.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Shared test utilities used across test projects")]
public static class TestBench {
    /// <summary>Measurement result with timing, memory, and iteration statistics.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Measurement {
        public readonly long ElapsedTicks;
        public readonly long MemoryBytes;
        public readonly int Iterations;
        public readonly double TicksPerIteration;
        public readonly double BytesPerIteration;
        public readonly double ElapsedMilliseconds;
        public readonly double ElapsedMicroseconds;
        public readonly double MemoryKilobytes;
        public readonly double MemoryMegabytes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Measurement(long ticks, long bytes, int iterations) {
            (this.ElapsedTicks, this.MemoryBytes, this.Iterations) = (ticks, bytes, iterations);
            (this.TicksPerIteration, this.BytesPerIteration) = (iterations > 0 ? (double)ticks / iterations : 0.0, iterations > 0 ? (double)bytes / iterations : 0.0);
            this.ElapsedMilliseconds = ticks / Stopwatch.Frequency * 1000.0;
            this.ElapsedMicroseconds = ticks / Stopwatch.Frequency * 1000000.0;
            this.MemoryKilobytes = bytes / 1024.0;
            this.MemoryMegabytes = bytes / (1024.0 * 1024.0);
        }
    }

    /// <summary>Statistical summary with percentiles and variance.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Statistics {
        public readonly double Min;
        public readonly double Max;
        public readonly double Mean;
        public readonly double Median;
        public readonly double P95;
        public readonly double P99;
        public readonly double StdDev;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Statistics(double[] sorted) {
            int count = sorted.Length;
            (this.Min, this.Max) = count > 0 ? (sorted[0], sorted[count - 1]) : (0.0, 0.0);
            this.Mean = count > 0 ? CalculateMean(sorted) : 0.0;
            this.Median = count > 0 ? CalculatePercentile(sorted, 50) : 0.0;
            this.P95 = count > 0 ? CalculatePercentile(sorted, 95) : 0.0;
            this.P99 = count > 0 ? CalculatePercentile(sorted, 99) : 0.0;
            this.StdDev = count > 0 ? CalculateStdDev(sorted, this.Mean) : 0.0;
        }

        [System.Diagnostics.Contracts.Pure]
        private static double CalculateMean(double[] values) {
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++) {
                sum += values[i];
            }
            return sum / values.Length;
        }

        [System.Diagnostics.Contracts.Pure]
        private static double CalculatePercentile(double[] sorted, int percentile) {
            double index = (percentile / 100.0) * (sorted.Length - 1);
            int lowerIndex = (int)Math.Floor(index);
            int upperIndex = (int)Math.Ceiling(index);
            return lowerIndex == upperIndex
                ? sorted[lowerIndex]
                : sorted[lowerIndex] + ((sorted[upperIndex] - sorted[lowerIndex]) * (index - lowerIndex));
        }

        [System.Diagnostics.Contracts.Pure]
        private static double CalculateStdDev(double[] values, double mean) {
            double sumSquaredDiff = 0.0;
            for (int i = 0; i < values.Length; i++) {
                double diff = values[i] - mean;
                sumSquaredDiff += diff * diff;
            }
            return Math.Sqrt(sumSquaredDiff / values.Length);
        }
    }

    /// <summary>Measures execution time and memory allocation for action with optional warmup.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Measurement Measure(Action action, int iterations = 100, int warmupIterations = 10) {
        for (int w = 0; w < warmupIterations; w++) {
            action();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) {
            action();
        }
        sw.Stop();
        long memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
        return new Measurement(ticks: sw.ElapsedTicks, bytes: Math.Max(0, memoryAfter - memoryBefore), iterations: iterations);
    }

    /// <summary>Measures property-based test performance for generator and assertion.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Measurement MeasureProperty<T>(Gen<T> gen, Action<T> assertion, int iterations = 100, int warmupIterations = 10) {
        List<T> samples = new(capacity: iterations + warmupIterations);
        gen.Sample(value => { samples.Add(value); return true; }, iter: iterations + warmupIterations);
        for (int w = 0; w < warmupIterations; w++) {
            assertion(samples[w]);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = warmupIterations; i < samples.Count; i++) {
            assertion(samples[i]);
        }
        sw.Stop();
        long memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
        return new Measurement(ticks: sw.ElapsedTicks, bytes: Math.Max(0, memoryAfter - memoryBefore), iterations: iterations);
    }

    /// <summary>Runs benchmark multiple times and computes statistics.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Statistics Benchmark(Action action, int runs = 10, int iterationsPerRun = 100, int warmupIterations = 10) {
        double[] timings = new double[runs];
        for (int r = 0; r < runs; r++) {
            Measurement m = Measure(action, iterationsPerRun, warmupIterations);
            timings[r] = m.TicksPerIteration;
        }
        Array.Sort(timings);
        return new Statistics(timings);
    }

    /// <summary>Compares two actions and returns speedup ratio (positive means first is faster).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double Compare(Action baseline, Action optimized, int runs = 10, int iterationsPerRun = 100) {
        Statistics baselineStats = Benchmark(baseline, runs, iterationsPerRun);
        Statistics optimizedStats = Benchmark(optimized, runs, iterationsPerRun);
        return baselineStats.Mean / optimizedStats.Mean;
    }

    /// <summary>Detects performance regression by comparing current to baseline with threshold.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool DetectRegression(Action baseline, Action current, double regressionThreshold = 1.1, int runs = 10) =>
        Compare(baseline, current, runs) > regressionThreshold;

    /// <summary>Computes rate metric from measurement (allocation rate bytes/sec or throughput ops/sec).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Diagnostics.Contracts.Pure]
    public static double Rate(Measurement m, bool isThroughput) {
        double seconds = (double)m.ElapsedTicks / Stopwatch.Frequency;
        return seconds > 0.0 ? (isThroughput ? m.Iterations / seconds : m.MemoryBytes / seconds) : 0.0;
    }

    /// <summary>Measures allocation rate in bytes per second for action.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double AllocationRate(Action action, int iterations = 1000) =>
        Rate(Measure(action, iterations, warmupIterations: 10), isThroughput: false);

    /// <summary>Verifies action produces zero allocations.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsZeroAllocation(Action action, int iterations = 100) =>
        Measure(action, iterations, warmupIterations: 10).MemoryBytes == 0;

    /// <summary>Profiles hot path by measuring time distribution across iterations.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (double[] Timings, Statistics Stats) Profile(Action action, int iterations = 1000) {
        double[] timings = new double[iterations];
        Stopwatch sw = new();
        for (int i = 0; i < iterations; i++) {
            sw.Restart();
            action();
            sw.Stop();
            timings[i] = sw.ElapsedTicks;
        }
        Array.Sort(timings);
        return (timings, new Statistics(timings));
    }

    /// <summary>Measures throughput in operations per second for action.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static double Throughput(Action action, int iterations = 1000) =>
        Rate(Measure(action, iterations, warmupIterations: 10), isThroughput: true);

    /// <summary>Measures action with adaptive iteration count for consistent measurement duration.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Measurement MeasureAdaptive(Action action, double targetSeconds = 1.0) {
        Measurement initial = Measure(action, iterations: 10, warmupIterations: 2);
        double initialSeconds = (double)initial.ElapsedTicks / Stopwatch.Frequency;
        int iterations = initialSeconds > 0.0 ? Math.Max(10, Math.Min((int)(targetSeconds / initialSeconds * 10), 100000)) : 100;
        return Measure(action, iterations, warmupIterations: Math.Max(2, iterations / 10));
    }

    /// <summary>Compares N actions and ranks by performance (1 = fastest).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (int Rank, double Mean)[] Rank(Action[] actions, int runs = 10, int iterationsPerRun = 100) {
        (int Index, double Mean)[] results = new (int, double)[actions.Length];
        for (int i = 0; i < actions.Length; i++) {
            Statistics stats = Benchmark(actions[i], runs, iterationsPerRun);
            results[i] = (i, stats.Mean);
        }
        Array.Sort(results, static (a, b) => a.Mean.CompareTo(b.Mean));
        (int, double)[] ranked = new (int, double)[actions.Length];
        for (int r = 0; r < results.Length; r++) {
            ranked[results[r].Index] = (r + 1, results[r].Mean);
        }
        return ranked;
    }

    /// <summary>Verifies action meets performance target (max ticks per iteration).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool MeetsTarget(Action action, long maxTicksPerIteration, int runs = 5) {
        Statistics stats = Benchmark(action, runs);
        return stats.P99 <= maxTicksPerIteration;
    }

    /// <summary>Estimates scalability by measuring with varying iteration counts.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (int Iterations, double TicksPerIteration)[] Scalability(Action action, int[] iterationCounts) {
        (int, double)[] results = new (int, double)[iterationCounts.Length];
        for (int i = 0; i < iterationCounts.Length; i++) {
            int iterations = iterationCounts[i];
            Measurement m = Measure(action, iterations, warmupIterations: Math.Max(2, iterations / 10));
            results[i] = (iterations, m.TicksPerIteration);
        }
        return results;
    }
}
