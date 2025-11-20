---
version: 1.0
last_updated: 2025-11-20
category: integration-testing
difficulty: advanced
target: both
prerequisites:
  - CLAUDE.md
  - AGENTS.md
  - copilot-instructions.md
  - libs/core/operations/UnifiedOperation.cs
  - libs/core/operations/OperationConfig.cs
  - libs/core/results/Result.cs
  - test/shared/Test.cs
  - test/core/Results/ResultAlgebraTests.cs
---

# Integration Testing

Design and implement integration tests for multi-module workflows verifying UnifiedOperation pipelines, Result monad composition, validation propagation, caching behavior, and cross-folder interactions.

## Task Description

Comprehensive cross-module pipeline testing. Verify UnifiedOperation chains, ConditionalWeakTable caching, Result composition, error accumulation, and cross-module interactions across libs/core and libs/rhino.

## Inputs

- **Integration Scenario**: `<<SCENARIO_DESCRIPTION>>` (e.g., "spatial indexing → topology analysis → morphology operations")
- **Modules Involved**: `<<MODULE_1>>`, `<<MODULE_2>>`, `<<MODULE_3>>`, ...
- **Test Project**: `test/core/Integration/` or `test/rhino/Integration/`

## Success Criteria

✅ Complete pipeline integration tests covering common workflows  
✅ UnifiedOperation chain verification with validation propagation  
✅ ConditionalWeakTable caching behavior validated  
✅ Cross-module Result composition and error accumulation tested  
✅ Performance characteristics measured (cache hits, execution time)  
✅ Diagnostic capture integration verified (when enabled)  
✅ All tests pass with zero warnings

## Constraints

Follow all rules in CLAUDE.md. Use test/shared/Test.cs utilities. Study UnifiedOperation.cs, Result.cs, and ResultAlgebraTests.cs for patterns.

**Core Integration Patterns**: UnifiedOperation.Apply, Result.Bind, Result.Map, ConditionalWeakTable caching, OperationConfig

## Methodology

---

### Phase 1: Integration Scenario Analysis (No Code Changes)

**Goal**: Map integration points and workflows before testing.

### 1.1 Identify Integration Workflows

**Common patterns** in Parametric Arsenal:
1. **Spatial → Analysis Pipeline**:
   - Spatial indexing (RTree) → Proximity queries → Distance analysis
   - Example: Index geometry → Find nearest → Compute field values

2. **Extraction → Processing Pipeline**:
   - Point extraction → Spatial clustering → Morphology operations
   - Example: Extract points from curves → Cluster by proximity → Apply morphology

3. **Topology → Spatial Pipeline**:
   - Topology diagnosis → Spatial indexing for repairs → Morphology healing
   - Example: Detect naked edges → Index problematic areas → Apply targeted repairs

4. **Multi-Stage Validation Pipeline**:
   - V.Standard → V.Degeneracy → V.All progressive validation
   - Example: Basic checks → Degenerate detection → Comprehensive validation

5. **Caching & Performance Pipeline**:
   - Repeated operations on same geometry with ConditionalWeakTable
   - Example: Multiple queries on same spatial index (cache hit verification)

### 1.2 Map Module Dependencies

**For each scenario**:
1. **List participating modules**: `libs/rhino/spatial/`, `libs/rhino/topology/`, etc.
2. **Identify data flow**: Input → Module1 → Intermediate → Module2 → Output
3. **Map validation points**: Where V flags apply, which E codes can occur
4. **Trace Result composition**: How Bind/Map chains connect operations
5. **Identify caching opportunities**: Same input, multiple operations

### 1.3 Study UnifiedOperation Integration

**Read** `libs/core/operations/UnifiedOperation.cs`:
- Polymorphic operation dispatch (Func signatures)
- ConditionalWeakTable caching mechanism
- OperationConfig validation modes
- Diagnostic capture integration
- Error accumulation vs fail-fast behavior

**Key patterns**:
```csharp
// Basic pipeline
Result<Step1Out> step1 = UnifiedOperation.Apply(
    input: data,
    operation: (Func<Input, Result<IReadOnlyList<Step1Out>>>)(x => Step1.Process(x)),
    config: new OperationConfig<Input, Step1Out> { ValidationMode = V.Standard });

Result<Step2Out> step2 = step1.Bind(out1 =>
    UnifiedOperation.Apply(
        input: out1,
        operation: (Func<Step1Out, Result<IReadOnlyList<Step2Out>>>)(x => Step2.Process(x)),
        config: new OperationConfig<Step1Out, Step2Out> { ValidationMode = V.Degeneracy }));

// Cache verification
Result<Output> first = UnifiedOperation.Apply(...);  // Cache miss
Result<Output> second = UnifiedOperation.Apply(...); // Cache hit (same input)
```

