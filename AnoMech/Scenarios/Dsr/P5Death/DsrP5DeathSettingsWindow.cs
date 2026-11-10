using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Dsr.P5Death;

public sealed class DsrP5DeathSettingsWindow
{
    private static readonly string[] SlotLabels =
        ["My role's spot (selected strategy)", "1 (left end)", "2", "3", "4 (left centre)", "5 (right centre)", "6", "7", "8 (right end)"];

    public DsrP5DeathStateOverrides Overrides { get; } = new() { PlayerDoom = Plugin.Config?.DsrDeathDoom };

    public void Draw()
    {
        var doom = Overrides.PlayerDoom is { } forced ? (forced ? 1 : 2) : 0;
        string[] labels = ["Random", "Doom", "No Doom"];
        if (ImGui.Combo("Assignment", ref doom, labels, labels.Length))
        {
            Overrides.PlayerDoom = doom == 0 ? null : doom == 1;
            Plugin.Config.DsrDeathDoom = Overrides.PlayerDoom;
            Plugin.Config.Save();
        }
        if (ImGui.Button("Auto")) Overrides.PlayerCongaSlot = null;

        if (SettingsGrid.Begin("##dsrp5death"))
        {
            SettingsGrid.Row("Conga spot:");
            var idx = Overrides.PlayerCongaSlot is { } slot ? slot + 1 : 0;
            SettingsGrid.ItemWidth(260);
            if (ImGui.Combo("##dsrconga", ref idx, SlotLabels, SlotLabels.Length))
                Overrides.PlayerCongaSlot = idx == 0 ? null : idx - 1;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Your place in the line facing the warrior; the bots shift to fill the rest.");
            SettingsGrid.End();
        }
    }
}
