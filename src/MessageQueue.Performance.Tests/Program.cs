// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace MessageQueue.Performance.Tests;

/// <summary>
/// Performance benchmarks using BenchmarkDotNet
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--all")
        {
            // Run all benchmarks
            BenchmarkRunner.Run<EnqueueBenchmarks>();
            BenchmarkRunner.Run<CheckoutBenchmarks>();
            BenchmarkRunner.Run<PersistenceBenchmarks>();
            BenchmarkRunner.Run<EndToEndBenchmarks>();
        }
        else if (args.Length > 0)
        {
            // Run specific benchmark by name
            var benchmarkType = args[0].ToLowerInvariant() switch
            {
                "enqueue" => typeof(EnqueueBenchmarks),
                "checkout" => typeof(CheckoutBenchmarks),
                "persistence" => typeof(PersistenceBenchmarks),
                "endtoend" => typeof(EndToEndBenchmarks),
                _ => null
            };

            if (benchmarkType != null)
            {
                BenchmarkRunner.Run(benchmarkType);
            }
            else
            {
                PrintUsage();
            }
        }
        else
        {
            // Default: run enqueue benchmarks
            BenchmarkRunner.Run<EnqueueBenchmarks>();
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MessageQueue Performance Tests");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run [benchmark]");
        Console.WriteLine();
        Console.WriteLine("Available benchmarks:");
        Console.WriteLine("  enqueue      - Message enqueue throughput benchmarks");
        Console.WriteLine("  checkout     - Message checkout latency benchmarks");
        Console.WriteLine("  persistence  - Persistence overhead benchmarks");
        Console.WriteLine("  endtoend     - End-to-end producer-consumer benchmarks");
        Console.WriteLine("  --all        - Run all benchmarks");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run enqueue");
        Console.WriteLine("  dotnet run --all");
    }
}
