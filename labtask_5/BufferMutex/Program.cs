using System;
using System.Diagnostics;
using System.Threading;

class Program
{
    const int NumItems = 100_000;
    static int bufferSize = 8;

    static readonly object gate = new object();

    static int[] buffer = new int[bufferSize];
    static int inPos;
    static int outPos;
    static int count;

    static long producerRetries;
    static long consumerRetries;
    static long checksum;
    static long orderErrors;

    static void Producer()
    {
        for (int item = 1; item <= NumItems; item++)
        {
            while (true)
            {
                lock (gate)
                {
                    if (count < bufferSize)
                    {
                        buffer[inPos] = item;
                        inPos = (inPos + 1) % bufferSize;
                        count++;
                        break;
                    }
                }

                producerRetries++;
                Thread.Yield();
            }
        }
    }

    static void Consumer()
    {
        int expected = 1;

        for (int taken = 0; taken < NumItems; taken++)
        {
            int value;

            while (true)
            {
                lock (gate)
                {
                    if (count > 0)
                    {
                        value = buffer[outPos];
                        outPos = (outPos + 1) % bufferSize;
                        count--;
                        break;
                    }
                }

                consumerRetries++;
                Thread.Yield();
            }

            if (value != expected)
                orderErrors++;

            expected++;
            checksum += value;
        }
    }

    static void Main()
    {
        Console.WriteLine($"processor count: {Environment.ProcessorCount}");
        Console.WriteLine($"buffer size: {bufferSize}, items: {NumItems}");

        var producer = new Thread(Producer);
        var consumer = new Thread(Consumer);

        var sw = Stopwatch.StartNew();
        producer.Start();
        consumer.Start();
        producer.Join();
        consumer.Join();
        sw.Stop();

        long expectedChecksum = (long)NumItems * (NumItems + 1) / 2;
        bool ok = checksum == expectedChecksum && orderErrors == 0;

        Console.WriteLine($"items: {NumItems}");
        Console.WriteLine($"checksum: {checksum} (expected {expectedChecksum})");
        Console.WriteLine($"order errors: {orderErrors}");
        Console.WriteLine($"ok: {(ok ? "Y" : "N")}");
        Console.WriteLine($"producer retries: {producerRetries}");
        Console.WriteLine($"consumer retries: {consumerRetries}");
        Console.WriteLine($"time (ms): {sw.Elapsed.TotalMilliseconds:F2}");
    }
}