using System;
using System.Diagnostics;
using MPI;

class Styles
{
    const int Tasks = 64;
    const int Chunk = 20000;
    const int TagRequest = 1, TagTask = 2;
    const int Laps = 1000;

    static long CountPrimes(long lo, long hi) // primes in [lo, hi)
    {
        if (hi <= 2) return 0;
        long count = 0;

        if (lo < 2) lo = 2;
        if (lo <= 2 && hi > 2) { count++; lo = 3; }
        if (lo % 2 == 0) lo++;

        for (long n = lo; n < hi; n += 2)
        {
            bool prime = true;
            for (long d = 3; d * d <= n; d += 2)
                if (n % d == 0) { prime = false; break; }
            if (prime) count++;
        }
        return count;
    }

    // Independent, fast reference used by the master to check the farm's total.
    static long CountPrimesSieve(int limit)
    {
        bool[] composite = new bool[limit];
        long count = 0;
        for (int i = 2; i < limit; i++)
        {
            if (composite[i]) continue;
            count++;
            if ((long)i * i < limit)
                for (long j = (long)i * i; j < limit; j += i)
                    composite[j] = true;
        }
        return count;
    }

    static void MasterWorker(Intracommunicator comm)
    {
        int rank = comm.Rank, size = comm.Size;

        if (rank == 0) // the server
        {
            long total = 0, next = 0;
            int stopped = 0;
            int[] served = new int[size];

            while (stopped < size - 1)
            {
                Status probe = comm.Probe(Communicator.anySource, TagRequest);
                int source = probe.Source;
                long[] msg = new long[2];
                comm.Receive(source, TagRequest, ref msg);

                if (msg[0] >= 0) { total += msg[1]; served[source]++; }

                long task = (next < Tasks) ? next++ : -1; // -1 tells the client to stop
                comm.Send(task, source, TagTask);
                if (task < 0) stopped++;
            }

            long expected = CountPrimesSieve(Tasks * Chunk);
            Console.WriteLine($"[master-worker] tasks={Tasks} workers={size - 1} " +
                $"total_primes={total} expected={expected} " +
                $"correct={(total == expected ? "YES" : "NO")}");
            Console.Write("tasks served per worker:");
            for (int w = 1; w < size; w++) Console.Write($" rank{w}={served[w]}");
            Console.WriteLine();
        }
        else // a client
        {
            long[] msg = { -1, 0 };
            while (true)
            {
                comm.Send(msg, 0, TagRequest);
                long task = comm.Receive<long>(0, TagTask);
                if (task < 0) break;
                msg = new long[] { task, CountPrimes(task * Chunk, (task + 1) * Chunk) };
            }
        }
    }

    static void Ring(Intracommunicator comm)
    {
        int rank = comm.Rank, size = comm.Size;
        int right = (rank + 1) % size, left = (rank + size - 1) % size;
        long token = 0;

        comm.Barrier();
        Stopwatch sw = Stopwatch.StartNew();

        for (int lap = 0; lap < Laps; lap++)
        {
            if (rank == 0)
            {
                token += rank;
                comm.Send(token, right, 0);
                token = comm.Receive<long>(left, 0);
            }
            else
            {
                token = comm.Receive<long>(left, 0);
                token += rank;
                comm.Send(token, right, 0);
            }
        }

        double ms = sw.Elapsed.TotalMilliseconds;
        if (rank == 0)
        {
            long expected = (long)Laps * size * (size - 1) / 2;
            Console.WriteLine($"[ring] ranks={size} laps={Laps} token={token} " +
                $"expected={expected} " +
                $"correct={(token == expected ? "YES" : "NO")} time={ms:F2} ms");
            Console.WriteLine($"messages sent={Laps * size}, " +
                $"average per message={ms / ((double)Laps * size):F4} ms");
        }
    }

    // usage: mpiexec -n 4 Styles.exe [master|ring]
    static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            Intracommunicator comm = Communicator.world;
            string mode = args.Length > 0 ? args[0] : "master";

            if (comm.Size < 2)
            {
                if (comm.Rank == 0) Console.WriteLine("run with at least 2 ranks");
                return;
            }

            if (mode == "ring") Ring(comm);
            else MasterWorker(comm);
        }
    }
}
