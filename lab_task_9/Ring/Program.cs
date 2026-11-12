using System;
using System.Diagnostics;
using MPI;

class Ring
{
    static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            Intracommunicator comm = Communicator.world;
            int rank = comm.Rank;
            int size = comm.Size;

            if (size < 2)
            {
                if (rank == 0)
                    Console.WriteLine("This program requires at least 2 ranks");
                return;
            }

            // Calculate ring topology
            int next = (rank + 1) % size;
            int previous = (rank - 1 + size) % size;

            if (rank == 0)
            {
                Console.WriteLine($"=== N-Rank Ring Communication ({size} ranks) ===");
                Console.WriteLine($"Each rank will pass a token to its successor");
            }

            Console.WriteLine($"Rank {rank}: previous={previous}, next={next}");

            comm.Barrier();
            Stopwatch sw = Stopwatch.StartNew();

            // Token structure: [sequence_number, accumulated_sum]
            int[] sendToken = new int[2];
            int[] recvToken = new int[2];

            if (rank == 0)
            {
                // Rank 0 initiates the token
                sendToken[0] = 1;      // sequence number
                sendToken[1] = rank;   // accumulated sum starts with rank 0's value
                
                Console.WriteLine($"Rank {rank}: Initiating token with seq={sendToken[0]}, sum={sendToken[1]}");
            }

            // Perform one complete circuit around the ring
            if (rank == 0)
            {
                // Rank 0: Send first, then receive
                Request sendReq = comm.ImmediateSend(sendToken, next, 0);
                Request recvReq = comm.ImmediateReceive(previous, 0, recvToken);
                
                sendReq.Wait();
                recvReq.Wait();
                
                sw.Stop();
                
                int expectedSum = 0;
                for (int i = 0; i < size; i++)
                    expectedSum += i;
                
                bool correct = (recvToken[1] == expectedSum);
                
                Console.WriteLine($"\n=== Results ===");
                Console.WriteLine($"Rank {rank}: Received token seq={recvToken[0]}, sum={recvToken[1]}");
                Console.WriteLine($"Expected sum: {expectedSum}");
                Console.WriteLine($"Correct: {correct}");
                Console.WriteLine($"Total time: {sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"Total messages: {size}");
            }
            else
            {
                // Other ranks: Receive, update, send
                Request recvReq = comm.ImmediateReceive(previous, 0, recvToken);
                recvReq.Wait();
                
                // Update token
                sendToken[0] = recvToken[0] + 1;  // increment sequence
                sendToken[1] = recvToken[1] + rank; // add own rank to sum
                
                Console.WriteLine($"Rank {rank}: Received token seq={recvToken[0]}, sum={recvToken[1]} -> Forwarding seq={sendToken[0]}, sum={sendToken[1]}");
                
                Request sendReq = comm.ImmediateSend(sendToken, next, 0);
                sendReq.Wait();
            }

            comm.Barrier();

            if (rank == 0)
            {
                Console.WriteLine("\n=== Ring Communication Complete ===");
                Console.WriteLine($"Token successfully circulated through all {size} ranks");
            }
        }
    }
}
