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

## Benchmarking and profiling

Use representative workloads. Microbenchmarks are useful for isolated hot paths but do not prove end-to-end improvements.

For application performance, prefer profiling, tracing, metrics, and realistic load tests where appropriate.

Compare before/after measurements using the same workload and environment as closely as practical.

## Performance review questions

- Is there evidence this is a bottleneck?
- What is the baseline?
- What resource is constrained: CPU, memory, I/O, database, network, lock contention, or external service?
- Does the change reduce the actual bottleneck?
- Does it increase complexity or operational cost?
- Does it alter correctness, ordering, consistency, or failure behavior?
- Can the simpler implementation be retained until profiling demonstrates a need?
