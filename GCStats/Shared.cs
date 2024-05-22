using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;


class Shared
{
    public static void WriteWithColor(string text, ConsoleColor color)
    {
        var current = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = current;
    }

    public static ConsoleColor GetGenColor(int gen)
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


    public const int gen_initial = 0;          // indicates the initial gen to condemn.
    public const int gen_final_per_heap = 1;   // indicates the final gen to condemn per heap.
    public const int gen_alloc_budget = 2;     // indicates which gen's budget is exceeded.

    private const int InitialGenMask = 0x0 + 0x1 + 0x2;

    public static int GetGen(int val, int reason)
    {
        int gen = (val >> 2 * reason) & InitialGenMask;
        return gen;
    }


    public static void DumpGlobalMechanisms(GCGlobalMechanisms gm)
    {
        string[] mechanisms = $"{gm}".Split(", ");
        int count = mechanisms.Length;
        for (int i = 0; i < count; i++)
        {
            if (mechanisms[i] == "Compaction")
            {
                Shared.WriteWithColor(mechanisms[i], ConsoleColor.DarkYellow);
            }
            else
            if (mechanisms[i] == "Concurrent")
            {
                Shared.WriteWithColor(mechanisms[i], ConsoleColor.DarkGreen);
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
    public enum CondemnReasonCondition
    {
        no_condemn_reason_condition = 0,
        induced_fullgc = 0x1,
        expand_fullgc = 0x2,
        high_mem = 0x4,
        very_high_mem = 0x8,
        low_ephemeral = 0x10,
        low_card = 0x20,
        eph_high_frag = 0x40,
        max_high_frag = 0x80,
        max_high_frag_e = 0x100,
        max_high_frag_m = 0x200,
        max_high_frag_vm = 0x400,
        max_gen1 = 0x800,
        before_oom = 0x1000,
        gen2_too_small = 0x2000,
        induced_noforce = 0x4000,
        before_bgc = 0x8000,
        almost_max_alloc = 0x10000,
        joined_avoid_unproductive = 0x20000,
        joined_pm_induced_fullgc = 0x40000,
        joined_pm_alloc_loh = 0x80000,
        joined_gen1_in_pm = 0x100000,
        joined_limit_before_oom = 0x200000,
        joined_limit_loh_frag = 0x400000,
        joined_limit_loh_reclaim = 0x800000,
        joined_servo_initial = 0x1000000,
        joined_servo_ngc = 0x2000000,
        joined_servo_bgc = 0x4000000,
        joined_servo_postpone = 0x8000000,
        joined_stress_mix = 0x10000000,
        joined_stress = 0x20000000,
        gcrc_max = 0x40000000
    }
}