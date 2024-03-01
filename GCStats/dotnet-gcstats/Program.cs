using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using System.Threading;
using System.Reflection;

namespace GCStats
{
    internal class Program
    {
        private const string name = "dotnet-gcstats";
        private static Version version = Assembly.GetExecutingAssembly().GetName().Version;
        private static bool _isVerbose = false;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp(name, version.ToString(), "No process ID specified");
                return;
            }

            int pid = -1;
            if (!int.TryParse(args[0], out pid))
            {
                ShowHelp(name, version.ToString(), $"Invalid specified process ID '{args[0]}'");
                return;
            }

            if (args.Length > 1)
            {
                if (args[1] == "-v")
                {
                    _isVerbose = true;
                }
                else
                {
                    ShowHelp(name, version.ToString(), $"Invalid option '{args[1]}'");
                    return;
                }
            }

            ShowHeader(name, version.ToString());
            try
            {
                PrintEventsLive(pid);
            }
            catch (Exception x)
            {
               ShowError(x.Message);
            }
        }


        public static void PrintEventsLive(int processId)
        {
            var providers = new List<EventPipeProvider>()
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime",
                    EventLevel.Informational, (long)ClrTraceEventParser.Keywords.GC),
            };
            var client = new DiagnosticsClient(processId);

            using (var session = client.StartEventPipeSession(providers, false))
            {
                Console.WriteLine();

                Task streamTask = Task.Run(() =>
                {
                    var source = new EventPipeEventSource(session.EventStream);

                    ClrTraceEventParser clrParser = new ClrTraceEventParser(source);
                    clrParser.GCPerHeapHistory += OnGCPerHeapHistory;
                    clrParser.GCStart += OnGCStart;
                    clrParser.GCGlobalHeapHistory += OnGCGlobalHeapHistory;

                    // to get all other events
                    //clrParser.All += ClrParser_All;

                    try
                    {
                        source.Process();
                    }
                    catch (Exception e)
                    {
                        ShowError($"Error encountered while processing events: {e.Message}");
                    }
                });

                Task inputTask = Task.Run(() =>
                {
                    while (Console.ReadKey().Key != ConsoleKey.Enter)
                    {
                        Thread.Sleep(100);
                    }
                    session.Stop();
                });

                Task.WaitAny(streamTask, inputTask);
            }
        }

        private static void ClrParser_All(TraceEvent eventData)
        {
            if (
                (eventData.ID == (TraceEventID)1)   ||
                (eventData.ID == (TraceEventID)204) ||
                (eventData.ID == (TraceEventID)205)
                )
            {
                return;
            }

            Console.WriteLine($"{eventData.ID,4} - {eventData.OpcodeName} | {eventData.EventName}");
        }

        private static ConsoleColor GetGenColor(int gen)
        {
            switch (gen)
            {
                case 2: return ConsoleColor.Blue;
                case 1: return ConsoleColor.DarkCyan;
                case 0: return ConsoleColor.Cyan;
                default:
                    return ConsoleColor.White;
            }
        }

        private static void OnGCStart(GCStartTraceData payload)
        {
            Console.WriteLine();
            Console.Write($"_______#{payload.Count}");
            WriteWithColor($" gen{payload.Depth}", GetGenColor(payload.Depth));

            GCReasonNet8 reason = (GCReasonNet8)payload.Reason;
            if (
                (reason == GCReasonNet8.Induced) ||
                (reason == GCReasonNet8.InducedNotForced) ||
                (reason == GCReasonNet8.InducedCompacting) ||
                (reason == GCReasonNet8.InducedAggressive)
                )
            {
                Console.Write(" = ");
                WriteWithColor($"{(GCReasonNet8)payload.Reason}", ConsoleColor.Red);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($" = {(GCReasonNet8)payload.Reason}");
            }
        }

        private static void DumpGlobalMechanisms(GCGlobalMechanisms gm)
        {
            string[] mechanisms = $"{gm}".Split(", ");
            int count = mechanisms.Length;
            for ( int i = 0; i < count; i++ )
            {
                if (mechanisms[i] == "Compaction")
                {
                    WriteWithColor(mechanisms[i], ConsoleColor.DarkYellow);
                }
                else
                if (mechanisms[i] == "Concurrent")
                {
                    WriteWithColor(mechanisms[i], ConsoleColor.DarkGreen);
                }
                else
                {
                    Console.Write(mechanisms[i]);
                }

                if (i < count - 1)
                {
                    Console.Write(", ");
                }
            }
        }

        private static void OnGCGlobalHeapHistory(GCGlobalHeapHistoryTraceData payload)
        {
            Console.Write($".......<  ");
            WriteWithColor($"gen{payload.CondemnedGeneration}", GetGenColor(payload.CondemnedGeneration));
            Console.Write($" {payload.PauseMode} [");
            DumpGlobalMechanisms(payload.GlobalMechanisms);
            Console.WriteLine($"] mem pressure = {payload.MemoryPressure}");
        }

        private static void OnGCPerHeapHistory(GCPerHeapHistoryTraceData payload)
        {
            if (payload.HeapIndex == 0)
            {
                var condemnReasonCondition = (payload.CondemnReasons1 == 0) ? "" : $"{(CondemnReasonCondition)payload.CondemnReasons1}";
                int startGen = GetGen(payload.CondemnReasons0, gen_initial);
                int finalGen = GetGen(payload.CondemnReasons0, gen_final_per_heap);
                var startGenColor = GetGenColor(startGen);
                var finalGenColor = GetGenColor(finalGen);
                Console.Write($"  condemn ");
                WriteWithColor($"gen{startGen}", startGenColor);
                Console.Write($" -> ");
                WriteWithColor($"gen{finalGen}", finalGenColor);
                Console.WriteLine($" [budget gen{GetGen(payload.CondemnReasons0, gen_alloc_budget)}] {condemnReasonCondition}");

                if (_isVerbose)
                {
                    Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
                }
            }

            if (!_isVerbose)
            {
                return;
            }

            Console.WriteLine($"      heap #{payload.HeapIndex,2}       Gen0         Gen1         Gen2          LOH          POH");
            Console.WriteLine("-----------------------------------------------------------------------------");
            var entryCount = payload.EntriesInGenData;
            var gen0 = payload.GenData(Gens.Gen0);
            var gen1 = payload.GenData(Gens.Gen1);
            var gen2 = payload.GenData(Gens.Gen2);
            var loh = payload.GenData(Gens.GenLargeObj);
            var poh = payload.GenData(Gens.GenPinObj);

            Console.WriteLine($"        Budget {(gen0.Budget),10}   {(gen1.Budget),10}   {(gen2.Budget),10}   {(loh.Budget),10}   {(poh.Budget),10}   ");
            Console.WriteLine($"    Begin size {(gen0.SizeBefore),10}   {(gen1.SizeBefore),10}   {(gen2.SizeBefore),10}   {(loh.SizeBefore),10}   {(poh.SizeBefore),10}   ");
            Console.WriteLine($"Begin obj size {(gen0.ObjSpaceBefore),10}   {(gen1.ObjSpaceBefore),10}   {(gen2.ObjSpaceBefore),10}   {(loh.ObjSpaceBefore),10}   {(poh.ObjSpaceBefore),10}   ");
            Console.WriteLine($"    Final size {(gen0.SizeAfter),10}   {(gen1.SizeAfter),10}   {(gen2.SizeAfter),10}   {(loh.SizeAfter),10}   {(poh.SizeAfter),10}   ");
            Console.WriteLine($" Promoted size {(gen0.PinnedSurv + gen0.NonePinnedSurv),10}   {(gen1.PinnedSurv + gen1.NonePinnedSurv),10}   {(gen2.PinnedSurv + gen2.NonePinnedSurv),10}   {(loh.PinnedSurv + loh.NonePinnedSurv),10}   {(poh.PinnedSurv + poh.NonePinnedSurv),10}   ");
            Console.WriteLine($" Fragmentation {(gen0.Fragmentation),10}   {(gen1.Fragmentation),10}   {(gen2.Fragmentation),10}   {(loh.Fragmentation),10}   {(poh.Fragmentation),10}   ");
            Console.WriteLine();
        }

        private static void WriteWithColor(string text, ConsoleColor color)
        {
            var current = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = current;
        }



        private const int gen_initial = 0;          // indicates the initial gen to condemn.
        private const int gen_final_per_heap = 1;   // indicates the final gen to condemn per heap.
        private const int gen_alloc_budget = 2;     // indicates which gen's budget is exceeded.

        private const int InitialGenMask = 0x0 + 0x1 + 0x2;

        static int GetGen(int val, int reason)
        {
            int gen = (val >> 2 * reason) & InitialGenMask;
            return gen;
        }


        public enum GCReasonNet8
        {
            AllocSmall,
            Induced,
            LowMemory,
            Empty,
            AllocLarge,
            OutOfSpaceSOH,
            OutOfSpaceLOH,
            InducedNotForced,
            Internal,
            InducedLowMemory,
            InducedCompacting,
            LowMemoryHost,
            PMFullGC,
            LowMemoryHostBlocking,
            //
            // new ones
            BgcTuningSOH,
            BgcTuningLOH,
            BgcStepping,
            InducedAggressive,
        }

        [Flags]
        enum CondemnReasonCondition
        {
            no_condemn_reason_condition = 0,
            induced_fullgc              = 0x1,
            expand_fullgc               = 0x2,
            high_mem                    = 0x4,
            very_high_mem               = 0x8,
            low_ephemeral               = 0x10,
            low_card                    = 0x20,
            eph_high_frag               = 0x40,
            max_high_frag               = 0x80,
            max_high_frag_e             = 0x100,
            max_high_frag_m             = 0x200,
            max_high_frag_vm            = 0x400,
            max_gen1                    = 0x800,
            before_oom                  = 0x1000,
            gen2_too_small              = 0x2000,
            induced_noforce             = 0x4000,
            before_bgc                  = 0x8000,
            almost_max_alloc            = 0x10000,
            joined_avoid_unproductive   = 0x20000,
            joined_pm_induced_fullgc    = 0x40000,
            joined_pm_alloc_loh         = 0x80000,
            joined_gen1_in_pm           = 0x100000,
            joined_limit_before_oom     = 0x200000,
            joined_limit_loh_frag       = 0x400000,
            joined_limit_loh_reclaim    = 0x800000,
            joined_servo_initial        = 0x1000000,
            joined_servo_ngc            = 0x2000000,
            joined_servo_bgc            = 0x4000000,
            joined_servo_postpone       = 0x8000000,
            joined_stress_mix           = 0x10000000,
            joined_stress               = 0x20000000,
            gcrc_max                    = 0x40000000
        }

        static void ShowHelp(string name, string version, string message)
        {
            Console.WriteLine(string.Format(Header, name, version));
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine(message);
            Console.WriteLine();
            Console.WriteLine(Help, name);
        }

        static void ShowError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine(message);
        }

        static void ShowHeader(string name, string version)
        {
            Console.WriteLine(string.Format(Header, name, version));
        }


        private static string Header =
            "{0} v{1}" + Environment.NewLine +
            "by Christophe Nasarre" + Environment.NewLine +
            "Displays live statistics about garbage collections in a .NET application";
        private static string Help =
            "Usage:  {0} <process ID> [-v]" + Environment.NewLine +
            "";
    }
}