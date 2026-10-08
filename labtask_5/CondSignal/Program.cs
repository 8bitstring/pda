using System;
using System.Diagnostics;
using System.Threading;

class Program
{
    static readonly object gate = new object();

    static int dataReady;
    static bool dataChanged;
    static int timesBlocked;
    static int value;
    static bool noPulse;
    static bool spin;

    static void Worker(int id, int takes)
    {
        for (int i = 0; i < takes; i++)
        {
            while (true)
            {
                if (spin)
                {
                    lock (gate)
                    {
                        if (dataChanged)
                        {
                            dataChanged = false;
                            value = dataReady;
                            break;
                        }
                    }
                }
                else
                {
                    lock (gate)
                    {
                        if (!dataChanged)
                        {
                            timesBlocked++;
                            Monitor.Wait(gate);
                            continue;
                        }

                        dataChanged = false;
                        value = dataReady;
                        break;
                    }
                }
            }
        }

        Console.WriteLine($"  worker {id} got {value}");
    }

    static void Publish(int item)
    {
        Thread.Sleep(50);

        lock (gate)
        {
            dataReady = item;
            dataChanged = true;

            if (!noPulse)
                Monitor.PulseAll(gate);
        }
    }

    static void Main(string[] args)
    {
        int workers = 1;
        int publishes = 1;
        bool signalFirst = false;
        int positional = 0;

        foreach (var arg in args)
        {
            if (arg == "spin")
                spin = true;
            else if (arg == "signal-first")
                signalFirst = true;
            else if (arg == "--no-pulse")
                noPulse = true;
            else if (arg == "cond")
                continue;
            else if (positional == 0)
                workers = int.Parse(arg);
            else
                publishes = int.Parse(arg);

            if (arg != "spin" && arg != "signal-first" && arg != "--no-pulse" && arg != "cond")
                positional++;
        }

        int takes = Math.Max(1, publishes / workers);

        string scenario = spin ? "d: spin (polling)"
            : signalFirst ? "b: signal-first"
            : workers > 2 ? "c: cond, 3 workers"
            : "a: cond, 1 worker";

        Console.WriteLine($"scenario {scenario}");
        Console.WriteLine($"  workers: {workers}, publishes: {publishes}, takes per worker: {takes}, no pulse: {noPulse}");

        var threads = new Thread[workers];
        for (int i = 0; i < workers; i++)
        {
            int id = i + 1;
            threads[i] = new Thread(() => Worker(id, takes));
        }

        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var sw = Stopwatch.StartNew();

        if (signalFirst)
        {
            for (int p = 1; p <= publishes; p++)
            {
                lock (gate)
                {
                    dataReady = p;
                    dataChanged = true;
                }
            }

            foreach (var thread in threads)
                thread.Start();
        }
        else
        {
            foreach (var thread in threads)
                thread.Start();

            Thread.Sleep(100);

            for (int p = 1; p <= publishes; p++)
                Publish(p);
        }

        foreach (var thread in threads)
            thread.Join();

        sw.Stop();

        var cpuAfter = process.TotalProcessorTime;

        Console.WriteLine($"times blocked in wait: {timesBlocked}");
        Console.WriteLine($"value received: {value}");
        Console.WriteLine($"wall time (ms): {sw.Elapsed.TotalMilliseconds:F2}");
        Console.WriteLine($"cpu time (ms) total: {cpuAfter.TotalMilliseconds:F2}");
        Console.WriteLine($"cpu time (ms) phase: {(cpuAfter - cpuBefore).TotalMilliseconds:F2}");
    }
}