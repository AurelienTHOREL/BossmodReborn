namespace BossMod;

public enum MultiboxRole
{
    Disabled,
    Main,
    Alt
}

public enum MultiboxTransport
{
    MMF,
    TCP
}

[ConfigDisplay(Name = "Multibox sync configuration", Order = 8)]
sealed class MultiboxConfig : ConfigNode
{
    [PropertyDisplay("Sync role")]
    [PropertyCombo(["Disabled", "Main", "Alt"])]
    public MultiboxRole Role = MultiboxRole.Disabled;

    [PropertyDisplay("Transport")]
    [PropertyCombo(["MMF (same machine)", "TCP (network)"])]
    public MultiboxTransport Transport = MultiboxTransport.MMF;

    [PropertyDisplay("Sync group name")]
    public string SyncGroupName = "Default";

    [PropertyDisplay("TCP address", tooltip: "IP address for TCP. Main: bind address (0.0.0.0 for all). Alt: main's IP.")]
    public string TcpAddress = "127.0.0.1";

    [PropertyDisplay("TCP port")]
    public int TcpPort = 42069;

    [PropertyDisplay("Sync position overrides to alts")]
    public bool SyncPositionOverrides = true;

    [PropertyDisplay("Sync DiveEnd invuln commands to alts")]
    public bool SyncDiveEndInvuln = true;

    [PropertyDisplay("Macro number for 'Run Macro' button (0-99, individual)")]
    public int SyncMacroNumber = 50;

    [PropertyDisplay("Macro number for 'RSR Off' button (0-99, individual)")]
    public int RsrOffMacroNumber = 99;

    [PropertyDisplay("Auto RSR Off on wipe (combat end while dead)")]
    public bool RsrOffOnWipe = true;
}
