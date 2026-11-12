using System;
using System.Diagnostics;
using MPI;

class PingPong
{
    static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            Intracommunicator comm = Communicator.world;
            int rank = comm.Rank;
            int size = comm.Size;

            if (size != 2)
            {
                if (rank == 0)
                    Console.WriteLine("This program requires exactly 2 ranks");
                return;
            }

            // Message sizes to test (in bytes)
            int[] sizes = { 0, 1, 8, 64, 256, 1024, 4096, 16384, 65536, 262144 };
            int iterations = 100; // Number of repetitions for stable results

            if (rank == 0)
            {
                Console.WriteLine("=== Ping-Pong Benchmark ===");
                Console.WriteLine("Message Size (bytes) | Iterations | Mean RTT (µs)");
                Console.WriteLine("--------------------------------------------------");
            }

            foreach (int msgSize in sizes)
            {
                byte[] buffer = new byte[msgSize];
                
                // Initialize buffer with some data
                for (int i = 0; i < msgSize; i++)
                    buffer[i] = (byte)(i % 256);

                comm.Barrier(); // Synchronize before timing
                Stopwatch sw = Stopwatch.StartNew();

                if (rank == 0)
                {
                    // Rank 0: Send to rank 1, then receive back
                    for (int iter = 0; iter < iterations; iter++)
                    {
                        comm.Send(buffer, 1, 0);
                        comm.Receive(1, 0, ref buffer);
                    }
                }
                else // rank == 1
                {
                    // Rank 1: Receive from rank 0, then send back
                    for (int iter = 0; iter < iterations; iter++)
                    {
                        comm.Receive(0, 0, ref buffer);
                        comm.Send(buffer, 0, 0);
                    }
                }

                sw.Stop();
                comm.Barrier();

                if (rank == 0)
                {
                    double totalTimeUs = sw.Elapsed.TotalMilliseconds * 1000; // Convert to microseconds
                    double meanRTT = totalTimeUs / iterations;
                    Console.WriteLine($"{msgSize,20} | {iterations,10} | {meanRTT,13:F4}");
                }
            }

            if (rank == 0)
            {
                Console.WriteLine("\n=== Benchmark Complete ===");
                Console.WriteLine("RTT = Round-Trip Time (send from rank 0 to rank 1 and back)");
                Console.WriteLine("RTT/2 provides an estimate of one-way latency");
            }
        }
    }
}
