using System;
using System.Diagnostics;
using MPI;

class BullyDemo
{
    const int TagElection = 1, TagOk = 2, TagCoord = 3, TagShutdown = 4;
    const double Timeout = 0.5; // seconds to wait for an answer
    static Intracommunicator comm;
    static int rank, size, crashed, initiator;
    static int leader = -1;
    static bool started, gotOk;
    static double deadline;
    static readonly Stopwatch clockWatch = Stopwatch.StartNew();
    static double Now() => clockWatch.Elapsed.TotalSeconds;

    static void StartElection()
    {
        started = true; gotOk = false; deadline = Now() + Timeout;
        for (int h = rank + 1; h < size; h++) comm.Send(rank, h, TagElection);
    }

    // usage: mpiexec -n 5 Bully.exe [crashedRank] [initiatorRank]
    static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            comm = Communicator.world;
            rank = comm.Rank; size = comm.Size;
            if (size < 3)
            {
                if (rank == 0) Console.WriteLine("run with at least 3 ranks");
                return;
            }
            // the old leader is the highest rank
            crashed = size - 1;
            // the rank that notices the failure
            initiator = 0;
            if (args.Length > 0) crashed = int.Parse(args[0]);
            if (args.Length > 1) initiator = int.Parse(args[1]);
            if (initiator == crashed) initiator = (crashed + 1) % size;

            if (rank == crashed)
            {
                Console.WriteLine($"[rank {rank}] CRASHED: silent, ignores every message");
                // simulation plumbing: swallow messages
                while (true)
                {
                    CompletedStatus st;
                    int dummy;
                    comm.Receive(Communicator.anySource, Communicator.anyTag, out dummy, out st);
                    if (st.Tag == TagShutdown) break;
                }
            }
            else
            {
                if (rank == initiator)
                {
                    Console.WriteLine($"[rank {rank}] detects that leader {crashed} is down: starts an election");
                    StartElection();
                }
                while (leader < 0)
                {
                    Status probe = comm.ImmediateProbe(Communicator.anySource,
                        Communicator.anyTag);
                    if (probe != null)
                    {
                        int from = probe.Source, tag = probe.Tag;
                        int v = comm.Receive<int>(from, tag);
                        if (tag == TagElection)
                        {
                            Console.WriteLine($"[rank {rank}] got ELECTION from {from}: replies OK");
                            comm.Send(rank, from, TagOk);
                            if (!started)
                                StartElection();
                        }
                        else if (tag == TagOk) gotOk = true;
                        else if (tag == TagCoord) leader = v;
                    }
                    if (started && !gotOk && leader < 0 && Now() > deadline)
                    {
                        Console.WriteLine($"[rank {rank}] no OK received: I am the new leader");
                        leader = rank;
                        for (int i = 0; i < rank; i++)
                        {
                            if (i != crashed)
                                comm.Send(rank, i, TagCoord);
                        }
                        comm.Send(rank, crashed, TagShutdown);
                    }
                    if (started && gotOk && leader < 0 && Now() > deadline + 2 * Timeout)
                        // no coordinator announced: try again
                        StartElection();
                }
                Console.WriteLine($"[rank {rank}] accepts rank {leader} as leader");
            }

            comm.Barrier();
            int[] leaders = comm.Gather(leader, 0);
            if (rank == 0)
            {
                int expected = size - 1;
                if (expected == crashed) expected--; // highest surviving rank
                bool ok = true;
                for (int r = 0; r < size; r++)
                    if (r != crashed && leaders[r] != expected) ok = false;
                Console.WriteLine($"[bully] crashed={crashed} new_leader={expected} " +
                    $"expected={expected} all_agree={(ok ? "YES" : "NO")}");
            }
            Status late;
            while ((late = comm.ImmediateProbe(Communicator.anySource,
                Communicator.anyTag)) != null)
                comm.Receive<int>(late.Source, late.Tag); // discard stray late messages
        }
    }
}