### Phase 2: Integration Test Design (No Code Changes)

**Goal**: Complete test plan with scenarios, assertions, and metrics.

### 2.1 Test File Organization

**Create integration test folders**:
- `test/core/Integration/` - Core infrastructure integration tests
- `test/rhino/Integration/` - Rhino module integration tests

**Test file naming**:
- `<<Module1>><<Module2>>IntegrationTests.cs` - Two-module integration
- `<<Scenario>>PipelineTests.cs` - Multi-module pipeline scenario
- `CachingIntegrationTests.cs` - ConditionalWeakTable caching verification
- `ValidationPropagationTests.cs` - V flag propagation through pipelines

### 2.2 Pipeline Test Structure

**For each integration scenario**:

1. **Setup Phase**:
   - Generate or load test data
   - Configure validation modes
   - Enable diagnostics if needed

2. **Execution Phase**:
   - Execute pipeline with Result.Bind chains
   - Track intermediate results
   - Measure performance metrics

3. **Verification Phase**:
   - Verify final output correctness
   - Check error propagation (if failure path)
   - Validate diagnostic capture (if enabled)
   - Confirm cache hits (if repeated operations)

**Template**:
```csharp
[Fact]
public void Module1_To_Module2_Pipeline() {
    // Arrange
    Input testData = CreateTestInput();
    OperationConfig<Input, Step1Out> config1 = new() {
        ValidationMode = V.Standard,
        EnableDiagnostics = true,
        OperationName = "Step1_Integration",
    };
    OperationConfig<Step1Out, FinalOut> config2 = new() {
        ValidationMode = V.Degeneracy,
        EnableDiagnostics = true,
        OperationName = "Step2_Integration",
    };

    // Act
    Result<IReadOnlyList<Step1Out>> step1 = UnifiedOperation.Apply(
        input: testData,
        operation: (Func<Input, Result<IReadOnlyList<Step1Out>>>)(x => Module1.Process(x)),
        config: config1);

    Result<IReadOnlyList<FinalOut>> final = step1.Bind(intermediate =>
        UnifiedOperation.Apply(
            input: intermediate,
            operation: (Func<IReadOnlyList<Step1Out>, Result<IReadOnlyList<FinalOut>>>)(x => Module2.Process(x)),
            config: config2));

    // Assert
    Test.Success(final, output => {
        Assert.NotEmpty(output);
        Test.All(output, item => item.IsValid);
        return true;
    });
}
```

### 2.3 Caching Test Strategy

**ConditionalWeakTable verification**:
```csharp
[Fact]
public void RepeatedOperations_UseCache() {
    // Arrange
    Input sharedInput = CreateComplexInput();
    OperationConfig<Input, Output> config = new() {
        ValidationMode = V.Standard,
        EnableDiagnostics = true,
        OperationName = "CachedOperation",
    };

    // Act - First call (cache miss)
    Stopwatch sw1 = Stopwatch.StartNew();
    Result<IReadOnlyList<Output>> first = UnifiedOperation.Apply(
        input: sharedInput,
        operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => Module.Process(x)),
        config: config);
    sw1.Stop();

    // Act - Second call (cache hit)
    Stopwatch sw2 = Stopwatch.StartNew();
    Result<IReadOnlyList<Output>> second = UnifiedOperation.Apply(
        input: sharedInput,
        operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => Module.Process(x)),
        config: config);
    sw2.Stop();

    // Assert
    Test.Success(first);
    Test.Success(second);
    Assert.Equal(first.Value.Count, second.Value.Count);
    
    // Cache hit should be significantly faster (order of magnitude)
    double speedupFactor = (double)sw1.ElapsedTicks / sw2.ElapsedTicks;
    Assert.True(speedupFactor > 10.0, $"Cache hit not faster: {speedupFactor}x speedup");
}
```

### 2.4 Validation Propagation Tests

