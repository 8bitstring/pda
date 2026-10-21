using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

class Program
{
    const int N = 10_000_000;
    static int[] data = new int[N];
    static long expectedSum;
    static int expectedMax;

    static readonly object gate = new object();

    static long racySum;
    static int racyMax;

    static long lockEachSum;
    static int lockEachMax;

    static long lockPartialSum;
    static int lockPartialMax;

    static void InitializeData()
    {
        var rng = new Random(42);
        long sum = 0;
        int max = int.MinValue;

        for (int i = 0; i < N; i++)
        {
            int val = rng.Next(1, 10_000);
            data[i] = val;
            sum += val;
            if (val > max) max = val;
        }

        // Set one distinct peak maximum value
        data[N / 2] = 999_999;
        sum += (999_999 - data[N / 2]); // adjust
        expectedMax = 999_999;

        // Recalculate true sum accurately
        sum = 0;
        for (int i = 0; i < N; i++)
        {
            sum += data[i];
            if (data[i] > max) max = data[i];
        }
        expectedSum = sum;
        expectedMax = max;
    }

    static (long sum, int max, double timeMs) SerialVersion()
    {
        var sw = Stopwatch.StartNew();
        long sum = 0;
        int max = int.MinValue;
        for (int i = 0; i < N; i++)
        {
            sum += data[i];
            if (data[i] > max) max = data[i];
        }
        sw.Stop();
        return (sum, max, sw.Elapsed.TotalMilliseconds);
    }

