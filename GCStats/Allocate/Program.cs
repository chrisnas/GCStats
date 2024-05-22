using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;

namespace Allocate
{
    internal class Program
    {
        static void Main(string[] args)
        {
            int maxGarbageCollectionsCount = 10;

            if (args.Length == 1)
            {
                maxGarbageCollectionsCount = int.Parse(args[0]);
            }

            Console.WriteLine($"Initial GCSettings.LatencyMode = {GCSettings.LatencyMode}");
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            Console.WriteLine($"GCSettings.LatencyMode = {GCSettings.LatencyMode}");
            Console.WriteLine($"Server GC = {GCSettings.IsServerGC}");
            Console.WriteLine($"pid = {Process.GetCurrentProcess().Id}");
            Console.ReadLine();

            // trigger induced GC
            //GC.Collect(2, GCCollectionMode.Forced);
            GC.Collect(2, GCCollectionMode.Aggressive);

            // allocate to trigger 10 garbage collections
            const int LEN = 1_000_000;
            byte[][] list = new byte[LEN][];
            for (int i = 0; i < LEN; ++i)
            {
                list[i] = new byte[25000];
                if (i % 100 == 0)
                {
                    Console.WriteLine("Allocated 100 arrays");
                    Thread.Sleep(500);
                    if (GC.CollectionCount(0) >= maxGarbageCollectionsCount)
                    {
                        Console.WriteLine($"Leaving at i = {i}");
                        Console.WriteLine($"  #gen0 = {GC.CollectionCount(0)}");
                        Console.WriteLine($"  #gen1 = {GC.CollectionCount(1)}");
                        Console.WriteLine($"  #gen2 = {GC.CollectionCount(2)}");
                        break;
                    }
                }
            }

            Console.WriteLine("Press any key to exit...");
            Console.Read();
        }
    }
}