**V flag propagation through pipeline**:
```csharp
[Theory]
[InlineData(V.None, true)]       // No validation = success
[InlineData(V.Standard, false)]  // Standard catches invalid
[InlineData(V.Degeneracy, false)] // Degeneracy catches degenerate
[InlineData(V.All, false)]        // All catches everything
public void ValidationMode_PropagatesThroughPipeline(V mode, bool expectSuccess) {
    // Arrange
    Input invalidInput = CreateInvalidInput();

    // Act
    Result<Step1Out> step1 = Module1.ProcessWithMode(invalidInput, mode);
    Result<FinalOut> final = step1.Bind(out1 => Module2.ProcessWithMode(out1, mode));

    // Assert
    _ = expectSuccess
        ? Test.Success(final)
        : Test.Failure(final, errs => errs.Any(e => e.Domain == ErrorDomain.Validation));
}
```

### 2.5 Error Accumulation Tests

**AccumulateErrors configuration**:
```csharp
[Fact]
public void ErrorAccumulation_CollectsAllErrors() {
    // Arrange
    IReadOnlyList<Input> inputs = CreateMixedInputs(); // Some valid, some invalid
    OperationConfig<Input, Output> config = new() {
        ValidationMode = V.Standard,
        AccumulateErrors = true, // Collect all errors instead of fail-fast
    };

    // Act
    Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
        input: inputs,
        operation: (IReadOnlyList<Func<Input, Result<Output>>>)inputs
            .Select(inp => (Func<Input, Result<Output>>)(x => Module.Process(inp)))
            .ToArray(),
        config: config);

    // Assert
    Test.Failure(result, errs => {
        // Should have multiple errors (one per invalid input)
        Assert.True(errs.Length > 1);
        Test.All(errs, e => e.Domain == ErrorDomain.Validation);
        return true;
    });
}
```

### Phase 3: Core Integration Tests Implementation

**Goal**: Implement tests for core infrastructure integration.

### 3.1 Result Monad Composition

**Bind chaining with validation**:
```csharp
[Fact]
public void ResultBind_ChainedOperations_PropagateValidation() {
    // Arrange
    Gen<int> inputGen = Gen.Int[0, 100];

    // Act & Assert
    inputGen.Run((Action<int>)(value => {
        Result<int> step1 = ResultFactory.Create(value: value)
            .Ensure(x => x > 0, error: E.Validation.InvalidRange.WithContext("Must be positive"));
        
        Result<string> step2 = step1.Map(x => x.ToString(CultureInfo.InvariantCulture));
        
        Result<int> step3 = step2.Bind(s => 
            int.TryParse(s, out int parsed)
                ? ResultFactory.Create(value: parsed)
                : ResultFactory.Create<int>(error: E.Validation.Failed));

        _ = value > 0
            ? Test.Success(step3, v => v == value)
            : Test.Failure(step3, errs => errs.Any(e => e.Code == E.Validation.InvalidRange.Code));
    }), 100);
}
```

**Map functor composition**:
```csharp
[Fact]
public void ResultMap_ComposedTransformations_PreserveFunctor() {
    Gen<int> gen = Gen.Int;
    Func<int, string> f = x => x.ToString(CultureInfo.InvariantCulture);
    Func<string, int> g = s => s.Length;

    gen.Run((Action<int>)(value => {
        Result<int> original = ResultFactory.Create(value: value);
        Result<int> composed = original.Map(x => g(f(x)));
        Result<int> sequential = original.Map(f).Map(g);

        Assert.Equal(composed.Value, sequential.Value);
    }), 100);
}
```

### 3.2 UnifiedOperation Dispatch

**Polymorphic operation types**:
```csharp
[Fact]
public void UnifiedOperation_PolymorphicDispatch_AllSignatures() {
    // Arrange
    Input input = CreateTestInput();
    OperationConfig<Input, Output> config = new() { ValidationMode = V.Standard };

    // Act & Assert - Test all supported Func signatures
    Test.RunAll(
        // Func<TIn, Result<IReadOnlyList<TOut>>>
        () => {
            Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
                input: input,
                operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => ProcessToList(x)),
                config: config);
            Test.Success(result);
        },
        // Func<TIn, V, Result<IReadOnlyList<TOut>>> - deferred validation
        () => {
            Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
                input: input,
                operation: (Func<Input, V, Result<IReadOnlyList<Output>>>)((x, v) => ProcessWithValidation(x, v)),
                config: config);
            Test.Success(result);
        },
        // Func<TIn, Result<TOut>> - single result
        () => {
            Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
                input: input,
                operation: (Func<Input, Result<Output>>)(x => ProcessSingle(x)),
                config: config);
            Test.Success(result, outputs => outputs.Count == 1);
        },
        // (Predicate, Operation) tuple - conditional execution
        () => {
            Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
                input: input,
                operation: ((Func<Input, bool>)(x => x.IsValid), (Func<Input, Result<IReadOnlyList<Output>>>)(x => ProcessToList(x))),
                config: config);
            Test.Success(result);
        });
}
```

