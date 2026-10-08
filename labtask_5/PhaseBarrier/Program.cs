using System;
using System.Diagnostics;
using System.Threading;

class PhaseBarrier
{
    readonly object gate = new object();
    readonly int parties;
    int waiting;
    int generation;

    public PhaseBarrier(int parties)
    {
        this.parties = parties;
    }

    public void SignalAndWait()
    {
        lock (gate)
        {
            int mine = generation;
            waiting++;

            if (waiting == parties)
            {
                waiting = 0;
                generation++;
                Monitor.PulseAll(gate);
                return;
            }

            while (mine == generation)
                Monitor.Wait(gate);
        }
    }
}

class Program
{
    const int Rounds = 200;
    const int CellsPerRound = 500;
    const int WorkPerCell = 200;

    static int[][] grid = new int[Rounds][];
    static PhaseBarrier barrier = null!;
    static int numWorkers = 4;
    static bool useBarrier = true;
    static int mismatchingCells;

    static int Expected(int column, int round)
    {
        return (column % numWorkers) * 1000 + round;
    }

    static void Worker(int id)
    {
        long checksum = 0;

        for (int round = 0; round < Rounds; round++)
        {
            int[] row = grid[round];

            for (int c = id; c < CellsPerRound; c += numWorkers)
            {
                int acc = round + 1;

                for (int i = 0; i < WorkPerCell; i++)
                    acc = acc * 31 + c & 0xffff;

                checksum += acc;
                row[c] = Expected(c, round);
            }

            if (useBarrier)
                barrier.SignalAndWait();

            if (id == 0)
            {
                for (int c = 0; c < CellsPerRound; c++)
                {
                    if (row[c] != Expected(c, round))
                        mismatchingCells++;
                }
            }

            if (useBarrier)
                barrier.SignalAndWait();
        }

        Console.WriteLine($"  worker {id} checksum {checksum}");
    }

    static void Main(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg == "--no-barrier")
                useBarrier = false;
            else
                numWorkers = int.Parse(arg);
        }

        for (int r = 0; r < Rounds; r++)
            grid[r] = new int[CellsPerRound];

        barrier = new PhaseBarrier(numWorkers);

        Console.WriteLine($"processor count: {Environment.ProcessorCount}");
        Console.WriteLine($"workers: {numWorkers}, rounds: {Rounds}, cells per round: {CellsPerRound}, barrier: {useBarrier}");

        var threads = new Thread[numWorkers];
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < numWorkers; i++)
        {
            int id = i;
            threads[i] = new Thread(() => Worker(id));
            threads[i].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        sw.Stop();

        for (int r = 0; r < Rounds; r++)
        {
            for (int c = 0; c < CellsPerRound; c++)
            {
                if (grid[r][c] != Expected(c, r))
                    mismatchingCells++;
            }
        }

        Console.WriteLine($"correct: {(mismatchingCells == 0 ? "Y" : "N")}");
        Console.WriteLine($"mismatching cells: {mismatchingCells}");
        Console.WriteLine($"time (ms): {sw.Elapsed.TotalMilliseconds:F2}");
    }
}