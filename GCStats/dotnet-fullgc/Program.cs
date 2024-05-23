using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Diagnostics.Tracing;


namespace dotnet_fullgc
{
    internal class Program
    {
        private const string name = "dotnet-fullgc";
        private static Version version = Assembly.GetExecutingAssembly().GetName().Version;

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

            Int64 clientSequenceNumber = 0;
            if (args.Length > 2)
            {
                if (args[1] == "-csn")
                {
                    if (!Int64.TryParse(args[2], out clientSequenceNumber))
                    {
                        ShowHelp(name, version.ToString(), $"Invalid client sequence number '{args[2]}'");
                        return;
                    }
                }
            }
            else
            {
                ShowHelp(name, version.ToString(), $"Invalid option '{args[1]}'");
                return;
            }

            ShowHeader(name, version.ToString());
            try
            {
                TriggerGC(pid, clientSequenceNumber);
            }
            catch (Exception x)
            {
                ShowError(x.Message);
            }
        }

        private static void TriggerGC(int processId, Int64 clientSequenceNumber)
        {
            // Note: this client sequence number is not processed before .NET 9
            Dictionary<string, string> arguments = new Dictionary<string, string>();
            arguments.Add("Id", clientSequenceNumber.ToString());
            var providers = new List<EventPipeProvider>()
            {
                new EventPipeProvider(
                    "Microsoft-Windows-DotNETRuntime",
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.GCHeapCollect,
                    arguments
                    ),
            };
            var client = new DiagnosticsClient(processId);

            using (var session = client.StartEventPipeSession(providers, false))
            {
                Console.WriteLine("Sending command...");
                Task streamTask = Task.Run(() =>
                {
                    // without source to process, session.Stop() will not return
                    var source = new EventPipeEventSource(session.EventStream);

                    // No GCStart event is received in that case  :^(
                    //ClrTraceEventParser clrParser = new ClrTraceEventParser(source);
                    //clrParser.GCStart += OnGCStart;

                    try
                    {
                        source.Process();
                    }
                    catch (Exception e)
                    {
                        ShowError($"Error encountered while processing event source: {e.Message}");
                    }
                });

                Task inputTask = Task.Run(() =>
                {
                    Thread.Sleep(1000);
                    session.Stop();
                });

                Task.WaitAny(streamTask, inputTask);

                Console.WriteLine("Full GC has been triggered");
            }
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
            "Trigger a full garbage collections in a .NET application";
        private static string Help =
            "Usage:  {0} <process ID> [-csn <client sequence number>]" + Environment.NewLine +
            "";

    }
}
