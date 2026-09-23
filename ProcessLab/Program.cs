using System;
using System.Diagnostics;

class Program
{
    static void Main(string[] args)
    {
        // if the program is called with --child, run the child logic
        // otherwise just act as the parent
        if (args.Length > 0 && args[0] == "--child")
            RunAsChild();
        else
            RunAsParent();
    }

    static void RunAsChild()
    {
        // each process has its own PID assigned by the OS
        Console.WriteLine($"[child] pid = {Environment.ProcessId}");

        int counter = 100;
        counter += 50;  // child adds 50, so it ends up at 150

        Console.WriteLine($"[child] final counter = {counter}");
    }

    static void RunAsParent()
    {
        Console.WriteLine($"[parent] pid = {Environment.ProcessId}");

        int counter = 100;
        counter += 1;  // parent only adds 1, so it ends at 101

        // find the path of the currently running exe so we can relaunch it as the child
        // Environment.ProcessPath works on .NET 6 and above
        string exePath = Environment.ProcessPath
                         ?? Process.GetCurrentProcess().MainModule!.FileName;

        // set up how we want to launch the child process
        var psi = new ProcessStartInfo
        {
            FileName       = exePath,
            UseShellExecute = false  // false so the child writes to the same terminal window
        };
        psi.ArgumentList.Add("--child");  // this is what tells the child branch to run

        Console.WriteLine($"[parent] launching child from: {exePath}");

        // actually start the child process
        using Process? child = Process.Start(psi);

        if (child == null)
        {
            Console.Error.WriteLine("[parent] something went wrong, child process didn't start");
            return;
        }

        // wait here until the child is completely done (like waitpid in C)
        child.WaitForExit();

        Console.WriteLine($"[parent] child finished with exit code {child.ExitCode}");
        Console.WriteLine($"[parent] final counter = {counter}");

        // the key point: child modified its own copy of counter, parent's is unchanged
        Console.WriteLine("[parent] both counters were modified independently because each process has its own address space");
    }
}
