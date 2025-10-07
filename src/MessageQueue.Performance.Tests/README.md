# MessageQueue.Performance.Tests

## Purpose

Performance benchmarks using BenchmarkDotNet to measure throughput, latency, and resource usage.

## Benchmark Categories

- **Benchmarks/** - Performance tests
  - Enqueue throughput (messages/second)
  - Checkout latency (microseconds)
  - Deduplication overhead
  - Persistence impact on throughput
  - Memory allocation per operation
  - Handler dispatch latency

## Key Metrics

- **Throughput** - Messages processed per second
- **Latency** - P50, P95, P99 latencies
- **Memory** - Allocations per operation
- **Concurrency** - Scalability with core count

## Running Benchmarks

```bash
# Run all benchmarks
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release

# Run specific benchmark
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release --filter "*EnqueueBenchmark*"

# Export results
dotnet run --project src/MessageQueue.Performance.Tests/MessageQueue.Performance.Tests.csproj -c Release --exporters json
```

## Output

Results are saved to `BenchmarkDotNet.Artifacts/` with detailed reports in HTML, JSON, and Markdown formats.

## Phase

**Phase 7** - Hardening & Release (Weeks 7-8, Days 43-56)