### 3.3 Diagnostic Capture Integration

**Diagnostic metadata collection**:
```csharp
[Fact]
public void DiagnosticCapture_CollectsMetadata() {
    // Arrange
    Input input = CreateTestInput();
    OperationConfig<Input, Output> config = new() {
        ValidationMode = V.Standard,
        EnableDiagnostics = true,
        OperationName = "TestOperation",
    };

    // Act
    Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
        input: input,
        operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => ProcessToList(x)),
        config: config);

    // Assert
    Test.Success(result);
    
    // Verify diagnostic metadata attached
    // (Implementation depends on Diagnostics infrastructure)
    // Example: result.Diagnostics should contain operation name, validation mode, cache hit status
}
```

### Phase 4: Rhino Module Integration Tests

**Goal**: Test cross-module Rhino geometry workflows.

### 4.1 Spatial → Topology Pipeline

**Spatial indexing followed by topology analysis**:
```csharp
[Test]
public void Spatial_To_Topology_Pipeline() {
    // Arrange
    Mesh testMesh = CreateComplexMesh(vertexCount: 1000);
    
    // Act - Spatial indexing
    Result<SpatialIndex> spatialResult = Spatial.Index(
        geometry: testMesh,
        config: new SpatialConfig { ValidationMode = V.Standard });

    // Act - Topology analysis on indexed geometry
    Result<TopologyDiagnosis> topoResult = spatialResult.Bind(index =>
        Topology.DiagnoseMesh(
            mesh: testMesh,
            spatialIndex: index,
            mode: V.Degeneracy));

    // Assert
    Test.Success(spatialResult);
    Test.Success(topoResult, diagnosis => {
        Assert.That(diagnosis.NakedEdgeCount, Is.GreaterThanOrEqualTo(0));
        Assert.That(diagnosis.NonManifoldEdgeCount, Is.GreaterThanOrEqualTo(0));
        return true;
    });
}
```

### 4.2 Extraction → Clustering Pipeline

**Point extraction followed by spatial clustering**:
```csharp
[Test]
public void Extraction_To_Clustering_Pipeline() {
    // Arrange
    IReadOnlyList<Curve> curves = CreateTestCurves(count: 50);
    
    // Act - Extract points from curves
    Result<IReadOnlyList<Point3d>> extractResult = curves
        .Select(curve => Extraction.ExtractPoints(curve, count: 10))
        .Aggregate(
            ResultFactory.Create(value: (IReadOnlyList<Point3d>)[]),
            (acc, res) => acc.Bind(list => res.Map(pts => (IReadOnlyList<Point3d>)[.. list, .. pts])));

    // Act - Cluster extracted points
    Result<ClusterResult> clusterResult = extractResult.Bind(points =>
        Spatial.ClusterDBSCAN(
            points: points,
            epsilon: 5.0,
            minPoints: 3,
            validationMode: V.Standard));

    // Assert
    Test.Success(extractResult, points => points.Count == 500); // 50 curves × 10 points
    Test.Success(clusterResult, clusters => {
        Assert.That(clusters.ClusterCount, Is.GreaterThan(0));
        Test.All(clusters.Assignments, assignment => assignment >= -1); // -1 = noise
        return true;
    });
}
```

### 4.3 Morphology → Topology Pipeline

**Morphology operations followed by topology verification**:
```csharp
[Test]
public void Morphology_To_Topology_Healing_Pipeline() {
    // Arrange
    Mesh problematicMesh = CreateMeshWithIssues();
    
    // Act - Apply morphology operations
    Result<Mesh> morphResult = Morphology.ApplyOperations(
        mesh: problematicMesh,
        operations: [MorphologyOperation.Smooth, MorphologyOperation.Fill,],
        validationMode: V.Standard);

    // Act - Verify topology health
    Result<TopologyDiagnosis> topoResult = morphResult.Bind(healedMesh =>
        Topology.DiagnoseMesh(mesh: healedMesh, mode: V.All));

    // Assert
    Test.Success(morphResult);
    Test.Success(topoResult, diagnosis => {
        // Healed mesh should have fewer issues than original
        Assert.That(diagnosis.NakedEdgeCount, Is.LessThanOrEqualTo(5));
        Assert.That(diagnosis.NonManifoldEdgeCount, Is.EqualTo(0));
        return true;
    });
}
```

