using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MPI;

class SumCompare
{
    const int N = 10_000_000;
    const int Reps = 3;

    static long sharedTotal;

    static long LocalSum(int[] a)
    {
        long s = 0;
        foreach (int v in a) s += v;
        return s;
    }

    static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            Intracommunicator comm = Communicator.world;
            int rank = comm.Rank, size = comm.Size;

            if (N % size != 0)
            {
                if (rank == 0)
                    Console.WriteLine("N must be divisible by the number of ranks");
                return;
            }

            int chunk = N / size;
            int[] data = null;      // exists only on rank 0
            int[][] chunks = null;  // the pieces rank 0 distributes
            long reference = 0;

            if (rank == 0)
            {
                data = new int[N];
                for (int i = 0; i < N; i++)
                    data[i] = (int)(((long)i * 7919) % 100003);

                reference = LocalSum(data); // serial reference

                chunks = new int[size][];
                for (int r = 0; r < size; r++)
                {
                    chunks[r] = new int[chunk];
                    Array.Copy(data, (long)r * chunk, chunks[r], 0, chunk);
                }
            }

            double tColl = 0, tP2p = 0, tShared = 0;
            long totalColl = 0, totalP2p = 0;

            for (int rep = 0; rep < Reps; rep++)
            {
                // --- variant 1: collective communication ---
                comm.Barrier();
                Stopwatch sw = Stopwatch.StartNew();

                int[] mine1 = (rank == 0) ? comm.Scatter(chunks, 0)
                                          : comm.Scatter<int[]>(0);
                long part = LocalSum(mine1);
                long reduced = comm.Reduce(part, Operation<long>.Add, 0);
                if (rank == 0) totalColl = reduced;

                comm.Barrier();
                tColl += sw.Elapsed.TotalMilliseconds;

                // --- variant 2: explicit point-to-point messages ---
                comm.Barrier();
                sw.Restart();

                if (rank == 0)
                {
                    for (int i = 1; i < size; i++)
                        comm.Send(chunks[i], i, 1);

                    long sum = LocalSum(chunks[0]);
                    for (int i = 1; i < size; i++)
                        sum += comm.Receive<long>(i, 2);
                    totalP2p = sum;
                }
                else
                {
                    int[] mine2 = new int[chunk];
                    comm.Receive(0, 1, ref mine2);
                    comm.Send(LocalSum(mine2), 0, 2);
                }

                comm.Barrier();
                tP2p += sw.Elapsed.TotalMilliseconds;

                // --- variant 3: shared memory in one process (rank 0 only) ---
                if (rank == 0)
                {
                    sharedTotal = 0;
                    ParallelOptions options =
                        new ParallelOptions { MaxDegreeOfParallelism = size };
                    sw.Restart();
                    Parallel.ForEach(Partitioner.Create(0, N), options, () => 0L,
                        (range, state, local) =>
                        {
                            for (int i = range.Item1; i < range.Item2; i++)
                                local += data[i];
                            return local;
                        },
                        local => Interlocked.Add(ref sharedTotal, local));
                    tShared += sw.Elapsed.TotalMilliseconds;
                }
                comm.Barrier();
            }

            if (rank == 0)
            {
                Console.WriteLine($"ranks={size} n={N} reps={Reps} reference_sum={reference}");
                Console.WriteLine($"[collective ] sum={totalColl} " +
                    $"correct={(totalColl == reference ? "YES" : "NO")} " +
                    $"avg_time={tColl / Reps:F2} ms");
                Console.WriteLine($"[point-to-point] sum={totalP2p} " +
                    $"correct={(totalP2p == reference ? "YES" : "NO")} " +
                    $"avg_time={tP2p / Reps:F2} ms");
                Console.WriteLine($"[shared memory ] threads={size} sum={sharedTotal} " +
                    $"correct={(sharedTotal == reference ? "YES" : "NO")} " +
                    $"avg_time={tShared / Reps:F2} ms");
            }
        }
    }
}
