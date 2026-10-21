using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    const long M = 50_000_000;
    const int Stride = 16; // 16 * 8 bytes = 128 bytes (exceeds 64-byte cache line and prefetch buffer)

    static (double timeMs, bool correct) RunAdjacent(int numThreads)
    {
        long[] counters = new long[numThreads];
        Thread[] threads = new Thread[numThreads];

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < numThreads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                for (long i = 0; i < M; i++)
                {
                    counters[tid]++;
                }
            });
            threads[t].Start();
        }

        for (int t = 0; t < numThreads; t++)
        {
            threads[t].Join();
        }
        sw.Stop();

        bool correct = counters.All(c => c == M);
        return (sw.Elapsed.TotalMilliseconds, correct);
    }

    static (double timeMs, bool correct) RunPadded(int numThreads)
    {
        long[] counters = new long[numThreads * Stride];
        Thread[] threads = new Thread[numThreads];

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < numThreads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                int index = tid * Stride;
                for (long i = 0; i < M; i++)
                {
                    counters[index]++;
                }
            });
            threads[t].Start();
        }

        for (int t = 0; t < numThreads; t++)
        {
            threads[t].Join();
        }
        sw.Stop();

        bool correct = true;
        for (int t = 0; t < numThreads; t++)
        {
            if (counters[t * Stride] != M)
            {
                correct = false;
                break;
            }
        }

        return (sw.Elapsed.TotalMilliseconds, correct);
    }

    static (double timeMs, bool correct) RunLocalAccumulate(int numThreads)
    {
        long[] counters = new long[numThreads];
        Thread[] threads = new Thread[numThreads];

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < numThreads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                long local = 0;
                for (long i = 0; i < M; i++)
                {
                    local++;
                }
                counters[tid] = local;
            });
            threads[t].Start();
        }

        for (int t = 0; t < numThreads; t++)
        {
            threads[t].Join();
        }
        sw.Stop();

        bool correct = counters.All(c => c == M);
        return (sw.Elapsed.TotalMilliseconds, correct);
    }

    static void Main(string[] args)
    {
        int numThreads = 4;
        if (args.Length > 0 && int.TryParse(args[0], out int t))
            numThreads = t;

        Console.WriteLine($"=== False Sharing Benchmark (Threads = {numThreads}) ===");
        Console.WriteLine($"Iterations M = {M:N0}, Cores = {Environment.ProcessorCount}, Cache Line = 64 bytes");

        // Warmup
        RunAdjacent(1);
        RunPadded(1);
        RunLocalAccumulate(1);

        var adj = RunAdjacent(numThreads);
        var pad = RunPadded(numThreads);
        var loc = RunLocalAccumulate(numThreads);

        double slowdown = pad.timeMs > 0 ? (adj.timeMs / pad.timeMs) : 1.0;
        bool allCorrect = adj.correct && pad.correct && loc.correct;

        Console.WriteLine($"{"Version",-20} | {"Time (ms)",-12} | {"Counters correct?",-18}");
        Console.WriteLine(new string('-', 55));
        Console.WriteLine($"{"adjacent",-20} | {adj.timeMs,12:F2} | {(adj.correct ? "Y" : "N"),-18}");
        Console.WriteLine($"{"padded",-20} | {pad.timeMs,12:F2} | {(pad.correct ? "Y" : "N"),-18}");
        Console.WriteLine($"{"local-accumulate",-20} | {loc.timeMs,12:F2} | {(loc.correct ? "Y" : "N"),-18}");
        Console.WriteLine(new string('-', 55));
        Console.WriteLine($"Slowdown (adjacent / padded): {slowdown:F2}x");
        Console.WriteLine($"[RESULT_ROW] threads={numThreads};adjacent={adj.timeMs:F2};padded={pad.timeMs:F2};local={loc.timeMs:F2};slowdown={slowdown:F2};correct={(allCorrect ? "YES" : "NO")}");
    }
}
