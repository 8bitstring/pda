using System;
using System.Diagnostics;
using System.Threading;

class Program
{
    const int ItemsPerProducer = 25_000;

    static readonly object gate = new object();

    static SemaphoreSlim emptySlots = null!;
    static SemaphoreSlim fullSlots = null!;

    static int[] buffer = null!;
    static int[] seen = null!;
    static int inPos;
    static int outPos;
    static int capacity = 8;

    static int produced;
    static int consumed;
    static bool wrongOrder;

    static void Producer(int id)
    {
        for (int i = 0; i < ItemsPerProducer; i++)
        {
            int item = id * ItemsPerProducer + i + 1;

            if (wrongOrder)
                lock (gate) { emptySlots.Wait(); }
            else
            {
                emptySlots.Wait();
                lock (gate) { }
            }

            lock (gate)
            {
                buffer[inPos] = item;
                inPos = (inPos + 1) % capacity;
            }

            fullSlots.Release();
            Interlocked.Increment(ref produced);
        }
    }

    static void Consumer(int quota)
    {
        for (int i = 0; i < quota; i++)
        {
            if (wrongOrder)
                lock (gate) { fullSlots.Wait(); }
            else
            {
                fullSlots.Wait();
                lock (gate) { }
            }

            int value;
            lock (gate)
            {
                value = buffer[outPos];
                outPos = (outPos + 1) % capacity;
            }

            emptySlots.Release();
            Interlocked.Increment(ref seen[value]);
            Interlocked.Increment(ref consumed);
        }
    }

    static void Main(string[] args)
    {
        int producers = 2;
        int consumers = 2;
        int next = 0;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--wrong-order")
            {
                wrongOrder = true;
                continue;
            }

            if (!int.TryParse(args[i], out int value))
            {
                Console.WriteLine("usage: producers consumers capacity [--wrong-order]");
                return;
            }

            if (next == 0) producers = value;
            else if (next == 1) consumers = value;
            else if (next == 2) capacity = value;

            next++;
        }

        if (producers < 1 || consumers < 1 || capacity < 1)
        {
            Console.WriteLine("usage: producers consumers capacity [--wrong-order]");
            return;
        }

        buffer = new int[capacity];

        int total = producers * ItemsPerProducer;
        int quota = total / consumers;
        seen = new int[total + 1];

        emptySlots = new SemaphoreSlim(capacity, capacity);
        fullSlots = new SemaphoreSlim(0, capacity);

        Console.WriteLine($"producers: {producers}, consumers: {consumers}, capacity: {capacity}, items: {total}, wrong order: {wrongOrder}");

        var threads = new Thread[producers + consumers];
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < producers; i++)
        {
            int id = i;
            threads[i] = new Thread(() => Producer(id));
            threads[i].Start();
        }

        for (int i = 0; i < consumers; i++)
        {
            int take = quota;
            threads[producers + i] = new Thread(() => Consumer(take));
            threads[producers + i].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        sw.Stop();

        int lost = 0;
        int duplicated = 0;

        for (int item = 1; item <= total; item++)
        {
            if (seen[item] == 0)
                lost++;
            else if (seen[item] > 1)
                duplicated++;
        }

        bool ok = lost == 0 && duplicated == 0 && consumed == total;

        Console.WriteLine($"produced: {produced}");
        Console.WriteLine($"consumed: {consumed}");
        Console.WriteLine($"lost: {lost}");
        Console.WriteLine($"duplicated: {duplicated}");
        Console.WriteLine($"ok: {(ok ? "Y" : "N")}");
        Console.WriteLine($"time (ms): {sw.Elapsed.TotalMilliseconds:F2}");
    }
}