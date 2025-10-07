using BenchmarkDotNet.Running;

namespace MessageQueue.Performance.Tests;

/// <summary>
/// Performance benchmarks using BenchmarkDotNet
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // TODO: Add benchmark classes
        // - Enqueue throughput
        // - Checkout latency
        // - Deduplication performance
        // - Persistence overhead

        Console.WriteLine("MessageQueue Performance Tests - To be implemented");
        // BenchmarkRunner.Run<EnqueueBenchmarks>();
    }
}
