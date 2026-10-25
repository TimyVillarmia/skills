# Performance

Use this reference when performance is part of the requirement, a regression is suspected, or profiling indicates a bottleneck.

## Measurement first

Do not optimize based on intuition alone.

Use this loop:

```text
Baseline -> Identify bottleneck -> Make the smallest useful change
-> Measure again -> Validate correctness and complexity
```

Define the metric before optimizing: latency, throughput, allocation rate, CPU time, memory, database duration, request volume, or another observable target.

## CPU and allocations

- Prefer appropriate algorithms and data structures before micro-optimizations.
- Avoid unnecessary allocations in hot paths.
- Be aware of boxing, repeated string operations, temporary collections, and excessive LINQ materialization when profiling shows allocation pressure.
- Use `FrozenDictionary<TKey, TValue>` and `FrozenSet<T>` (.NET 8+) for immutable lookup tables constructed once at startup; they provide faster reads than standard dictionaries.
- Use `SearchValues<T>` (.NET 8/9+) for vectorized, SIMD-accelerated character, byte, and substring matching.
- Use the `[LoggerMessage]` source generator rather than string interpolation or un-templated `ILogger` calls on frequently executed code paths.
- Use spans, pooling, specialized collections, or lower-level APIs only when measurement demonstrates their value and the resulting complexity is justified.

Readable code is normally preferable outside measured hot paths.

## Async and I/O

- Use asynchronous APIs for asynchronous I/O.
- Avoid blocking on tasks with `.Result`, `.Wait()`, or equivalent patterns.
- Do not create asynchronous wrappers around inherently synchronous work without a real benefit.
- Propagate cancellation through long-running or externally bounded operations.
- Avoid unnecessary concurrency; parallelizing a bottleneck is useful only when the underlying resource can sustain it.

## Database performance

For EF Core/database bottlenecks, follow the main skill's routing and read the EF Core reference alongside this one.

Start with query shape, result size, indexes, round trips, tracking/materialization, and database execution plans before introducing application-side caching or complex optimizations.

## HTTP and distributed systems

Reduce unnecessary network calls before optimizing serialization details. Consider:

- batching
- appropriate caching
- compression where beneficial
- connection reuse
- payload size
- timeouts
- retries and backoff
- idempotency

Retries can multiply load and latency. Do not add them blindly.

## Serialization

Measure serialization and payload costs before replacing standard serializers or introducing custom formats.

Avoid serializing data that clients do not need. Keep public DTOs intentional rather than exposing persistence graphs.

## Caching

Caching is a consistency and invalidation decision, not merely a performance toggle.

For .NET 9+, prefer **`HybridCache`** (`Microsoft.Extensions.Caching.Hybrid`) for application-level data caching. It provides:
- Two-tier caching (fast in-process L1 + distributed L2 such as Redis) in a single unified API.
- Automatic cache stampede protection (concurrent callers await the same factory execution rather than racing).
- Native cancellation, tag-based eviction, and serialization support.

Before caching, identify:

- what is expensive
- whether the data can become stale
- acceptable staleness
- cache scope
- eviction strategy
- invalidation behavior
- memory/distributed cache cost
- failure behavior when the cache is unavailable

Do not cache a cheap operation merely because caching appears faster in a microbenchmark.

## Concurrency

Concurrency can improve throughput for independent I/O-bound operations, but it can also increase contention, resource consumption, database pressure, and tail latency.

Bound concurrency when fan-out can grow with input size.

Do not use `Task.Run` as a generic performance fix for server-side request handling or I/O-bound work.

## Benchmarking with BenchmarkDotNet

Use BenchmarkDotNet for isolated microbenchmarks on CPU-bound and allocation-sensitive hot paths (e.g. parsers, serializers, string manipulations, collections).

### When to use
- Measuring relative throughput or allocation diffs between algorithm alternatives.
- Verifying zero-allocation claims on critical execution paths.
- Do not use BenchmarkDotNet for database operations, network I/O, or end-to-end HTTP pipelines; use profiling and load testing instead.

### Common mistakes to avoid
1. **Running in Debug mode:** BenchmarkDotNet requires a Release build (`dotnet run -c Release`) to measure JIT-optimized code and inlining.
2. **Omitting `[MemoryDiagnoser]`:** Always decorate benchmark classes with `[MemoryDiagnoser]` to track GC collections and allocated bytes.
3. **Dead-code elimination:** The JIT eliminates computations whose results are never read. Always return benchmark results from the method or pass them to a consumer.
4. **Mutating shared state:** Ensure benchmarks are idempotent; do not let iteration $N$ modify state that affects iteration $N+1$.

### How to read results
- **Mean / Median:** Average execution time. When Mean and Median diverge materially, inspect distribution skew.
- **Error / StdDev:** Half of the 99.9% confidence interval and standard deviation. Wide error relative to the mean indicates environment noise or inconsistent execution.
- **Allocated:** Managed memory allocated per operation. A 0 B allocation confirms zero-allocation behavior.

```csharp
[MemoryDiagnoser]
public class ParserBenchmarks
{
    private string _payload = "id=123&status=active";

    [Benchmark(Baseline = true)]
    public Dictionary<string, string> ParseStandard() => ParseViaSplit(_payload);

    [Benchmark]
    public ParsedSpanResult ParseOptimized() => ParseViaSpan(_payload.AsSpan());
}
```

## Performance review questions

- Is there evidence this is a bottleneck?
- What is the baseline?
- What resource is constrained: CPU, memory, I/O, database, network, lock contention, or external service?
- Does the change reduce the actual bottleneck?
- Does it increase complexity or operational cost?
- Does it alter correctness, ordering, consistency, or failure behavior?
- Can the simpler implementation be retained until profiling demonstrates a need?
