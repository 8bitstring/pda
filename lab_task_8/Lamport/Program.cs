using System;
using System.Linq;
using MPI;

class LamportDemo
{
    const int Rounds = 4;
    const int Local = 0, Send = 1, Recv = 2;
    static readonly string[] TypeName = { "local", "send ", "recv " };

    // an event is stored as 5 consecutive ints: clock, rank, type, peer, message timestamp
    static void Log(int[] log, ref int n, int clock, int rank, int type, int peer, int ts)
    {
        log[n++] = clock; log[n++] = rank; log[n++] = type; log[n++] = peer; log[n++] = ts;
    }

    static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            Intracommunicator comm = Communicator.world;
            int rank = comm.Rank, size = comm.Size;

            if (size < 2)
            {
                if (rank == 0) Console.WriteLine("run with at least 2 ranks");
                return;
            }

            // local + send + receive per round
            int perRank = 3 * Rounds;
            int[] mine = new int[perRank * 5];
            int n = 0, clock = 0;

            for (int r = 0; r < Rounds; r++)
            {
                clock++; // local event
                Log(mine, ref n, clock, rank, Local, -1, 0);

                // a different partner in every round
                int shift = 1 + r % (size - 1);
                int dest = (rank + shift) % size;
                int src = (rank - shift + size) % size;

                // send event: increase the clock and stamp the outgoing message
                clock++;
                int outTs = clock;
                Log(mine, ref n, clock, rank, Send, dest, outTs);

                int inTs;
                comm.SendReceive(outTs, dest, r, src, r, out inTs);

                // receive event: max(own, received) + 1
                clock = Math.Max(clock, inTs) + 1;
                Log(mine, ref n, clock, rank, Recv, src, inTs);
            }

            int[][] all = comm.Gather(mine, 0); // only rank 0 receives the logs

            if (rank == 0)
            {
                var events = all.SelectMany(a => Enumerable.Range(0, a.Length / 5)
                    .Select(i => (Clock: a[5 * i], Rank: a[5 * i + 1], Type: a[5 * i + 2],
                        Peer: a[5 * i + 3], Ts: a[5 * i + 4]))).ToList();

                var merged = events.OrderBy(e => e.Clock).ThenBy(e => e.Rank).ToList();

                int shown = Math.Min(24, merged.Count);
                Console.WriteLine($"merged log (first {shown} of {merged.Count} events):");
                Console.WriteLine(" clock rank event peer msg_ts");
                foreach (var e in merged.Take(24))
                    Console.WriteLine($" {e.Clock,5} {e.Rank,4} {TypeName[e.Type]} " +
                        $"{e.Peer,4} {e.Ts,6}");

                int violations = 0, messages = 0;

                // receive must follow send
                foreach (var e in events.Where(e => e.Type == Recv))
                {
                    messages++;
                    var s = events.Where(x => x.Type == Send && x.Rank == e.Peer &&
                        x.Peer == e.Rank && x.Clock == e.Ts).ToList();
                    if (s.Count != 1 || e.Clock <= s[0].Clock) violations++;
                }

                // each rank's own clock must increase
                for (int r = 0; r < size; r++)
                    for (int k = 1; k < perRank; k++)
                        if (all[r][5 * k] <= all[r][5 * (k - 1)]) violations++;

                Console.WriteLine($"ranks={size} events={events.Count} messages={messages} " +
                    $"causal_violations={violations} " +
                    $"consistent={(violations == 0 ? "YES" : "NO")}");
            }
        }
    }
}
