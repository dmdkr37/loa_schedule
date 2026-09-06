namespace LoaSchedule.Domain.Models;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 5;
    public string LostArkApiToken { get; set; } = string.Empty;
    public List<Person> People { get; set; } = [];
    public List<GameCharacter> Characters { get; set; } = [];
    public List<AvailabilityWindow> AvailabilityWindows { get; set; } = [];
    public List<WeeklyRaidSelection> WeeklyRaidSelections { get; set; } = [];
    public List<RaidContent> Raids { get; set; } = [];
    public List<SynergyDefinition> Synergies { get; set; } = [];
    public List<SynergyExclusion> SynergyExclusions { get; set; } = [];
    public List<RaidTeamSchedule> RaidTeamSchedules { get; set; } = [];
    public PartyCompositionSettings PartyCompositionSettings { get; set; } = new();
}
