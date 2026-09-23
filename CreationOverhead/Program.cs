using System;
using System.Diagnostics;
using System.Threading;

class Program
{
    const int Iterations = 50;  // 50 rounds gives a stable average without taking forever

    // the thread just returns immediately — we only care about the overhead of creating it
    static void TrivialWork() { }

    static void Main()
    {
        // find the ProcessLab exe to use as our trivial child target
        // it's built in the sibling folder under Release
        string processLabExe = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "ProcessLab", "bin", "Release", "net10.0", "ProcessLab.exe"));

        // fallback in case the relative path above doesn't resolve correctly
        if (!System.IO.File.Exists(processLabExe))
        {
            string? root = System.IO.Path.GetDirectoryName(
                System.IO.Path.GetDirectoryName(
                    System.IO.Path.GetDirectoryName(
                        System.IO.Path.GetDirectoryName(AppContext.BaseDirectory))));

            if (root != null)
                processLabExe = System.IO.Path.Combine(
                    root, "ProcessLab", "bin", "Release", "net10.0", "ProcessLab.exe");
        }

        if (!System.IO.File.Exists(processLabExe))
        {
            Console.Error.WriteLine($"can't find ProcessLab.exe at: {processLabExe}");
            Console.Error.WriteLine("build it first with: dotnet build ../ProcessLab -c Release");
            return;
        }

        Console.WriteLine($"child exe: {processLabExe}\n");

        // ---- process creation benchmark ----
        Console.WriteLine($"timing process creation ({Iterations} iterations)...");

        var processSw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            var psi = new ProcessStartInfo
            {
                FileName              = processLabExe,
                UseShellExecute       = false,
                RedirectStandardOutput = true,  // suppress child output so it doesn't clutter timing
                RedirectStandardError  = true
            };
            psi.ArgumentList.Add("--child");

            using Process? p = Process.Start(psi);
            p?.WaitForExit();  // wait fully — we want end-to-end creation + teardown time
        }
        processSw.Stop();

        // ---- thread creation benchmark ----
        Console.WriteLine($"timing thread creation ({Iterations} iterations)...");

        var threadSw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            Thread t = new Thread(TrivialWork);
            t.Start();
            t.Join();  // join immediately so we measure the full create + teardown cycle
        }
        threadSw.Stop();

        // ---- print the comparison ----
        double avgProcessMs = processSw.Elapsed.TotalMilliseconds / Iterations;
        double avgThreadMs  = threadSw.Elapsed.TotalMilliseconds  / Iterations;
        double ratio        = avgProcessMs / avgThreadMs;

        Console.WriteLine();
        Console.WriteLine($"avg process creation : {avgProcessMs:F3} ms");
        Console.WriteLine($"avg thread creation  : {avgThreadMs:F3} ms");
        Console.WriteLine($"process creation was {ratio:F1}x more expensive than thread creation");
    }
}
