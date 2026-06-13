using BossMod.Autorotation;
using Dalamud.Bindings.ImGui;

namespace BossMod;

// ImGui window for the main client to assign per-role positions for mechanics
sealed class MultiboxPositionEditor(BossModuleManager bossmod, WorldState ws, RotationModuleManager rotation, Func<MultiboxAltReport> getAltReport, Func<long> getFrameSequence, Action<byte> setCommandFlags, Action<byte> setDiveEndFlags) : UIWindow("Multibox Position Editor###mbox_pos", false, new(380, 580))
{
    private enum Preset { None, Cardinals, Intercardinals, ClockSpots, LightParties, StackCenter }

    private static readonly WPos DefaultCenter = new(100, 100);
    private static readonly string[] RoleNames = ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];
    private static readonly uint[] RoleColors =
    [
        0xFF0000FF, // MT - red
        0xFF4444FF, // OT - light red
        0xFF00FF00, // H1 - green
        0xFF44FF44, // H2 - light green
        0xFFFF8800, // M1 - orange
        0xFFFFBB44, // M2 - light orange
        0xFFFFFF00, // R1 - yellow
        0xFFFFFF88, // R2 - light yellow
    ];

    private readonly float _drawScale = 6f;

    // Per-role position overrides (in world coordinates)
    private readonly WPos?[] _positions = new WPos?[8];

    // State
    private float _radius = 10f;
    private float _hitboxOffset = 3f;
    private bool _useHitboxRadius;
    private int _dragging = -1;
    private Preset _activePreset = Preset.None;
    private float _prevEffectiveRadius;
    private WPos _prevCenter;

    public override void Draw()
    {
        DrawDashboard();
        ImGui.Separator();

        var activeModule = bossmod.ActiveModule;
        var arenaCenter = activeModule?.Center ?? DefaultCenter;
        var arenaRadius = activeModule?.Bounds.Radius ?? 20f;
        var bossHitbox = activeModule?.PrimaryActor.HitboxRadius ?? 0f;
        var hasBoss = activeModule?.PrimaryActor != null && bossHitbox > 0;

        // Info
        if (hasBoss)
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), $"Boss | Center: {arenaCenter} | Hitbox: {bossHitbox:F1} | Arena: {arenaRadius:F0}");
        else
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"No boss | Center: {arenaCenter}");

        // Radius controls
        ImGui.Separator();
        var radiusChanged = false;
        if (hasBoss)
        {
            ImGui.Checkbox("Use boss hitbox ring", ref _useHitboxRadius);
            if (_useHitboxRadius)
            {
                radiusChanged = ImGui.SliderFloat("Offset from hitbox", ref _hitboxOffset, -5f, 20f, "%.1f");
                ImGui.Text($"Effective radius: {GetEffectiveRadius(bossHitbox):F1}");
            }
            else
            {
                radiusChanged = ImGui.SliderFloat("Radius", ref _radius, 1f, 30f, "%.1f");
            }
        }
        else
        {
            _useHitboxRadius = false;
            radiusChanged = ImGui.SliderFloat("Radius", ref _radius, 1f, 30f, "%.1f");
        }

        var effectiveR = GetEffectiveRadius(bossHitbox);

        // Re-apply active preset when radius or center changes
        if (_activePreset != Preset.None && (radiusChanged || effectiveR != _prevEffectiveRadius || arenaCenter != _prevCenter))
            ApplyPreset(_activePreset, arenaCenter, effectiveR);
        _prevEffectiveRadius = effectiveR;
        _prevCenter = arenaCenter;

        // Minimap
        ImGui.Separator();
        var drawRadius = (arenaRadius + 2f) * _drawScale;
        var mapSize = drawRadius * 2;
        var drawCenter = ImGui.GetCursorScreenPos() + new Vector2(mapSize * 0.5f, mapSize * 0.5f);
        var dl = ImGui.GetWindowDrawList();

        // Arena boundary
        dl.AddCircle(drawCenter, arenaRadius * _drawScale, 0x80FFFFFF, 64);

        // Boss hitbox ring
        if (hasBoss)
            dl.AddCircle(drawCenter, bossHitbox * _drawScale, 0x40FF8888, 48);

        // Placement radius ring
        dl.AddCircle(drawCenter, effectiveR * _drawScale, 0x6000FFFF, 48);

        // Center + cardinal labels
        dl.AddCircleFilled(drawCenter, 3, 0xFFAAAAAA);
        var labelOffset = (arenaRadius + 1f) * _drawScale;
        dl.AddText(drawCenter + new Vector2(-3, -labelOffset - 14), 0x80FFFFFF, "N");
        dl.AddText(drawCenter + new Vector2(-3, labelOffset + 2), 0x80FFFFFF, "S");

        // Role markers
        for (var i = 0; i < 8; ++i)
        {
            if (_positions[i] is not WPos pos)
                continue;
            var offset = pos - arenaCenter;
            var screenPos = drawCenter + new Vector2(offset.X, offset.Z) * _drawScale;
            dl.AddCircleFilled(screenPos, 10, RoleColors[i]);
            var textSize = ImGui.CalcTextSize(RoleNames[i]);
            dl.AddText(screenPos - textSize * 0.5f, 0xFFFFFFFF, RoleNames[i]);
        }

        ImGui.Dummy(new Vector2(mapSize, mapSize));

        // Dragging
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var mousePos = ImGui.GetMousePos();
            var closest = -1;
            var closestDist = 16f;
            for (var i = 0; i < 8; ++i)
            {
                if (_positions[i] is not WPos p)
                    continue;
                var screenPos = drawCenter + new Vector2((p - arenaCenter).X, (p - arenaCenter).Z) * _drawScale;
                var dist = (mousePos - screenPos).Length();
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = i;
                }
            }
            _dragging = closest;
        }

        if (_dragging >= 0 && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var relPos = (ImGui.GetMousePos() - drawCenter) / _drawScale;
            _positions[_dragging] = arenaCenter + new WDir(relPos.X, relPos.Y);
            _activePreset = Preset.None; // manual drag breaks preset tracking
        }
        else
        {
            _dragging = -1;
        }

        // Preset buttons
        ImGui.Separator();
        PresetButton("Cardinals", Preset.Cardinals, arenaCenter, effectiveR);
        ImGui.SameLine();
        PresetButton("Intercardinals", Preset.Intercardinals, arenaCenter, effectiveR);
        ImGui.SameLine();
        PresetButton("Clock Spots", Preset.ClockSpots, arenaCenter, effectiveR);

        PresetButton("Light Parties", Preset.LightParties, arenaCenter, effectiveR);
        ImGui.SameLine();
        PresetButton("Stack Center", Preset.StackCenter, arenaCenter, effectiveR);
        ImGui.SameLine();
        if (ImGui.Button("Clear All"))
        {
            ClearAllPositions();
            _activePreset = Preset.None;
        }

        if (_activePreset != Preset.None)
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), $"Active: {_activePreset}");

        ImGui.Separator();

        // Per-role coordinate editor (relative to center)
        for (var i = 0; i < 8; ++i)
        {
            var hasPos = _positions[i] != null;
            var relX = hasPos ? _positions[i]!.Value.X - arenaCenter.X : 0f;
            var relZ = hasPos ? _positions[i]!.Value.Z - arenaCenter.Z : 0f;

            ImGui.PushID(i);
            var changed = false;
            if (ImGui.Checkbox($"{RoleNames[i]}##chk", ref hasPos))
                changed = true;
            ImGui.SameLine();
            if (hasPos)
            {
                ImGui.SetNextItemWidth(80);
                changed |= ImGui.DragFloat("X", ref relX, 0.1f);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                changed |= ImGui.DragFloat("Z", ref relZ, 0.1f);
            }
            if (changed)
            {
                _positions[i] = hasPos ? arenaCenter + new WDir(relX, relZ) : null;
                _activePreset = Preset.None;
            }
            ImGui.PopID();
        }
    }

    private void DrawDashboard()
    {
        if (!ImGui.CollapsingHeader("Connected Players"))
            return;

        var report = getAltReport();
        var prc = Service.Config.Get<PartyRolesConfig>();
        var mainContentId = (long)ws.Party.Members[PartyState.PlayerSlot].ContentId;
        var frameSeq = getFrameSequence();

        // Sync buttons
        var mboxCfg = Service.Config.Get<MultiboxConfig>();
        var mainAiOn = AI.AIManager.Instance?.Beh != null;
        if (ImGui.Button(mainAiOn ? "All Off" : "All On"))
        {
            if (mainAiOn)
            {
                // Turn off: local + alts
                AI.AIManager.Instance?.SwitchToIdle();
                setCommandFlags(2); // AI off pulse
            }
            else if (AI.AIManager.Instance != null)
            {
                // Turn on: local + alts
                var aiCfg = Service.Config.Get<AI.AIConfig>();
                AI.AIManager.Instance.SwitchToFollow(aiCfg.FollowSlot);
                setCommandFlags(1 | 4); // AI on + preset sync pulse
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Reload"))
            setCommandFlags(0x08);
        ImGui.SameLine();
        if (ImGui.Button("RSR Off"))
        {
            // Execute RSR off macro locally on main + send to alts
            Plugin.ExecuteGameMacro(mboxCfg.RsrOffMacroNumber);
            setCommandFlags(0x10);
        }
        ImGui.SameLine();
        if (ImGui.Button("DE On"))
            setDiveEndFlags(1);
        ImGui.SameLine();
        if (ImGui.Button("DE Off"))
            setDiveEndFlags(2);
        var tpOn = AI.AIManager.Instance?.Controller.UseTeleport == true;
        ImGui.SameLine();
        if (ImGui.Button(tpOn ? "TP Off" : "TP On"))
        {
            if (AI.AIManager.Instance != null)
                AI.AIManager.Instance.Controller.UseTeleport = !tpOn;
            setCommandFlags((byte)(tpOn ? 0x40 : 0x20));
        }
        ImGui.SameLine();
        if (ImGui.Button("Leave"))
            setCommandFlags(0x80);
        if (mainAiOn && rotation.Preset != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), $"({rotation.Preset.Name})");
        }

        if (ImGui.BeginTable("mbox_dashboard", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableSetupColumn("AI", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableSetupColumn("Preset", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Plan", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Update", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableHeadersRow();

            for (var i = 0; i < 8; ++i)
            {
                ref var member = ref ws.Party.Members[i];
                if (member.ContentId == 0 || (long)member.ContentId == mainContentId)
                    continue;

                var actor = ws.Party[i];
                var hasReport = report.HasSlot((long)member.ContentId);
                ref var altSlot = ref report.FindSlot((long)member.ContentId);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"{i}");
                ImGui.TableNextColumn();
                ImGui.Text(actor?.Name ?? "?");
                ImGui.TableNextColumn();
                ImGui.Text(hasReport ? ((Class)altSlot.ClassJob).ToString() : actor?.Class.ToString() ?? "?");
                ImGui.TableNextColumn();
                var roleIdx = (int)prc[member.ContentId];
                ImGui.Text(roleIdx < RoleNames.Length ? RoleNames[roleIdx] : "?");
                ImGui.TableNextColumn();
                if (hasReport)
                {
                    var aiOn = (altSlot.Flags & 1) != 0;
                    ImGui.TextColored(aiOn ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0.3f, 0.3f, 1), aiOn ? "On" : "Off");
                }
                else
                {
                    ImGui.Text("-");
                }
                ImGui.TableNextColumn();
                ImGui.Text(hasReport ? altSlot.GetPresetName() : "-");
                ImGui.TableNextColumn();
                ImGui.Text(hasReport ? altSlot.GetPlanName() : "-");
                ImGui.TableNextColumn();
                if (hasReport)
                {
                    var staleness = frameSeq - altSlot.LastSyncSequence;
                    if (staleness < 60)
                        ImGui.Text($"{staleness}f");
                    else
                        ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "stale");
                }
                else
                {
                    ImGui.Text("-");
                }
                ImGui.TableNextColumn();
                if (hasReport)
                {
                    var altBuild = altSlot.GetBuildNumber();
                    var matches = altBuild == Plugin.BuildNumber;
                    ImGui.TextColored(
                        matches ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0.3f, 0.3f, 1),
                        string.IsNullOrEmpty(altBuild) ? "?" : altBuild);
                }
                else
                {
                    ImGui.Text("-");
                }
            }
            ImGui.EndTable();
        }
    }

    private void PresetButton(string label, Preset preset, WPos center, float r)
    {
        if (ImGui.Button(label))
        {
            _activePreset = preset;
            ApplyPreset(preset, center, r);
        }
    }

    private void ApplyPreset(Preset preset, WPos c, float r)
    {
        // Layout rules: Team 1 (MT,H1,M1,R1) left/west, Team 2 (OT,H2,M2,R2) right/east
        // Ranged (R1,R2) prefer north, Melee (M1,M2) prefer south/back
        var d = r * 0.707f;
        switch (preset)
        {
            case Preset.Cardinals:
                // Tanks N/S, Healers W/E (team split), Ranged N, Melee S
                _positions[0] = c + new WDir(0, -r);  // MT north
                _positions[1] = c + new WDir(0, r);   // OT south
                _positions[2] = c + new WDir(-r, 0);  // H1 west (team 1)
                _positions[3] = c + new WDir(r, 0);   // H2 east (team 2)
                _positions[4] = c + new WDir(-r, 0);  // M1 west (melee, team 1)
                _positions[5] = c + new WDir(r, 0);   // M2 east (melee, team 2)
                _positions[6] = c + new WDir(0, -r);  // R1 north (ranged, team 1)
                _positions[7] = c + new WDir(0, -r);  // R2 north (ranged, team 2)
                break;
            case Preset.Intercardinals:
                // Team 1 west side, Team 2 east side; Ranged north, Melee south
                _positions[0] = c + new WDir(-d, -d); // MT NW (team 1)
                _positions[1] = c + new WDir(d, -d);  // OT NE (team 2)
                _positions[2] = c + new WDir(-d, d);  // H1 SW (team 1)
                _positions[3] = c + new WDir(d, d);   // H2 SE (team 2)
                _positions[4] = c + new WDir(-d, d);  // M1 SW (melee back, team 1)
                _positions[5] = c + new WDir(d, d);   // M2 SE (melee back, team 2)
                _positions[6] = c + new WDir(-d, -d); // R1 NW (ranged north, team 1)
                _positions[7] = c + new WDir(d, -d);  // R2 NE (ranged north, team 2)
                break;
            case Preset.ClockSpots:
                // 8 unique spots: T N/S, H W/E, R NW/NE (north), M SW/SE (south/back)
                _positions[0] = c + new WDir(0, -r);  // MT 12 (N)
                _positions[1] = c + new WDir(0, r);   // OT 6 (S)
                _positions[2] = c + new WDir(-r, 0);  // H1 9 (W, team 1)
                _positions[3] = c + new WDir(r, 0);   // H2 3 (E, team 2)
                _positions[4] = c + new WDir(-d, d);  // M1 8 (SW, melee back, team 1)
                _positions[5] = c + new WDir(d, d);   // M2 4 (SE, melee back, team 2)
                _positions[6] = c + new WDir(-d, -d); // R1 10 (NW, ranged north, team 1)
                _positions[7] = c + new WDir(d, -d);  // R2 2 (NE, ranged north, team 2)
                break;
            case Preset.LightParties:
                // LP1 (team 1) west, LP2 (team 2) east
                _positions[0] = c + new WDir(-r, 0);  // MT west
                _positions[1] = c + new WDir(r, 0);   // OT east
                _positions[2] = c + new WDir(-r, 0);  // H1 west
                _positions[3] = c + new WDir(r, 0);   // H2 east
                _positions[4] = c + new WDir(-r, 0);  // M1 west
                _positions[5] = c + new WDir(r, 0);   // M2 east
                _positions[6] = c + new WDir(-r, 0);  // R1 west
                _positions[7] = c + new WDir(r, 0);   // R2 east
                break;
            case Preset.StackCenter:
                for (var i = 0; i < 8; ++i)
                    _positions[i] = c;
                break;
        }
    }

    private float GetEffectiveRadius(float bossHitbox)
        => _useHitboxRadius && bossHitbox > 0 ? bossHitbox + _hitboxOffset : _radius;

    public void WritePositionsToState(ref MultiboxSyncState state, PartyState party, PartyRolesConfig prc)
    {
        for (var slot = 0; slot < MultiboxSyncState.MaxSlots; ++slot)
        {
            ref var slotData = ref state.Slot(slot);
            var contentId = party.Members[slot].ContentId;
            if (contentId == 0)
                continue;

            var assignment = prc[contentId];
            if (assignment == PartyRolesConfig.Assignment.Unassigned)
                continue;

            var roleIdx = (int)assignment;
            if (roleIdx < 8 && _positions[roleIdx] is WPos pos)
            {
                slotData.TargetX = pos.X;
                slotData.TargetZ = pos.Z;
                slotData.Flags |= 1;
            }
        }
    }

    public void ClearAllPositions()
    {
        for (var i = 0; i < 8; ++i)
            _positions[i] = null;
    }
}
