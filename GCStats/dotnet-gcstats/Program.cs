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

        private static void OnGCStart(GCStartTraceData payload)
        {
            Console.WriteLine();
            Console.Write($"_______#{payload.Count}");
            Shared.WriteWithColor($" gen{payload.Depth}", Shared.GetGenColor(payload.Depth));

            Shared.GCReasonNet8 reason = (Shared.GCReasonNet8)payload.Reason;
            if (
                (reason == Shared.GCReasonNet8.Induced) ||
                (reason == Shared.GCReasonNet8.InducedNotForced) ||
                (reason == Shared.GCReasonNet8.InducedCompacting) ||
                (reason == Shared.GCReasonNet8.InducedAggressive)
                )
            {
                Console.Write(" = ");
                Shared.WriteWithColor($"{(Shared.GCReasonNet8)payload.Reason}", ConsoleColor.Red);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($" = {(Shared.GCReasonNet8)payload.Reason}");
            }
        }

        private static void OnGCGlobalHeapHistory(GCGlobalHeapHistoryTraceData payload)
        {
            Console.Write($".......<  ");
            Shared.WriteWithColor($"gen{payload.CondemnedGeneration}", Shared.GetGenColor(payload.CondemnedGeneration));
            Console.Write($" {payload.PauseMode} [");
            Shared.DumpGlobalMechanisms(payload.GlobalMechanisms);
            Console.WriteLine($"] mem pressure = {payload.MemoryPressure}");
        }

        private static void OnGCPerHeapHistory(GCPerHeapHistoryTraceData payload)
        {
            if (payload.HeapIndex == 0)
            {
                var condemnReasonCondition = (payload.CondemnReasons1 == 0) ? "" : $"{(Shared.CondemnReasonCondition)payload.CondemnReasons1}";
                int startGen = Shared.GetGen(payload.CondemnReasons0, Shared.gen_initial);
                int finalGen = Shared.GetGen(payload.CondemnReasons0, Shared.gen_final_per_heap);
                var startGenColor = Shared.GetGenColor(startGen);
                var finalGenColor = Shared.GetGenColor(finalGen);
                Console.Write($"  condemn ");
                Shared.WriteWithColor($"gen{startGen}", startGenColor);
                Console.Write($" -> ");
                Shared.WriteWithColor($"gen{finalGen}", finalGenColor);
                Console.WriteLine($" [budget gen{Shared.GetGen(payload.CondemnReasons0, Shared.gen_alloc_budget)}] {condemnReasonCondition}");

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