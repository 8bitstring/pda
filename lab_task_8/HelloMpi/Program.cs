using System;
using MPI;

class HelloMpi
{
    static void Main(string[] args)
    {
        using (new MPI.Environment(ref args))
        {
            Intracommunicator comm = Communicator.world;

            int rank = comm.Rank;
            int size = comm.Size;
            string name = MPI.Environment.ProcessorName;

            // order is not defined
            Console.WriteLine($"Hello from rank {rank} of {size} on {name}");
            comm.Barrier();

            // ordered report through rank 0
            string line = $"rank {rank} of {size} on {name}";
            if (rank == 0)
            {
                Console.WriteLine("--- ordered report collected by rank 0 ---");
                Console.WriteLine(line);
                for (int src = 1; src < size; src++)
                    Console.WriteLine(comm.Receive<string>(src, 0));
            }
            else
            {
                comm.Send(line, 0, 0);
            }
        }
    }
}
