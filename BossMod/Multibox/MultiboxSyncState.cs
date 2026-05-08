using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace BossMod;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MultiboxSlotData
{
    public long ContentId;
    public byte Assignment; // PartyRolesConfig.Assignment enum value
    public float TargetX;
    public float TargetZ;
    public byte Flags; // bit 0: has position, bit 1: use DiveEnd invuln
    public byte ResolutionFlags; // bit 0: sprint, bit 1: cancel cast, bit 2: stop autoattack
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MultiboxSyncState
{
    public const int MaxSlots = 8;
    public const int PresetNameMaxBytes = 64;
    public const int TotalSize = 512; // padded to round size

    public long FrameSequence;      // monotonic counter for freshness detection
    public long MainContentId;      // main player's content ID
    public ushort TerritoryId;      // current zone
    public byte PhaseIndex;         // boss module phase
    public uint StateId;            // state machine state ID
    public float StateTime;         // time within state
    public float MainX;             // main player's world X (used by TP pulse to land alts on main)
    public float MainY;             // main player's world Y
    public float MainZ;             // main player's world Z

    public MultiboxSlotData Slot0;
    public MultiboxSlotData Slot1;
    public MultiboxSlotData Slot2;
    public MultiboxSlotData Slot3;
    public MultiboxSlotData Slot4;
    public MultiboxSlotData Slot5;
    public MultiboxSlotData Slot6;
    public MultiboxSlotData Slot7;

    public byte DiveEndFlags;       // bit 0: enable invuln, bit 1: disable invuln (pulse)
    public byte CommandFlags;       // bit 0: AI on, bit 1: AI off, bit 2: preset sync, bit 3: macro A, bit 4: macro B, bit 5: TP-to-main pulse, bit 6: reserved, bit 7: leave duty
    public byte ResolutionActive;   // 1 if resolution engine is driving positions
    public byte ResolutionFlags;    // bit 0: sprint, bit 1: cancel cast, bit 2: stop autoattack (per-slot in SlotData)
    public byte MacroNumberA;       // macro number for "Run Macro" command (0-99)
    public byte MacroNumberB;       // macro number for "RSR Off" command (0-99)
    public fixed byte ActivePresetName[PresetNameMaxBytes];

    [UnscopedRef]
    public ref MultiboxSlotData Slot(int index)
    {
        switch (index)
        {
            case 0: return ref Slot0;
            case 1: return ref Slot1;
            case 2: return ref Slot2;
            case 3: return ref Slot3;
            case 4: return ref Slot4;
            case 5: return ref Slot5;
            case 6: return ref Slot6;
            case 7: return ref Slot7;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public void SetPresetName(string? name)
    {
        fixed (byte* ptr = ActivePresetName)
        {
            if (string.IsNullOrEmpty(name))
            {
                ptr[0] = 0;
                return;
            }
            var target = new Span<byte>(ptr, PresetNameMaxBytes);
            var written = Encoding.UTF8.GetBytes(name.AsSpan(), target[..(PresetNameMaxBytes - 1)]);
            target[written] = 0;
        }
    }

    public readonly string GetPresetName()
    {
        fixed (byte* ptr = ActivePresetName)
        {
            var len = 0;
            while (len < PresetNameMaxBytes && ptr[len] != 0)
                len++;
            return len > 0 ? Encoding.UTF8.GetString(ptr, len) : "";
        }
    }

    public static int FindSlot(long contentId, ref MultiboxSyncState state)
    {
        for (var i = 0; i < MaxSlots; ++i)
        {
            if (state.Slot(i).ContentId == contentId)
                return i;
        }
        return -1;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MultiboxAltReportSlot
{
    public const int NameMaxBytes = 32;

    public long ContentId;
    public byte ClassJob;              // Class enum value
    public byte Flags;                 // bit 0: AI active, bit 1: has active preset (not null/ForceDisable)
    public long LastSyncSequence;      // FrameSequence from last processed main state
    public fixed byte PresetName[NameMaxBytes];
    public fixed byte PlanName[NameMaxBytes];
    public fixed byte BuildNumber[NameMaxBytes];

    public void SetPresetName(string? name)
    {
        fixed (byte* ptr = PresetName)
            WriteFixedString(ptr, NameMaxBytes, name);
    }

    public readonly string GetPresetName()
    {
        fixed (byte* ptr = PresetName)
            return ReadFixedString(ptr, NameMaxBytes);
    }

    public void SetPlanName(string? name)
    {
        fixed (byte* ptr = PlanName)
            WriteFixedString(ptr, NameMaxBytes, name);
    }

    public readonly string GetPlanName()
    {
        fixed (byte* ptr = PlanName)
            return ReadFixedString(ptr, NameMaxBytes);
    }

    public void SetBuildNumber(string? name)
    {
        fixed (byte* ptr = BuildNumber)
            WriteFixedString(ptr, NameMaxBytes, name);
    }

    public readonly string GetBuildNumber()
    {
        fixed (byte* ptr = BuildNumber)
            return ReadFixedString(ptr, NameMaxBytes);
    }

    private static void WriteFixedString(byte* ptr, int maxBytes, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            ptr[0] = 0;
            return;
        }
        var target = new Span<byte>(ptr, maxBytes);
        var written = Encoding.UTF8.GetBytes(name.AsSpan(), target[..(maxBytes - 1)]);
        target[written] = 0;
    }

    private static string ReadFixedString(byte* ptr, int maxBytes)
    {
        var len = 0;
        while (len < maxBytes && ptr[len] != 0)
            len++;
        return len > 0 ? Encoding.UTF8.GetString(ptr, len) : "";
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MultiboxAltReport
{
    public const int MaxSlots = 8;
    public const int TotalSize = 1024; // padded

    public MultiboxAltReportSlot Slot0;
    public MultiboxAltReportSlot Slot1;
    public MultiboxAltReportSlot Slot2;
    public MultiboxAltReportSlot Slot3;
    public MultiboxAltReportSlot Slot4;
    public MultiboxAltReportSlot Slot5;
    public MultiboxAltReportSlot Slot6;
    public MultiboxAltReportSlot Slot7;

    [UnscopedRef]
    public ref MultiboxAltReportSlot Slot(int index)
    {
        switch (index)
        {
            case 0: return ref Slot0;
            case 1: return ref Slot1;
            case 2: return ref Slot2;
            case 3: return ref Slot3;
            case 4: return ref Slot4;
            case 5: return ref Slot5;
            case 6: return ref Slot6;
            case 7: return ref Slot7;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    [UnscopedRef]
    public ref MultiboxAltReportSlot FindSlot(long contentId)
    {
        for (var i = 0; i < MaxSlots; ++i)
        {
            if (Slot(i).ContentId == contentId)
                return ref Slot(i);
        }
        return ref Slot(0); // fallback
    }

    public bool HasSlot(long contentId)
    {
        for (var i = 0; i < MaxSlots; ++i)
        {
            if (Slot(i).ContentId == contentId)
                return true;
        }
        return false;
    }
}
