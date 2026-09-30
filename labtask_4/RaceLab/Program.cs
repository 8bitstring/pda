using System;
using System.Threading;

class Program
{
    const int NumThreads = 4;
    const int IncrementsPerThread = 1_000_000;
    static long counter = 0; // shared state

    static void Worker()
    {
        for (int i = 0; i < IncrementsPerThread; i++)
        {
            counter++; // unsynchronized read-modify-write
        }
    }

    static void Main()
    {
        Thread[] threads = new Thread[NumThreads];
        for (int i = 0; i < NumThreads; i++)
        {
            threads[i] = new Thread(Worker);
            threads[i].Start();
        }

        for (int i = 0; i < NumThreads; i++)
        {
            threads[i].Join();
        }

        long expected = (long)NumThreads * IncrementsPerThread;
        Console.WriteLine($"Expected: {expected}");
        Console.WriteLine($"Actual: {counter}");
        Console.WriteLine($"Lost updates: {expected - counter}");
    }
}
