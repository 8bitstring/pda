using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    const int N = 30_000;
    static readonly long[] results = new long[N];
    static readonly int[] completedCount = new int[N];

    static int sharedIndex;

    static void DoWork(int i, bool isUniform)
    {
        int iters = isUniform ? 1500 : (int)(3000.0 * i / N) + 10;
        long acc = 0;
        for (int j = 0; j < iters; j++)
        {
            acc += (j ^ (i + 1));
        }
        results[i] = acc;
        completedCount[i]++;
    }

    static (double wallMs, double busyMax, double busyMin, double imbalance, bool ok) RunPolicy(
        string policy, int numThreads, bool isUniform)
    {
        Array.Clear(completedCount, 0, N);
        sharedIndex = 0;

        double[] workerBusyTimes = new double[numThreads];
        Thread[] threads = new Thread[numThreads];

        var wallSw = Stopwatch.StartNew();

        if (policy == "Parallel.For")
        {
            var threadTimes = new ConcurrentDictionary<int, Stopwatch>();

            Parallel.For(0, N, new ParallelOptions { MaxDegreeOfParallelism = numThreads }, i =>
            {
                int tid = Environment.CurrentManagedThreadId;
                var sw = threadTimes.GetOrAdd(tid, _ => Stopwatch.StartNew());
                DoWork(i, isUniform);
            });

            wallSw.Stop();

            var times = threadTimes.Values.Select(s => s.Elapsed.TotalMilliseconds).ToList();
            if (times.Count == 0) times.Add(wallSw.Elapsed.TotalMilliseconds);

            double bMax = times.Max();
            double bMin = times.Min();
            double imb = bMin > 0.001 ? (bMax / bMin) : 1.0;
            bool correct = completedCount.All(c => c == 1);

            return (wallSw.Elapsed.TotalMilliseconds, bMax, bMin, imb, correct);
        }

        // Hand-made thread implementations for policies 1 to 7
        for (int t = 0; t < numThreads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                var busySw = Stopwatch.StartNew();

                switch (policy)
                {
                    case "static":
                        {
                            int chunkSize = (N + numThreads - 1) / numThreads;
                            int start = tid * chunkSize;
                            int end = Math.Min(N, start + chunkSize);
                            for (int i = start; i < end; i++)
                                DoWork(i, isUniform);
                            break;
                        }

                    case "static,1":
                        {
                            for (int i = tid; i < N; i += numThreads)
                                DoWork(i, isUniform);
                            break;
                        }

                    case "static,64":
                        {
                            const int chunk = 64;
                            int totalChunks = (N + chunk - 1) / chunk;
                            for (int c = tid; c < totalChunks; c += numThreads)
                            {
                                int start = c * chunk;
                                int end = Math.Min(N, start + chunk);
                                for (int i = start; i < end; i++)
                                    DoWork(i, isUniform);
                            }
                            break;
                        }

                    case "dynamic,1":
                        {
                            while (true)
                            {
                                int i = Interlocked.Increment(ref sharedIndex) - 1;
                                if (i >= N) break;
                                DoWork(i, isUniform);
                            }
                            break;
                        }

                    case "dynamic,64":
                        {
                            const int chunk = 64;
                            while (true)
                            {
                                int start = Interlocked.Add(ref sharedIndex, chunk) - chunk;
                                if (start >= N) break;
                                int end = Math.Min(N, start + chunk);
                                for (int i = start; i < end; i++)
                                    DoWork(i, isUniform);
                            }
                            break;
                        }

                    case "guided,1":
                        {
                            const int minChunk = 1;
                            while (true)
                            {
                                int current = 0, next = 0, chunk = 0;
                                do
                                {
                                    current = Volatile.Read(ref sharedIndex);
                                    if (current >= N) break;
                                    int remaining = N - current;
                                    chunk = Math.Max(minChunk, (int)Math.Ceiling((double)remaining / numThreads));
                                    next = Math.Min(N, current + chunk);
                                } while (Interlocked.CompareExchange(ref sharedIndex, next, current) != current);

                                if (current >= N) break;
                                for (int i = current; i < next; i++)
                                    DoWork(i, isUniform);
                            }
                            break;
                        }

                    case "guided,64":
                        {
                            const int minChunk = 64;
                            while (true)
                            {
                                int current = 0, next = 0, chunk = 0;
                                do
                                {
                                    current = Volatile.Read(ref sharedIndex);
                                    if (current >= N) break;
                                    int remaining = N - current;
                                    chunk = Math.Max(minChunk, (int)Math.Ceiling((double)remaining / numThreads));
                                    next = Math.Min(N, current + chunk);
                                } while (Interlocked.CompareExchange(ref sharedIndex, next, current) != current);

                                if (current >= N) break;
                                for (int i = current; i < next; i++)
                                    DoWork(i, isUniform);
                            }
                            break;
                        }

                    default:
                        throw new ArgumentException($"Unknown policy {policy}");
                }

                busySw.Stop();
                workerBusyTimes[tid] = busySw.Elapsed.TotalMilliseconds;
            });

            threads[t].Start();
        }

        for (int t = 0; t < numThreads; t++)
        {
            threads[t].Join();
        }

        wallSw.Stop();

        double busyMax = workerBusyTimes.Max();
        double busyMin = workerBusyTimes.Min();
        double imbalance = busyMin > 0.001 ? (busyMax / busyMin) : 1.0;
        bool ok = completedCount.All(c => c == 1);

        return (wallSw.Elapsed.TotalMilliseconds, busyMax, busyMin, imbalance, ok);
    }

    static double MeasureSerial(bool isUniform)
    {
        Array.Clear(completedCount, 0, N);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
            DoWork(i, isUniform);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    static void Main(string[] args)
    {
        int numThreads = 4;
        bool isUniform = false;

        if (args.Length > 0 && int.TryParse(args[0], out int t))
            numThreads = t;

        if (args.Length > 1 && args[1].Equals("uniform", StringComparison.OrdinalIgnoreCase))
            isUniform = true;

        string workloadName = isUniform ? "Uniform" : "Uneven";
        Console.WriteLine($"=== Scheduling Benchmark ({workloadName} Workload, Threads = {numThreads}) ===");
        Console.WriteLine($"N = {N:N0}, Logical Cores = {Environment.ProcessorCount}");

        // Warmup
        MeasureSerial(isUniform);
        RunPolicy("dynamic,64", numThreads, isUniform);

        double serialMs = MeasureSerial(isUniform);
        Console.WriteLine($"Serial time: {serialMs:F2} ms\n");

        string[] policies = {
            "static",
            "static,1",
            "static,64",
            "dynamic,1",
            "dynamic,64",
            "guided,1",
            "guided,64",
            "Parallel.For"
        };

        Console.WriteLine($"{"Policy",-15} | {"Wall time (ms)",-14} | {"Busy max (ms)",-13} | {"Busy min (ms)",-13} | {"Imbalance",-10} | {"Correct?",-8}");
        Console.WriteLine(new string('-', 85));

        string fastestPolicy = "";
        double fastestTime = double.MaxValue;

        foreach (var policy in policies)
        {
            // Run 3 times, pick best wall time
            double bestWall = double.MaxValue;
            double bestBMax = 0, bestBMin = 0, bestImb = 0;
            bool bestOk = false;

            for (int r = 0; r < 3; r++)
            {
                var res = RunPolicy(policy, numThreads, isUniform);
                if (res.wallMs < bestWall)
                {
                    bestWall = res.wallMs;
                    bestBMax = res.busyMax;
                    bestBMin = res.busyMin;
                    bestImb = res.imbalance;
                    bestOk = res.ok;
                }
            }

            if (bestWall < fastestTime)
            {
                fastestTime = bestWall;
                fastestPolicy = policy;
            }

            Console.WriteLine($"{policy,-15} | {bestWall,14:F2} | {bestBMax,13:F2} | {bestBMin,13:F2} | {bestImb,10:F2} | {(bestOk ? "Y" : "N"),-8}");
        }

        Console.WriteLine(new string('-', 85));
        Console.WriteLine($"Fastest policy: {fastestPolicy} ({fastestTime:F2} ms)");
        Console.WriteLine($"[RESULT_SUMMARY] threads={numThreads};workload={workloadName};fastest={fastestPolicy};fastest_ms={fastestTime:F2};serial_ms={serialMs:F2}");
    }
}
