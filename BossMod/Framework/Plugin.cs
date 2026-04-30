using BossMod.Autorotation;
using Dalamud.Common;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.IO;
using System.Reflection;

namespace BossMod;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "BossMod Reborn";

    public static readonly string BuildNumber = typeof(Plugin).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "BuildNumber")?.Value ?? "unknown";

    private readonly ICommandManager CommandManager;

    private readonly RotationDatabase _rotationDB;
    private readonly WorldState _ws;
    private readonly AIHints _hints;
    private readonly BossModuleManager _bossmod;
    private readonly ZoneModuleManager _zonemod;
    private readonly AIHintsBuilder _hintsBuilder;
    private readonly MovementOverride _movementOverride;
    private readonly ActionManagerEx _amex;
    private readonly WorldStateGameSync _wsSync;
    private readonly RotationModuleManager _rotation;
    private readonly AI.AIManager _ai;
    private readonly AI.Broadcast _broadcast;
    private readonly IPCProvider _ipc;
    private readonly DTRProvider _dtr;
    private readonly MultiboxManager _mbox;
    private readonly MultiboxConfig _mboxConfig;
    private readonly PartyRolesConfig _mboxPrc;
    private IMultiboxSyncWriter? _mboxWriter;
    private IMultiboxSyncReader? _mboxReader;
    private IMultiboxAltReportReader? _mboxAltReportReader;
    private IMultiboxAltReportWriter? _mboxAltReportWriter;
    private MultiboxAltReport _altReport;
    private readonly MultiboxPositionEditor _mboxPosEditor;
    private MultiboxSyncState _mboxState;
    private byte _mboxPrevDiveEndFlags;
    private byte _mboxPrevCommandFlags;
    private bool _pendingDiveEndEnable;
    private bool _pendingDiveEndDisable;
    private TimeSpan _prevUpdateTime;
    private DateTime _throttleJump;
    private DateTime _throttleInteract;
    private DateTime _throttleFateSync;
    private DateTime _throttleLeaveDuty;

    // windows
    private readonly ConfigUI _configUI; // TODO: should be a proper window!
    private readonly BossModuleMainWindow _wndBossmod;
    private readonly BossModuleHintsWindow _wndBossmodHints;
    private readonly ZoneModuleWindow _wndZone;
    private readonly ReplayManagementWindow _wndReplay;
    private readonly UIRotationWindow _wndRotation;
    private readonly MainDebugWindow _wndDebug;
    private readonly RotationSolverRebornModule _rsr;

    public unsafe Plugin(IDalamudPluginInterface dalamud, ICommandManager commandManager, ISigScanner sigScanner, IDataManager dataManager)
    {
        if (!dalamud.ConfigDirectory.Exists)
            dalamud.ConfigDirectory.Create();
        var dalamudRoot = dalamud.GetType().Assembly.
                GetType("Dalamud.Service`1", true)!.MakeGenericType(dalamud.GetType().Assembly.GetType("Dalamud.Dalamud", true)!).
                GetMethod("Get")!.Invoke(null, BindingFlags.Default, null, [], null);
        var dalamudStartInfo = dalamudRoot?.GetType().GetProperty("StartInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(dalamudRoot) as DalamudStartInfo;
        var gameVersion = dalamudStartInfo?.GameVersion?.ToString() ?? "unknown";

        InteropGenerator.Runtime.Resolver.GetInstance.Setup(sigScanner.SearchBase, gameVersion, new(dalamud.ConfigDirectory.FullName + "/cs.json"));
        FFXIVClientStructs.Interop.Generated.Addresses.Register();
        InteropGenerator.Runtime.Resolver.GetInstance.Resolve();

        dalamud.Create<Service>();
        Service.LogHandlerDebug = msg => Service.Logger.Debug(msg);
        Service.LogHandlerVerbose = msg => Service.Logger.Verbose(msg);
        Service.LuminaGameData = dataManager.GameData;
        Service.WindowSystem = new("bmr");
        //Service.Device = pluginInterface.UiBuilder.Device;
        Service.Condition.ConditionChange += OnConditionChanged;
        Service.IconFont = UiBuilder.IconFont;
        MultiboxUnlock.Exec();
        Camera.Instance = new();

        Service.Config.Initialize();
        Service.Config.LoadFromFile(dalamud.ConfigFile);
        Service.Config.Modified.Subscribe(() => Task.Run(() => Service.Config.SaveToFile(dalamud.ConfigFile)));

        CommandManager = commandManager;
        CommandManager.AddHandler("/bmr", new CommandInfo(OnCommand) { HelpMessage = "Show boss mod settings UI" });

        ActionDefinitions.Instance.UnlockCheck = QuestUnlocked; // ensure action definitions are initialized and set unlock check functor (we don't really store the quest progress in clientstate, for now at least)

        var qpf = (ulong)FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->PerformanceCounterFrequency;
        _rotationDB = new(new(dalamud.ConfigDirectory.FullName + "/autorot"), new(dalamud.AssemblyLocation.DirectoryName! + "/DefaultRotationPresets.json"));
        _ws = new(qpf, gameVersion);
        _rsr = new(dalamud);
        _hints = new();
        _bossmod = new(_ws);
        _zonemod = new(_ws);
        _hintsBuilder = new(_ws, _bossmod, _zonemod, _rsr);
        _movementOverride = new(dalamud);
        _amex = new(_ws, _hints, _movementOverride);
        _wsSync = new(_ws, _amex);
        _rotation = new(_rotationDB, _bossmod, _hints);
        _ai = new(_rotation, _amex, _movementOverride);
        _broadcast = new();
        _ipc = new(_bossmod, _hints, _rotation, _amex, _movementOverride, _ai, _hintsBuilder.Obstacles);
        _dtr = new(_rotation, _ai);
        _mbox = new(_rotation, _ws);
        _mboxConfig = Service.Config.Get<MultiboxConfig>();
        _mboxPrc = Service.Config.Get<PartyRolesConfig>();
        InitMultiboxSync();
        _ws.Actors.InCombatChanged.Subscribe(OnCombatChangedMbox);
        _mboxPosEditor = new(_bossmod, _ws, _rotation,
            () => _altReport,
            () => _mboxState.FrameSequence,
            flags => _mboxState.CommandFlags |= flags,
            flags =>
            {
                _mboxState.DiveEndFlags |= flags;
                if ((flags & 1) != 0)
                    _pendingDiveEndEnable = true;
                if ((flags & 2) != 0)
                    _pendingDiveEndDisable = true;
            });
        _wndBossmod = new(_bossmod, _zonemod);
        _wndBossmodHints = new(_bossmod, _zonemod);
        _wndZone = new(_zonemod);
        var config = Service.Config.Get<ReplayManagementConfig>();
        var replayDir = string.IsNullOrEmpty(config.ReplayFolder) ? dalamud.ConfigDirectory.FullName + "/replays" : config.ReplayFolder;
        _wndReplay = new ReplayManagementWindow(_ws, _bossmod, _rotationDB, new DirectoryInfo(replayDir));
        _configUI = new(Service.Config, _ws, new DirectoryInfo(replayDir), _rotationDB);
        config.Modified.ExecuteAndSubscribe(() => _wndReplay.UpdateLogDirectory());
        _wndRotation = new(_rotation, _amex, () => OpenConfigUI("Autorotation presets"));
        _wndDebug = new(_ws, _rotation, _zonemod, _amex, _movementOverride, _hintsBuilder, dalamud);

        dalamud.UiBuilder.DisableAutomaticUiHide = true;
        dalamud.UiBuilder.Draw += DrawUI;
        dalamud.UiBuilder.OpenMainUi += () => OpenConfigUI();
        dalamud.UiBuilder.OpenConfigUi += () => OpenConfigUI();
    }

    public void Dispose()
    {
        Service.Condition.ConditionChange -= OnConditionChanged;
        _wndDebug.Dispose();
        _wndRotation.Dispose();
        _wndReplay.Dispose();
        _wndZone.Dispose();
        _wndBossmodHints.Dispose();
        _wndBossmod.Dispose();
        _mboxPosEditor.Dispose();
        _configUI.Dispose();
        _mboxWriter?.Dispose();
        _mboxReader?.Dispose();
        if (_mboxAltReportReader is IDisposable altReader && altReader != _mboxWriter as IDisposable)
            altReader.Dispose();
        if (_mboxAltReportWriter is IDisposable altWriter && altWriter != _mboxReader as IDisposable)
            altWriter.Dispose();
        _mbox.Dispose();
        _dtr.Dispose();
        _ipc.Dispose();
        _ai.Dispose();
        _rotation.Dispose();
        _wsSync.Dispose();
        _amex.Dispose();
        _movementOverride.Dispose();
        _hintsBuilder.Dispose();
        _zonemod.Dispose();
        _bossmod.Dispose();
        ActionDefinitions.Instance.Dispose();
        CommandManager.RemoveHandler("/bmr");
        GarbageCollection();
    }

    private void OnCommand(string cmd, string args)
    {
        Service.Log($"OnCommand: {cmd} {args}");
        var split = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 0)
        {
            OpenConfigUI();
            return;
        }

        switch (split[0].ToUpperInvariant())
        {
            case "D":
                _wndDebug.IsOpen = true;
                _wndDebug.BringToFront();
                break;
            case "CFG":
                var output = Service.Config.ConsoleCommand(new ArraySegment<string>(split, 1, split.Length - 1));
                foreach (var msg in output)
                    Service.ChatGui.Print(msg);
                break;
            case "GC":
                GarbageCollection();
                break;
            case "R":
                HandleReplayCommand(split);
                break;
            case "AR":
                ParseAutorotationCommands(split);
                break;
            case "RESETCOLORS":
                ResetColors();
                break;
            case "RESTOREROTATION":
                ToggleRestoreRotation();
                break;
            case "TOGGLEANTICHEAT":
                ToggleAnticheat();
                break;
            case "MBOX":
                HandleMultiboxCommand(split);
                break;
        }
    }

    private bool HandleReplayCommand(string[] messageData)
    {
        if (messageData.Length == 1)
            _wndReplay.SetVisible(!_wndReplay.IsOpen);
        else
        {
            switch (messageData[1].ToUpperInvariant())
            {
                case "ON":
                    _wndReplay.StartRecording("");
                    break;
                case "OFF":
                    _wndReplay.StopRecording();
                    break;
                default:
                    Service.ChatGui.Print($"[BMR] Unknown replay command: {messageData[1]}");
                    break;
            }
        }
        return false;
    }

    private static void ResetColors()
    {
        var defaultConfig = ColorConfig.DefaultConfig;
        var currentConfig = Service.Config.Get<ColorConfig>();
        var fields = typeof(ColorConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);

        for (var i = 0; i < fields.Length; ++i)
        {
            ref var field = ref fields[i];
            var value = field.GetValue(defaultConfig);
            if (value is Color or Color[])
                field.SetValue(currentConfig, value);
        }

        currentConfig.Modified.Fire();
        Service.Log("Colors have been reset to default values.");
    }

    private static bool ToggleAnticheat()
    {
        var config = Service.Config.Get<ActionTweaksConfig>();
        config.ActivateAnticheat = !config.ActivateAnticheat;
        config.Modified.Fire();
        Service.Log($"The animation lock anticheat is now {(config.ActivateAnticheat ? "enabled" : "disabled")}");
        return true;
    }

    private static bool ToggleRestoreRotation()
    {
        var config = Service.Config.Get<ActionTweaksConfig>();
        config.RestoreRotation = !config.RestoreRotation;
        config.Modified.Fire();
        Service.Log($"Restore character orientation after action use is now {(config.RestoreRotation ? "enabled" : "disabled")}");
        return true;
    }

    private void OpenConfigUI(string showTab = "")
    {
        _configUI.ShowTab(showTab);
        _ = new UISimpleWindow("BossModReborn", _configUI.Draw, true, new(300, 300));
    }

    private void DrawUI()
    {
        var tsStart = DateTime.Now;
        var moveImminent = _movementOverride.IsMoveRequested() && (!ActionManagerEx.Config.PreventMovingWhileCasting || _movementOverride.IsForceUnblocked());

        _dtr.Update();
        Camera.Instance?.Update();
        _wsSync.Update(_prevUpdateTime);
        _bossmod.Update();
        _zonemod.ActiveModule?.Update();
        _hintsBuilder.Update(_hints, PartyState.PlayerSlot, moveImminent);
        _amex.QueueManualActions();
        _rotation.Update(_amex.AnimationLockDelayEstimate, _movementOverride.IsMoving(), Service.Condition[ConditionFlag.DutyRecorderPlayback]);
        UpdateMultiboxSyncAlt();
        _ai.Update();
        _broadcast.Update();
        UpdateMultiboxSync();
        DispatchMultiboxDiveEnd();
        _amex.FinishActionGather();

        var uiHidden = Service.GameGui.GameUiHidden || Service.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Service.Condition[ConditionFlag.WatchingCutscene78] || Service.Condition[ConditionFlag.WatchingCutscene];
        if (!uiHidden)
        {
            Service.WindowSystem?.Draw();
        }

        ExecuteHints();

        Camera.Instance?.DrawWorldPrimitives();
        _prevUpdateTime = DateTime.Now - tsStart;
    }

    private unsafe bool QuestUnlocked(uint link)
    {
        // see ActionManager.IsActionUnlocked
        var gameMain = FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance();
        return link == 0
            || Service.LuminaRow<Lumina.Excel.Sheets.TerritoryType>(gameMain->CurrentTerritoryTypeId)?.TerritoryIntendedUse.RowId == 31u // deep dungeons check is hardcoded in game
            || FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(link);
    }

    private unsafe void ExecuteHints()
    {
        _movementOverride.DesiredDirection = _hints.ForcedMovement;
        _movementOverride.MisdirectionThreshold = _hints.MisdirectionThreshold;
        _movementOverride.DesiredSpinDirection = _hints.SpinDirection;

        var targetSystem = FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance();
        SetTarget(_hints.ForcedTarget, &targetSystem->Target);
        SetTarget(_hints.ForcedFocusTarget, &targetSystem->FocusTarget);

        foreach (var s in _hints.StatusesToCancel)
        {
            var res = FFXIVClientStructs.FFXIV.Client.Game.StatusManager.ExecuteStatusOff(s.statusId, s.sourceId != default ? (uint)s.sourceId : 0xE0000000);
            Service.Log($"[ExecHints] Canceling status {s.statusId} from {s.sourceId:X} -> {res}");
        }
        if (_hints.WantJump && _ws.CurrentTime > _throttleJump)
        {
            //Service.Log($"[ExecHints] Jumping...");
            FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance()->UseAction(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 2);
            _throttleJump = _ws.FutureTime(0.1d);
        }

        if (_hints.ShouldLeaveDuty && _ws.CurrentTime >= _throttleLeaveDuty)
        {
            EventFramework.LeaveCurrentContent(false);
            _throttleLeaveDuty = _ws.FutureTime(1d);
        }

        if ((AI.AIManager.Instance?.Beh != null || Autorotation.MiscAI.NormalMovement.Instance != null) && CheckInteractRange(_ws.Party.Player(), _hints.InteractWithTarget))
        {
            // many eventobj interactions "immediately" start some cast animation (delayed by server roundtrip), and if we keep trying to move toward the target after sending the interact request, it will be canceled and force us to start over
            _movementOverride.DesiredDirection = default;

            if (_amex.EffectiveAnimationLock == 0 && _ws.CurrentTime >= _throttleInteract)
            {
                FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance()->InteractWithObject(GetActorObject(_hints.InteractWithTarget), false);
                _throttleInteract = _ws.FutureTime(1.1d);
            }
        }

        HandleFateSync();
    }

    private unsafe void SetTarget(Actor? target, FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject** targetPtr)
    {
        if (target == null || !target.IsTargetable)
            return;

        var obj = GetActorObject(target);

        // 50 in-game units is the maximum distance before nameplates stop rendering (making the mob effectively untargetable)
        // targeting a mob that isn't visible is bad UX
        if (_ws.Party.Player() is { } player)
        {
            var distSq = (player.PosRot.XYZ() - target.PosRot.XYZ()).LengthSquared();
            if (distSq < 2500f)
                *targetPtr = obj;
        }
    }

    private unsafe bool CheckInteractRange(Actor? player, Actor? target)
    {
        var playerObj = GetActorObject(player);
        var targetObj = GetActorObject(target);
        if (playerObj == null || targetObj == null)
            return false;

        // treasure chests have no client-side interact range check at all; just assume they use the standard "small" range, seems to be accurate from testing
        return targetObj->ObjectKind is FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Treasure
            ? player?.DistanceToHitbox(target) <= 2.09f
            : EventFramework.Instance()->CheckInteractRange(playerObj, targetObj, 1, false);
    }

    private unsafe void HandleFateSync()
    {
        var fm = FateManager.Instance();
        var fate = fm->CurrentFate;
        if (fate == null)
            return;

        var shouldDoSomething = _hints.WantFateSync switch
        {
            AIHints.FateSync.Enable => !fm->IsSyncedToFate(fate),
            AIHints.FateSync.Disable => fm->IsSyncedToFate(fate),
            _ => false
        };

        if (shouldDoSomething && _ws.CurrentTime >= _throttleFateSync)
        {
            fm->LevelSync();
            _throttleFateSync = _ws.FutureTime(0.5f);
        }
    }

    private unsafe FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* GetActorObject(Actor? actor)
    {
        if (actor == null)
            return null;

        var obj = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObjectManager.Instance()->Objects.IndexSorted[actor.SpawnIndex].Value;
        if (obj == null)
            return null;

        if (obj->EntityId != actor.InstanceID)
            Service.Log($"[ExecHints] Unexpected actor: expected {actor.InstanceID:X} at #{actor.SpawnIndex}, but found {obj->EntityId:X}");

        return obj;
    }

    private void ParseAutorotationCommands(string[] cmd)
    {
        switch (cmd.Length > 1 ? cmd[1].ToUpperInvariant() : "")
        {
            case "CLEAR":
                Service.Log($"Console: clearing autorotation preset '{_rotation.Preset?.Name ?? "<n/a>"}'");
                _rotation.Preset = null;
                break;
            case "DISABLE":
                Service.Log($"Console: force-disabling from preset '{_rotation.Preset?.Name ?? "<n/a>"}'");
                _rotation.Preset = RotationModuleManager.ForceDisable;
                break;
            case "SET":
                if (cmd.Length <= 2)
                    Service.Log("Specify an autorotation preset name.");
                else
                    ParseAutorotationSetCommand([.. cmd.Skip(1)], false);
                break;
            case "TOGGLE":
                ParseAutorotationSetCommand(cmd.Length > 2 ? [.. cmd.Skip(1)] : [""], true);
                break;
            case "UI":
                _wndRotation.SetVisible(!_wndRotation.IsOpen);
                break;
        }
    }

    private void ParseAutorotationSetCommand(string[] presetName, bool toggle)
    {
        if (presetName.Length < 2)
        {
            Service.Log("No valid preset name provided.");
            return;
        }

        var userInput = string.Join(" ", presetName.Skip(1)).Trim();
        if (userInput == "null" || string.IsNullOrWhiteSpace(userInput))
        {
            _rotation.Preset = null;
            Service.Log("Disabled AI autorotation preset.");
            return;
        }
        var normalizedInput = userInput.ToUpperInvariant();
        var preset = _rotation.Database.Presets.AllPresets
            .FirstOrDefault(p => p.Name.Trim().Equals(normalizedInput, StringComparison.OrdinalIgnoreCase))
            ?? RotationModuleManager.ForceDisable;
        if (preset != null)
        {
            var newPreset = toggle && _rotation.Preset == preset ? null : preset;
            Service.Log($"Console: {(toggle ? "toggle" : "set")} changes preset from '{_rotation.Preset?.Name ?? "<n/a>"}' to '{newPreset?.Name ?? "<n/a>"}'");
            _rotation.Preset = newPreset;
        }
        else
        {
            Service.ChatGui.PrintError($"Failed to find preset '{presetName}'");
        }
    }

    private static void OnConditionChanged(ConditionFlag flag, bool value)
    {
        Service.Log($"Condition change: {flag}={value}");
    }

    private void HandleMultiboxCommand(string[] split)
    {
        if (split.Length < 2)
        {
            _mboxPosEditor.IsOpen ^= true;
            if (_mboxPosEditor.IsOpen)
                _mboxPosEditor.BringToFront();
            return;
        }

        switch (split[1].ToUpperInvariant())
        {
            case "UI":
                _mboxPosEditor.IsOpen ^= true;
                if (_mboxPosEditor.IsOpen)
                    _mboxPosEditor.BringToFront();
                break;
            case "SYNCAI":
                if (_mboxConfig.Role != MultiboxRole.Main)
                {
                    Service.ChatGui.Print("[BMR] Multibox sync commands are only available on Main.");
                    return;
                }
                if (split.Length > 2)
                {
                    switch (split[2].ToUpperInvariant())
                    {
                        case "ON":
                            _mboxState.CommandFlags |= 1 | 4;
                            Service.ChatGui.Print("[BMR] Multibox: sent AI on + preset sync pulse to alts.");
                            break;
                        case "OFF":
                            _mboxState.CommandFlags |= 2;
                            Service.ChatGui.Print("[BMR] Multibox: sent AI off pulse to alts.");
                            break;
                        default:
                            Service.ChatGui.Print($"[BMR] Unknown syncai option: {split[2]}. Use on/off.");
                            break;
                    }
                }
                else
                {
                    var aiOn = AI.AIManager.Instance?.Beh != null;
                    _mboxState.CommandFlags |= (byte)(aiOn ? (1 | 4) : 2);
                    Service.ChatGui.Print($"[BMR] Multibox: sent AI {(aiOn ? "on + preset sync" : "off")} pulse to alts.");
                }
                break;
            case "SYNCPRESET":
                if (_mboxConfig.Role != MultiboxRole.Main)
                {
                    Service.ChatGui.Print("[BMR] Multibox sync commands are only available on Main.");
                    return;
                }
                _mboxState.CommandFlags |= 4;
                Service.ChatGui.Print($"[BMR] Multibox: sent preset sync pulse ({_rotation.Preset?.Name ?? "none"}).");
                break;
            case "POSITIONS":
                if (split.Length > 2)
                {
                    switch (split[2].ToUpperInvariant())
                    {
                        case "ON":
                            _mboxConfig.SyncPositionOverrides = true;
                            break;
                        case "OFF":
                            _mboxConfig.SyncPositionOverrides = false;
                            break;
                        default:
                            Service.ChatGui.Print($"[BMR] Unknown positions option: {split[2]}. Use on/off.");
                            return;
                    }
                }
                else
                {
                    _mboxConfig.SyncPositionOverrides = !_mboxConfig.SyncPositionOverrides;
                }
                _mboxConfig.Modified.Fire();
                Service.ChatGui.Print($"[BMR] Multibox position sync is now {(_mboxConfig.SyncPositionOverrides ? "enabled" : "disabled")}.");
                break;
            case "DIVEEND":
                if (split.Length > 2)
                {
                    switch (split[2].ToUpperInvariant())
                    {
                        case "ON":
                            _mboxConfig.SyncDiveEndInvuln = true;
                            break;
                        case "OFF":
                            _mboxConfig.SyncDiveEndInvuln = false;
                            break;
                        default:
                            Service.ChatGui.Print($"[BMR] Unknown diveend option: {split[2]}. Use on/off.");
                            return;
                    }
                }
                else
                {
                    _mboxConfig.SyncDiveEndInvuln = !_mboxConfig.SyncDiveEndInvuln;
                }
                _mboxConfig.Modified.Fire();
                Service.ChatGui.Print($"[BMR] Multibox DiveEnd invuln sync is now {(_mboxConfig.SyncDiveEndInvuln ? "enabled" : "disabled")}.");
                break;
            case "DE":
                if (_amex.BlockTerritoryTransport)
                {
                    _mboxState.DiveEndFlags |= 2;
                    _pendingDiveEndDisable = true;
                    Service.ChatGui.Print("[BMR] Multibox: DiveEnd OFF sent to all.");
                }
                else
                {
                    _mboxState.DiveEndFlags |= 1;
                    _pendingDiveEndEnable = true;
                    Service.ChatGui.Print("[BMR] Multibox: DiveEnd ON sent to all.");
                }
                break;
            case "CLEAR":
                _mboxPosEditor.ClearAllPositions();
                Service.ChatGui.Print("[BMR] Multibox: cleared all position overrides.");
                break;
            case "STATUS":
                Service.ChatGui.Print($"[BMR] Multibox role: {_mboxConfig.Role}");
                Service.ChatGui.Print($"[BMR] Transport: {_mboxConfig.Transport}");
                if (_mboxConfig.Transport == MultiboxTransport.TCP)
                    Service.ChatGui.Print($"[BMR] TCP: {_mboxConfig.TcpAddress}:{_mboxConfig.TcpPort}");
                else
                    Service.ChatGui.Print($"[BMR] MMF group: {_mboxConfig.SyncGroupName}");
                Service.ChatGui.Print($"[BMR] Position sync: {(_mboxConfig.SyncPositionOverrides ? "on" : "off")}");
                Service.ChatGui.Print($"[BMR] DiveEnd sync: {(_mboxConfig.SyncDiveEndInvuln ? "on" : "off")}");
                break;
            default:
                Service.ChatGui.Print($"[BMR] Unknown mbox command: {split[1]}. Use: ui, syncai, syncpreset, positions, diveend, de, clear, status.");
                break;
        }
    }

    private void DispatchMultiboxDiveEnd()
    {
        if (_pendingDiveEndEnable)
        {
            _pendingDiveEndEnable = false;
            if (_rotation.Player is { } p)
            {
                _amex.BlockTerritoryTransport = true;
                _amex.ExecuteCommandComplexLocation(ExecuteCommandComplexFlag.DiveEnd, new Vector3(p.PosRot.X, p.PosRot.Y, p.PosRot.Z));
                Service.Log("[MultiboxSync] Executed DiveEnd invuln on alt");
            }
        }

        if (_pendingDiveEndDisable)
        {
            _pendingDiveEndDisable = false;
            _amex.BlockTerritoryTransport = false;
            _amex.ExecuteCommand(201, 0);
            Service.Log("[MultiboxSync] Disabled DiveEnd invuln on alt");
        }
    }

    internal static unsafe void ExecuteGameMacro(int number)
    {
        if (number is < 0 or > 99)
            return;
        var macroModule = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureMacroModule.Instance();
        var shellModule = FFXIVClientStructs.FFXIV.Client.UI.Shell.RaptureShellModule.Instance();
        if (macroModule == null || shellModule == null)
            return;
        var macro = macroModule->GetMacro(0, (uint)number);
        if (macro == null)
            return;
        shellModule->ExecuteMacro(macro);
        Service.Log($"[MultiboxSync] Executed macro #{number}");
    }

    private void OnCombatChangedMbox(Actor actor)
    {
        if (!_mboxConfig.RsrOffOnWipe)
            return;
        if (_ws.Party.Members[PartyState.PlayerSlot].InstanceId != actor.InstanceID)
            return;
        if (actor.InCombat || !actor.IsDead)
            return;

        Service.Log("[MultiboxSync] Wipe detected, firing RSR Off");
        ExecuteGameMacro(_mboxConfig.RsrOffMacroNumber);
        if (_mboxConfig.Role == MultiboxRole.Main)
            _mboxState.CommandFlags |= 0x10;
    }

    private void InitMultiboxSync()
    {
        try
        {
            switch (_mboxConfig.Role)
            {
                case MultiboxRole.Main:
                    if (_mboxConfig.Transport == MultiboxTransport.TCP)
                    {
                        var server = new TcpSyncServer(_mboxConfig.TcpAddress, _mboxConfig.TcpPort);
                        _mboxWriter = server;
                        _mboxAltReportReader = server;
                    }
                    else
                    {
                        _mboxWriter = new MultiboxSyncWriter(_mboxConfig.SyncGroupName);
                        _mboxAltReportReader = new MultiboxAltReportReader(_mboxConfig.SyncGroupName);
                    }
                    break;
                case MultiboxRole.Alt:
                    if (_mboxConfig.Transport == MultiboxTransport.TCP)
                    {
                        var client = new TcpSyncClient(_mboxConfig.TcpAddress, _mboxConfig.TcpPort);
                        _mboxReader = client;
                        _mboxAltReportWriter = client;
                    }
                    else
                    {
                        _mboxReader = new MultiboxSyncReader(_mboxConfig.SyncGroupName);
                        _mboxAltReportWriter = new MultiboxAltReportWriter(_mboxConfig.SyncGroupName);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Service.Log($"[MultiboxSync] Failed to initialize: {ex.Message}");
        }
    }

    private void UpdateMultiboxSyncAlt()
    {
        if (_mboxConfig.Role != MultiboxRole.Alt || _mboxReader == null)
            return;

        if (!_mboxReader.TryRead(out var state))
            return;

        var player = _rotation.Player;
        if (player == null)
            return;

        if (_mboxConfig.SyncDiveEndInvuln)
        {
            if ((state.DiveEndFlags & 1) != 0 && (_mboxPrevDiveEndFlags & 1) == 0)
            {
                _pendingDiveEndEnable = true;
                Service.Log("[MultiboxSync] DiveEnd enable pulse received");
            }
            if ((state.DiveEndFlags & 2) != 0 && (_mboxPrevDiveEndFlags & 2) == 0)
            {
                _pendingDiveEndDisable = true;
                Service.Log("[MultiboxSync] DiveEnd disable pulse received");
            }
            _mboxPrevDiveEndFlags = state.DiveEndFlags;
        }

        var aiOnPulse = (state.CommandFlags & 1) != 0 && (_mboxPrevCommandFlags & 1) == 0;
        var aiOffPulse = (state.CommandFlags & 2) != 0 && (_mboxPrevCommandFlags & 2) == 0;
        var presetPulse = (state.CommandFlags & 4) != 0 && (_mboxPrevCommandFlags & 4) == 0;
        var macroAPulse = (state.CommandFlags & 0x08) != 0 && (_mboxPrevCommandFlags & 0x08) == 0;
        var macroBPulse = (state.CommandFlags & 0x10) != 0 && (_mboxPrevCommandFlags & 0x10) == 0;
        var leavePulse = (state.CommandFlags & 0x80) != 0 && (_mboxPrevCommandFlags & 0x80) == 0;

        if (aiOnPulse && AI.AIManager.Instance != null)
        {
            var aiCfg = Service.Config.Get<AI.AIConfig>();
            AI.AIManager.Instance.SwitchToFollow(aiCfg.FollowSlot);
            Service.Log("[MultiboxSync] AI on pulse received");
        }
        if (aiOffPulse)
        {
            AI.AIManager.Instance?.SwitchToIdle();
            Service.Log("[MultiboxSync] AI off pulse received");
        }
        if (presetPulse && AI.AIManager.Instance != null)
        {
            var presetName = state.GetPresetName();
            if (!string.IsNullOrEmpty(presetName))
            {
                var preset = _rotation.Database.Presets.AllPresets
                    .FirstOrDefault(p => p.Name == presetName);
                if (preset != null)
                {
                    AI.AIManager.Instance.SetAIPreset(preset);
                    Service.Log($"[MultiboxSync] Preset sync: applied '{presetName}'");
                }
            }
        }
        if (macroAPulse)
            ExecuteGameMacro(state.MacroNumberA);
        if (macroBPulse)
            ExecuteGameMacro(state.MacroNumberB);
        if (leavePulse)
        {
            EventFramework.LeaveCurrentContent(false);
            Service.Log("[MultiboxSync] Leave duty pulse received");
        }

        _mboxPrevCommandFlags = state.CommandFlags;

        var myContentId = (long)_ws.Party.Members[PartyState.PlayerSlot].ContentId;
        var mySlot = MultiboxSyncState.FindSlot(myContentId, ref state);

        if (mySlot >= 0)
        {
            ref var slotData = ref state.Slot(mySlot);

            var assignment = (PartyRolesConfig.Assignment)slotData.Assignment;
            var myContentIdUlong = (ulong)myContentId;
            if (assignment != PartyRolesConfig.Assignment.Unassigned
                && _mboxPrc[myContentIdUlong] != assignment)
            {
                _mboxPrc.Assignments[myContentIdUlong] = assignment;
                _mboxPrc.Modified.Fire();
            }

            if (_mboxConfig.SyncPositionOverrides && (slotData.Flags & 1) != 0)
            {
                var targetPos = new WPos(slotData.TargetX, slotData.TargetZ);
                _hints.GoalZones.Add(AIHints.GoalProximity(targetPos, 1f, 100f));
            }
        }

        if (mySlot >= 0 && _mboxAltReportWriter != null)
        {
            var report = new MultiboxAltReportSlot
            {
                ContentId = myContentId,
                ClassJob = (byte)player.Class,
                Flags = 0,
                LastSyncSequence = state.FrameSequence
            };
            if (AI.AIManager.Instance?.Beh != null)
                report.Flags |= 1;
            if (_rotation.Preset != null && _rotation.Preset != RotationModuleManager.ForceDisable)
                report.Flags |= 2;
            report.SetPresetName(_rotation.Preset?.Name);
            report.SetPlanName(_rotation.Planner?.Plan?.Name);
            report.SetBuildNumber(BuildNumber);
            _mboxAltReportWriter.Write(ref report, mySlot);
        }
    }

    private unsafe void UpdateMultiboxSync()
    {
        if (_mboxConfig.Role != MultiboxRole.Main || _mboxWriter == null)
            return;

        var player = _ws.Party.Player();
        if (player == null)
            return;

        _mboxState.MainContentId = (long)_ws.Party.Members[PartyState.PlayerSlot].ContentId;
        _mboxState.TerritoryId = (ushort)FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance()->CurrentTerritoryTypeId;

        var activeModule = _bossmod.ActiveModule;
        if (activeModule?.StateMachine.ActiveState != null)
        {
            _mboxState.PhaseIndex = (byte)Math.Max(0, activeModule.StateMachine.ActivePhaseIndex);
            _mboxState.StateId = activeModule.StateMachine.ActiveState.ID;
            _mboxState.StateTime = activeModule.StateMachine.TimeSinceTransition;
        }
        else
        {
            _mboxState.PhaseIndex = 0;
            _mboxState.StateId = 0;
            _mboxState.StateTime = 0;
        }

        for (var i = 0; i < MultiboxSyncState.MaxSlots; ++i)
        {
            ref var slot = ref _mboxState.Slot(i);
            ref var member = ref _ws.Party.Members[i];
            slot.ContentId = (long)member.ContentId;
            slot.Assignment = (byte)_mboxPrc[member.ContentId];
            slot.Flags = 0;
        }

        if (_mboxConfig.SyncPositionOverrides)
            _mboxPosEditor.WritePositionsToState(ref _mboxState, _ws.Party, _mboxPrc);

        _mboxState.ResolutionActive = 0;
        _mboxState.ResolutionFlags = 0;

        _mboxState.SetPresetName(_rotation.Preset?.Name);
        _mboxState.MacroNumberA = (byte)Math.Clamp(_mboxConfig.SyncMacroNumber, 0, 99);
        _mboxState.MacroNumberB = (byte)Math.Clamp(_mboxConfig.RsrOffMacroNumber, 0, 99);

        _mboxState.FrameSequence++;
        _mboxWriter.Write(ref _mboxState);

        if (_mboxAltReportReader?.TryRead(out var altReport) == true)
            _altReport = altReport;

        _mboxState.DiveEndFlags = 0;
        _mboxState.CommandFlags = 0;
    }

    public static void GarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
