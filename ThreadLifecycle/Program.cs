using System;
using System.Threading;

class Program
{
    // simple worker that just sleeps so we can watch the thread block and then finish
    static void Worker()
    {
        Thread.Sleep(200);  // 200 ms is long enough to clearly see the WaitSleepJoin state
    }

    static void Main()
    {
        Thread t = new Thread(Worker);

        // --- state 1: right after creating the thread object, before calling Start() ---
        // no OS thread exists yet, so .NET reports Unstarted
        // conceptually this is the "New" state in the thread lifecycle
        Console.WriteLine($"after new Thread()   -> thread state: {t.ThreadState}  (conceptual: New)");

        t.Start();  // this is where the OS actually creates the thread

        // --- state 2: right after Start() ---
        // the thread is now alive and waiting for the scheduler to give it a core
        // .NET usually reports Running here, though it could still be in the ready queue
        Console.WriteLine($"after t.Start()      -> thread state: {t.ThreadState}  (conceptual: Runnable/Ready or Running)");

        // give the worker 50 ms to get scheduled and reach its Thread.Sleep(200)
        Thread.Sleep(50);

        // --- state 3: while the worker is blocked inside Thread.Sleep(200) ---
        // the worker is not on any core, it's waiting for the timer to expire
        // .NET uses WaitSleepJoin to cover all blocking waits (sleep, lock, join)
        Console.WriteLine($"while worker sleeps  -> thread state: {t.ThreadState}  (conceptual: Blocked/Waiting)");

        // wait here until the worker is done
        t.Join();

        // --- state 4: after Join() returns ---
        // the worker method returned, the OS thread is gone
        // .NET reports Stopped which maps to the Terminated conceptual state
        Console.WriteLine($"after t.Join()       -> thread state: {t.ThreadState}  (conceptual: Terminated)");

        Console.WriteLine();
        Console.WriteLine("lifecycle: New -> Runnable/Ready -> Running -> Blocked/Waiting -> Terminated");
    }
}