### 4.4 Multi-Stage Validation Pipeline

**Progressive validation through V.Standard → V.Degeneracy → V.All**:
```csharp
[Test]
public void Progressive_Validation_Pipeline() {
    // Arrange
    GeometryBase geometry = CreateGeometryWithSubtleIssues();

    // Act - V.Standard (basic checks)
    Result<GeometryAnalysis> standardResult = Analysis.Analyze(geometry, V.Standard);

    // Act - V.Degeneracy (if standard passes)
    Result<GeometryAnalysis> degeneracyResult = standardResult.Bind(_ =>
        Analysis.Analyze(geometry, V.Degeneracy));

    // Act - V.All (comprehensive if degeneracy passes)
    Result<GeometryAnalysis> comprehensiveResult = degeneracyResult.Bind(_ =>
        Analysis.Analyze(geometry, V.All));

    // Assert
    _ = geometry.IsValid switch {
        true => Test.Success(comprehensiveResult),
        false => Test.Failure(comprehensiveResult, errs =>
            errs.Any(e => e.Domain == ErrorDomain.Validation)),
    };
}
```

### Phase 5: Performance & Caching Tests

**Goal**: Verify caching behavior and performance characteristics.

### 5.1 Cache Hit Rate Measurement

**ConditionalWeakTable effectiveness**:
```csharp
[Fact]
public void Cache_HitRate_MultipleOperations() {
    // Arrange
    Input sharedInput = CreateComplexInput();
    int operationCount = 100;
    OperationConfig<Input, Output> config = new() {
        ValidationMode = V.Standard,
        EnableDiagnostics = true,
        OperationName = "CachedOp",
    };

    // Act - First operation (cache miss)
    Stopwatch firstCall = Stopwatch.StartNew();
    Result<IReadOnlyList<Output>> first = UnifiedOperation.Apply(
        input: sharedInput,
        operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => Module.Process(x)),
        config: config);
    firstCall.Stop();

    // Act - Subsequent operations (cache hits)
    Stopwatch subsequentCalls = Stopwatch.StartNew();
    for (int i = 0; i < operationCount; i++) {
        Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
            input: sharedInput,
            operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => Module.Process(x)),
            config: config);
        Test.Success(result);
    }
    subsequentCalls.Stop();

    // Assert
    Test.Success(first);
    
    // Average cache hit should be much faster than first call
    double avgCacheHitTime = (double)subsequentCalls.ElapsedTicks / operationCount;
    double speedupFactor = (double)firstCall.ElapsedTicks / avgCacheHitTime;
    
    Assert.True(speedupFactor > 50.0, 
        $"Cache hits not fast enough: {speedupFactor}x speedup (expected >50x)");
}
```

### 5.2 Memory Efficiency Tests

**ConditionalWeakTable garbage collection behavior**:
```csharp
[Fact]
public void Cache_AllowsGarbageCollection() {
    // Arrange
    WeakReference weakRef = null;
    OperationConfig<Input, Output> config = new() { ValidationMode = V.None };

    void CreateAndProcess() {
        Input input = CreateLargeInput(); // Large object
        weakRef = new WeakReference(input);
        
        Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
            input: input,
            operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => Module.Process(x)),
            config: config);
        Test.Success(result);
    }

    // Act
    CreateAndProcess();
    
    // Force garbage collection
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    // Assert - WeakReference should be dead (ConditionalWeakTable allows GC)
    Assert.False(weakRef.IsAlive, "Input should be garbage collected");
}
```

### 5.3 Parallel Operation Tests

**Concurrent operations with caching**:
```csharp
[Fact]
public void Parallel_Operations_ThreadSafe() {
    // Arrange
    IReadOnlyList<Input> inputs = Enumerable.Range(0, 100)
        .Select(_ => CreateTestInput())
        .ToArray();
    OperationConfig<Input, Output> config = new() { ValidationMode = V.Standard };

    // Act
    Result<IReadOnlyList<Output>>[] results = inputs
        .AsParallel()
        .WithDegreeOfParallelism(Environment.ProcessorCount)
        .Select(input => UnifiedOperation.Apply(
            input: input,
            operation: (Func<Input, Result<IReadOnlyList<Output>>>)(x => Module.Process(x)),
            config: config))
        .ToArray();

    // Assert
    Assert.All(results, result => Test.Success(result));
    Assert.Equal(inputs.Count, results.Length);
}
```

