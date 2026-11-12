using System;
using System.Diagnostics;
using MPI;

class Deadlock
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

            string mode = args.Length > 0 ? args[0] : "deadlock";

            if (mode == "deadlock")
            {
                ReproduceDeadlock(comm, rank);
            }
            else if (mode == "reordered")
            {
                ReorderedFix(comm, rank);
            }
            else if (mode == "nonblocking")
            {
                NonBlockingFix(comm, rank);
            }
            else
            {
                if (rank == 0)
                    Console.WriteLine("Usage: [deadlock|reordered|nonblocking]");
            }
        }
    }

    static void ReproduceDeadlock(Intracommunicator comm, int rank)
    {
        // Large payload to exceed eager buffering limit (8 MB)
        int payloadSize = 8 * 1024 * 1024; // 8 MiB
        byte[] buffer = new byte[payloadSize];

        for (int i = 0; i < Math.Min(1000, payloadSize); i++)
            buffer[i] = (byte)(rank + i);

        Console.WriteLine($"Rank {rank}: Starting deadlock scenario with {payloadSize / (1024 * 1024)} MiB payload...");
        Console.WriteLine($"Rank {rank}: Attempting to send to peer before posting receive (UNSAFE)");

        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            int peer = 1 - rank; // rank 0 -> peer 1, rank 1 -> peer 0

            // BOTH ranks send BEFORE receiving - this can deadlock!
            comm.Send(buffer, peer, 0);
            Console.WriteLine($"Rank {rank}: Send completed");

            // Now try to receive
            comm.Receive(peer, 0, ref buffer);
            Console.WriteLine($"Rank {rank}: Receive completed");

            sw.Stop();
            Console.WriteLine($"Rank {rank}: Completed in {sw.ElapsedMilliseconds} ms (buffering may have saved us)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Rank {rank}: Exception - {ex.Message}");
        }
    }

    static void ReorderedFix(Intracommunicator comm, int rank)
    {
        int payloadSize = 8 * 1024 * 1024; // 8 MiB
        byte[] sendBuffer = new byte[payloadSize];
        byte[] recvBuffer = new byte[payloadSize];

        // Initialize send buffer
        for (int i = 0; i < Math.Min(1000, payloadSize); i++)
            sendBuffer[i] = (byte)(rank + 42 + i);

        Console.WriteLine($"Rank {rank}: Starting REORDERED fix with {payloadSize / (1024 * 1024)} MiB payload");

        Stopwatch sw = Stopwatch.StartNew();
        int peer = 1 - rank;

        // Asymmetric ordering breaks the deadlock
        if (rank == 0)
        {
            // Rank 0: Send first, then receive
            Console.WriteLine($"Rank {rank}: Sending first...");
            comm.Send(sendBuffer, peer, 0);
            Console.WriteLine($"Rank {rank}: Receiving...");
            comm.Receive(peer, 0, ref recvBuffer);
        }
        else // rank == 1
        {
            // Rank 1: Receive first, then send
            Console.WriteLine($"Rank {rank}: Receiving first...");
            comm.Receive(peer, 0, ref recvBuffer);
            Console.WriteLine($"Rank {rank}: Sending...");
            comm.Send(sendBuffer, peer, 0);
        }

        sw.Stop();

        // Verify data correctness
        bool correct = recvBuffer[0] == (byte)(peer + 42);
        Console.WriteLine($"Rank {rank}: REORDERED fix completed in {sw.ElapsedMilliseconds} ms, Data correct: {correct}");
    }

    static void NonBlockingFix(Intracommunicator comm, int rank)
    {
        int payloadSize = 8 * 1024 * 1024; // 8 MiB
        byte[] sendBuffer = new byte[payloadSize];
        byte[] recvBuffer = new byte[payloadSize];

        // Initialize send buffer
        for (int i = 0; i < Math.Min(1000, payloadSize); i++)
            sendBuffer[i] = (byte)(rank + 99 + i);

        Console.WriteLine($"Rank {rank}: Starting NON-BLOCKING fix with {payloadSize / (1024 * 1024)} MiB payload");

        Stopwatch sw = Stopwatch.StartNew();
        int peer = 1 - rank;

        // Post non-blocking receive
        Console.WriteLine($"Rank {rank}: Posting non-blocking receive...");
        Request recvRequest = comm.ImmediateReceive(peer, 0, recvBuffer);

        // Post non-blocking send
        Console.WriteLine($"Rank {rank}: Posting non-blocking send...");
        Request sendRequest = comm.ImmediateSend(sendBuffer, peer, 0);

        // Wait for both to complete
        Console.WriteLine($"Rank {rank}: Waiting for both operations to complete...");
        recvRequest.Wait();
        sendRequest.Wait();

        sw.Stop();

        // Verify data correctness
        bool correct = recvBuffer[0] == (byte)(peer + 99);
        Console.WriteLine($"Rank {rank}: NON-BLOCKING fix completed in {sw.ElapsedMilliseconds} ms, Data correct: {correct}");
    }
}
