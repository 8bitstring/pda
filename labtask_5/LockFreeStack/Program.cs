using System;
using System.Diagnostics;
using System.Threading;

interface IStack
{
    void Push(int value);
    bool Pop(out int value);
}

class Node
{
    public int Value;
    public Node Next = null!;
}

class LockFreeStack : IStack
{
    Node head = null!;

    public void Push(int value)
    {
        while (true)
        {
            Node seen = Volatile.Read(ref head);
            var node = new Node { Value = value, Next = seen };

            if (Interlocked.CompareExchange(ref head, node, seen) == seen)
                return;
        }
    }

    public bool Pop(out int value)
    {
        while (true)
        {
            Node seen = Volatile.Read(ref head);

            if (seen == null)
            {
                value = 0;
                return false;
            }

            Node next = Volatile.Read(ref seen.Next);

            if (Interlocked.CompareExchange(ref head, next, seen) == seen)
            {
                value = seen.Value;
                return true;
            }
        }
    }
}

class MutexStack : IStack
{
    readonly object gate = new object();
    Node head = null!;

    public void Push(int value)
    {
        lock (gate)
            head = new Node { Value = value, Next = head };
    }

    public bool Pop(out int value)
    {
        lock (gate)
        {
            if (head == null)
            {
                value = 0;
                return false;
            }

            Node node = head;
            head = node.Next;
            value = node.Value;
            return true;
        }
    }
}

class Program
{
    const int OpsPerThread = 50_000;

    static int numThreads = 4;
    static int[] popped = null!;
    static int pushed;

    static void Worker(int id, IStack stack)
    {
        for (int i = 0; i < OpsPerThread; i++)
        {
            stack.Push(id * OpsPerThread + i + 1);
            Interlocked.Increment(ref pushed);

            if (stack.Pop(out int value))
                Interlocked.Increment(ref popped[value]);
        }
    }

    static (long lost, long duplicated, bool empty, double ms) Run(bool lockFree)
    {
        IStack stack = lockFree ? (IStack)new LockFreeStack() : new MutexStack();

        int total = numThreads * OpsPerThread;
        pushed = 0;
        popped = new int[total + 1];

        var threads = new Thread[numThreads];
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < numThreads; i++)
        {
            int id = i;
            threads[i] = new Thread(() => Worker(id, stack));
            threads[i].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        sw.Stop();

        long lost = 0;
        long duplicated = 0;

        for (int v = 1; v <= total; v++)
        {
            if (popped[v] == 0)
                lost++;
            else if (popped[v] > 1)
                duplicated++;
        }

        int remaining = 0;
        while (stack.Pop(out _))
            remaining++;

        return (lost, duplicated, remaining == 0, sw.Elapsed.TotalMilliseconds);
    }

    static void Main(string[] args)
    {
        foreach (var arg in args)
            numThreads = int.Parse(arg);

        int total = numThreads * OpsPerThread;

        Console.WriteLine($"processor count: {Environment.ProcessorCount}");
        Console.WriteLine($"threads: {numThreads}, push pop pairs per thread: {OpsPerThread}, total elements: {total}");

        var lf = Run(true);
        Console.WriteLine($"nodes (total): {total}");
        Console.WriteLine($"lost: {lf.lost}");
        Console.WriteLine($"duplicated: {lf.duplicated}");
        Console.WriteLine($"stack empty: {(lf.empty ? "Y" : "N")}");
        Console.WriteLine($"lock free time (ms): {lf.ms:F2}");

        var mx = Run(false);
        Console.WriteLine($"mutex time (ms): {mx.ms:F2} (lost: {mx.lost}, duplicated: {mx.duplicated}, empty: {(mx.empty ? "Y" : "N")})");
        Console.WriteLine($"ratio: {mx.ms / lf.ms:F2}x");
    }
}