    static (long sum, int max, double timeMs) RacyVersion(int numThreads)
    {
        racySum = 0;
        racyMax = int.MinValue;

        Thread[] threads = new Thread[numThreads];
        int chunkSize = (N + numThreads - 1) / numThreads;

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < numThreads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                int start = tid * chunkSize;
                int end = Math.Min(N, start + chunkSize);
                for (int i = start; i < end; i++)
                {
                    racySum += data[i];
                    if (data[i] > racyMax) racyMax = data[i];
                }
            });
            threads[t].Start();
        }

        for (int t = 0; t < numThreads; t++)
        {
            threads[t].Join();
        }
        sw.Stop();

        return (racySum, racyMax, sw.Elapsed.TotalMilliseconds);
    }

    static (long sum, int max, double timeMs) LockEachVersion(int numThreads)
    {
        lockEachSum = 0;
        lockEachMax = int.MinValue;

        Thread[] threads = new Thread[numThreads];
        int chunkSize = (N + numThreads - 1) / numThreads;

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < numThreads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                int start = tid * chunkSize;
                int end = Math.Min(N, start + chunkSize);
                for (int i = start; i < end; i++)
                {
                    lock (gate)
                    {
                        lockEachSum += data[i];
                        if (data[i] > lockEachMax) lockEachMax = data[i];
                    }
                }
            });
            threads[t].Start();
        }

        for (int t = 0; t < numThreads; t++)
        {
            threads[t].Join();
        }
        sw.Stop();

        return (lockEachSum, lockEachMax, sw.Elapsed.TotalMilliseconds);
    }

    static (long sum, int max, double timeMs) LockPartialVersion(int numThreads)
    {
        lockPartialSum = 0;
        lockPartialMax = int.MinValue;

        Thread[] threads = new Thread[numThreads];
        int chunkSize = (N + numThreads - 1) / numThreads;

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < numThreads; t++)
        {
            int tid = t;
            threads[t] = new Thread(() =>
            {
                long localSum = 0;
                int localMax = int.MinValue;
                int start = tid * chunkSize;
                int end = Math.Min(N, start + chunkSize);

                for (int i = start; i < end; i++)
                {
                    localSum += data[i];
                    if (data[i] > localMax) localMax = data[i];
                }

                lock (gate)
                {
                    lockPartialSum += localSum;
                    if (localMax > lockPartialMax) lockPartialMax = localMax;
                }
            });
            threads[t].Start();
        }

        for (int t = 0; t < numThreads; t++)
        {
            threads[t].Join();
        }
        sw.Stop();

        return (lockPartialSum, lockPartialMax, sw.Elapsed.TotalMilliseconds);
    }

    static (long sum, int max, double timeMs) PlinqVersion(int numThreads)
    {
        var sw = Stopwatch.StartNew();
        var result = data.AsParallel()
            .WithDegreeOfParallelism(numThreads)
            .Aggregate(
                () => (sum: 0L, max: int.MinValue),
                (acc, val) => (acc.sum + val, val > acc.max ? val : acc.max),
                (acc1, acc2) => (acc1.sum + acc2.sum, Math.Max(acc1.max, acc2.max)),
                res => res
            );
        sw.Stop();

        return (result.sum, result.max, sw.Elapsed.TotalMilliseconds);
    }

    static void Main(string[] args)
    {
        int numThreads = 4;
        if (args.Length > 0 && int.TryParse(args[0], out int t))
        {
            numThreads = t;
        }

        Console.WriteLine($"=== Reduction Benchmark (Threads = {numThreads}) ===");
        Console.WriteLine($"Array size N = {N:N0}, Cores = {Environment.ProcessorCount}");

        InitializeData();
        Console.WriteLine($"Expected Sum = {expectedSum}, Expected Max = {expectedMax}\n");

        // Warmup
        SerialVersion();
        PlinqVersion(numThreads);

        const int runs = 3;

        // 1. Serial
        double serialTimeSum = 0;
        long sSum = 0; int sMax = 0;
        for (int r = 0; r < runs; r++)
        {
            var res = SerialVersion();
            sSum = res.sum; sMax = res.max;
            serialTimeSum += res.timeMs;
        }
        double serialAvg = serialTimeSum / runs;
        bool serialSumOk = sSum == expectedSum;
        bool serialMaxOk = sMax == expectedMax;

        // 2. Racy
        double racyTimeSum = 0;
        long rSum = 0; int rMax = 0;
        for (int r = 0; r < runs; r++)
        {
            var res = RacyVersion(numThreads);
            rSum = res.sum; rMax = res.max;
            racyTimeSum += res.timeMs;
        }
        double racyAvg = racyTimeSum / runs;
        bool racySumOk = rSum == expectedSum;
        bool racyMaxOk = rMax == expectedMax;

        // 3. Lock-each
        double lockEachTimeSum = 0;
        long leSum = 0; int leMax = 0;
        for (int r = 0; r < runs; r++)
        {
            var res = LockEachVersion(numThreads);
            leSum = res.sum; leMax = res.max;
            lockEachTimeSum += res.timeMs;
        }
        double lockEachAvg = lockEachTimeSum / runs;
        bool lockEachSumOk = leSum == expectedSum;
        bool lockEachMaxOk = leMax == expectedMax;

        // 4. Lock-partial
        double lockPartialTimeSum = 0;
        long lpSum = 0; int lpMax = 0;
        for (int r = 0; r < runs; r++)
        {
            var res = LockPartialVersion(numThreads);
            lpSum = res.sum; lpMax = res.max;
            lockPartialTimeSum += res.timeMs;
        }
        double lockPartialAvg = lockPartialTimeSum / runs;
        bool lockPartialSumOk = lpSum == expectedSum;
        bool lockPartialMaxOk = lpMax == expectedMax;

        // 5. PLINQ
        double plinqTimeSum = 0;
        long pSum = 0; int pMax = 0;
        for (int r = 0; r < runs; r++)
        {
            var res = PlinqVersion(numThreads);
            pSum = res.sum; pMax = res.max;
            plinqTimeSum += res.timeMs;
        }
        double plinqAvg = plinqTimeSum / runs;
        bool plinqSumOk = pSum == expectedSum;
        bool plinqMaxOk = pMax == expectedMax;

        Console.WriteLine($"{"Variant",-15} | {"Sum correct?",-12} | {"Max correct?",-12} | {"Average time (ms)",-18} | {"sum_ok",-8} | {"max_ok",-8}");
        Console.WriteLine(new string('-', 85));
        Console.WriteLine($"{"serial",-15} | {(serialSumOk ? "Y" : "N"),-12} | {(serialMaxOk ? "Y" : "N"),-12} | {serialAvg,18:F2} | {(serialSumOk ? "YES" : "NO"),-8} | {(serialMaxOk ? "YES" : "NO"),-8}");
        Console.WriteLine($"{"racy",-15} | {(racySumOk ? "Y" : "N"),-12} | {(racyMaxOk ? "Y" : "N"),-12} | {racyAvg,18:F2} | {(racySumOk ? "YES" : "NO"),-8} | {(racyMaxOk ? "YES" : "NO"),-8} (Sum: {rSum}, Max: {rMax})");
        Console.WriteLine($"{"lock-each",-15} | {(lockEachSumOk ? "Y" : "N"),-12} | {(lockEachMaxOk ? "Y" : "N"),-12} | {lockEachAvg,18:F2} | {(lockEachSumOk ? "YES" : "NO"),-8} | {(lockEachMaxOk ? "YES" : "NO"),-8}");
        Console.WriteLine($"{"lock-partial",-15} | {(lockPartialSumOk ? "Y" : "N"),-12} | {(lockPartialMaxOk ? "Y" : "N"),-12} | {lockPartialAvg,18:F2} | {(lockPartialSumOk ? "YES" : "NO"),-8} | {(lockPartialMaxOk ? "YES" : "NO"),-8}");
        Console.WriteLine($"{"plinq",-15} | {(plinqSumOk ? "Y" : "N"),-12} | {(plinqMaxOk ? "Y" : "N"),-12} | {plinqAvg,18:F2} | {(plinqSumOk ? "YES" : "NO"),-8} | {(plinqMaxOk ? "YES" : "NO"),-8}");

        Console.WriteLine("\n[SUMMARY_ROW]");
        Console.WriteLine($"threads:{numThreads};lock-each:{lockEachAvg:F2};lock-partial:{lockPartialAvg:F2};plinq:{plinqAvg:F2}");
    }
}