### Phase 6: Error Propagation Tests

**Goal**: Verify error handling through integration pipelines.

### 6.1 Fail-Fast vs Accumulation

**Compare AccumulateErrors = false vs true**:
```csharp
[Theory]
[InlineData(false, 1)] // Fail-fast: stop at first error
[InlineData(true, 3)]  // Accumulate: collect all errors
public void ErrorPropagation_FailFast_Vs_Accumulate(bool accumulate, int expectedErrorCount) {
    // Arrange
    IReadOnlyList<Input> inputs = [
        CreateValidInput(),
        CreateInvalidInput(),    // Error 1
        CreateDegenerateInput(), // Error 2
        CreateInvalidInput(),    // Error 3
    ];
    OperationConfig<Input, Output> config = new() {
        ValidationMode = V.All,
        AccumulateErrors = accumulate,
    };

    // Act
    Result<IReadOnlyList<Output>> result = UnifiedOperation.Apply(
        input: inputs,
        operation: (IReadOnlyList<Func<Input, Result<Output>>>)inputs
            .Select(inp => (Func<Input, Result<Output>>)(x => Module.Process(inp)))
            .ToArray(),
        config: config);

    // Assert
    Test.Failure(result, errs => {
        Assert.Equal(expectedErrorCount, errs.Length);
        return true;
    });
}
```

### 6.2 Error Context Propagation

**Verify error context preserved through pipeline**:
```csharp
[Fact]
public void ErrorContext_PreservedThroughPipeline() {
    // Arrange
    Input invalidInput = CreateInvalidInput();
    string expectedContext = "Integration test context";

    // Act
    Result<Step1Out> step1 = Module1.ProcessWithContext(
        invalidInput, 
        context: expectedContext);
    
    Result<FinalOut> final = step1.Bind(out1 => 
        Module2.ProcessWithContext(out1, context: expectedContext));

    // Assert
    Test.Failure(final, errs => {
        Test.Any(errs, e => e.Message.Contains(expectedContext));
        return true;
    });
}
```

### Phase 7: Final Quality Pass

**Goal**: Holistic verification of integration test quality.

### 7.1 Coverage Verification

**For each integration scenario**:
- [ ] Pipeline execution tested (all modules)
- [ ] Validation propagation verified (V flags)
- [ ] Error accumulation/fail-fast tested
- [ ] Caching behavior validated
- [ ] Diagnostic capture verified (if enabled)
- [ ] Performance characteristics measured
- [ ] Parallel execution tested (if applicable)

### 7.2 Test Execution

```bash
# Run all integration tests
dotnet test --filter "Category=Integration"

# Run specific integration test
dotnet test --filter "Name~SpatialToTopology"

# Run with performance profiling
dotnet test --logger "console;verbosity=detailed" --filter "Category=Integration"
```

## Verification

After implementation:
- Complete pipeline integration tests
- UnifiedOperation chain verification
- ConditionalWeakTable caching validated
- Cross-module Result composition tested
- Performance characteristics measured
- All tests pass with zero warnings

---

## Editing Discipline

✅ **Do**:
- Test complete workflows with multiple modules
- Verify Result.Bind chains and error propagation
- Measure caching effectiveness with timing
- Test all validation modes (None, Standard, Degeneracy, All)
- Use Test.cs utilities for assertions
- Document integration scenarios clearly

❌ **Don't**:
- Test individual modules in isolation (that's unit tests)
- Skip caching verification
- Ignore performance characteristics
- Use magic numbers for timeouts/thresholds
- Create integration tests that are too narrow

---

## Anti-Patterns to Avoid

1. **Narrow Integration**: Testing only two methods instead of complete workflows
2. **Missing Cache Tests**: Not verifying ConditionalWeakTable behavior
3. **Performance Blindness**: Not measuring timing or cache hit rates
4. **Validation Gaps**: Not testing all V flag combinations
5. **Error Path Neglect**: Only testing success paths, not failure propagation
6. **Parallel Omission**: Not testing concurrent operation safety
7. **Diagnostic Ignorance**: Not verifying diagnostic capture when enabled
