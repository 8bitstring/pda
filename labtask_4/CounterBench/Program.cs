using System;
using System.Diagnostics;
using System.Threading;

class Program
{
    const int NumThreads = 4;
    const int IncrementsPerThread = 1_000_000;
    static long counter = 0;
    static readonly object counterLock = new object();

    static void LockWorker()
    {
        for (int i = 0; i < IncrementsPerThread; i++)
        {
            lock (counterLock) { counter++; }
        }
    }

    static void InterlockedWorker()
    {
        for (int i = 0; i < IncrementsPerThread; i++)
        {
            Interlocked.Increment(ref counter);
        }
    }

    static double RunVersion(string name, ThreadStart worker)
    {
        counter = 0;
        Thread[] threads = new Thread[NumThreads];
        Stopwatch sw = Stopwatch.StartNew();

        for (int i = 0; i < NumThreads; i++)
        {
            threads[i] = new Thread(worker);
            threads[i].Start();
        }
        for (int i = 0; i < NumThreads; i++)
        {
            threads[i].Join();
        }

        sw.Stop();
        long expected = (long)NumThreads * IncrementsPerThread;
        double ms = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[{name}] expected={expected} actual={counter} " +
            $"correct={counter == expected} time={ms:F2} ms");
        return ms;
    }

    static void Main()
    {
        RunVersion("warm-up lock", LockWorker);
        RunVersion("warm-up Interlocked", InterlockedWorker);

        RunVersion("lock", LockWorker);
        RunVersion("Interlocked.Increment", InterlockedWorker);
    }
}
