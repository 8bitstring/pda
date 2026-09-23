using System;
using System.Threading;

class Program
{
    // the big array all threads will read from — filled once, never written again
    static long[] data = new long[10_000_000];

    // one slot per thread so each thread can store its result without stepping on others
    static long[] partialSums = Array.Empty<long>();

    static int numWorkers;

    // this method runs inside each worker thread
    // each thread gets its own index (0, 1, 2 ...) and works on its own chunk
    static void SumSlice(object? arg)
    {
        int idx = (int)arg!;

        // figure out where this thread's slice starts and ends
        int sliceSize = data.Length / numWorkers;
        int start = idx * sliceSize;

        // last thread takes whatever is left over so nothing gets skipped
        int end = (idx == numWorkers - 1) ? data.Length : start + sliceSize;

        long sum = 0;
        for (int i = start; i < end; i++)
            sum += data[i];

        // each thread writes only to its own slot — no lock needed because there's no overlap
        partialSums[idx] = sum;

        Console.WriteLine($"  thread {idx}: covered [{start}, {end}), partial sum = {sum}");
    }

    static void Main()
    {
        // fill the array with 1, 2, 3, ... 10000000
        // expected total: n*(n+1)/2 = 50,000,005,000,000
        Console.WriteLine("filling array...");
        for (int i = 0; i < data.Length; i++)
            data[i] = i + 1;

        // use as many threads as there are logical cores on this machine
        numWorkers = Environment.ProcessorCount;
        partialSums = new long[numWorkers];

        Console.WriteLine($"array size    : {data.Length:N0}");
        Console.WriteLine($"worker threads: {numWorkers}");
        Console.WriteLine();

        // spawn all the threads
        Thread[] threads = new Thread[numWorkers];
        for (int i = 0; i < numWorkers; i++)
        {
            int idx = i;  // capture i before the loop moves on, otherwise all threads get the same value
            threads[i] = new Thread(() => SumSlice(idx));
            threads[i].Start();
        }

        // wait for every thread to finish before reading the results
        for (int i = 0; i < numWorkers; i++)
            threads[i].Join();

        // add up all the partial results
        long threadedTotal = 0;
        foreach (long partial in partialSums)
            threadedTotal += partial;

        // do the same thing sequentially so we can check the answer
        long sequentialTotal = 0;
        foreach (long val in data)
            sequentialTotal += val;

        Console.WriteLine($"\nthreaded total  : {threadedTotal:N0}");
        Console.WriteLine($"sequential total: {sequentialTotal:N0}");
        Console.WriteLine($"match           : {(threadedTotal == sequentialTotal ? "yes" : "NO - something went wrong")}");
    }
}
