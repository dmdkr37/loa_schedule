namespace LoaSchedule.Domain.Models;

public sealed class RaidTeamSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime WeekStartLocal { get; set; }
    public Guid RaidContentId { get; set; }
    public int TeamNumber { get; set; }
    public DayOfWeek Day { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
    public bool IsConfirmed { get; set; }
    public decimal SynergyScore { get; set; }
    public decimal PartyAveragePowerSpread { get; set; }
    public List<RaidTeamMember> Members { get; set; } = [];
}

public sealed class RaidTeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CharacterId { get; set; }
    public int PartyNumber { get; set; }
}
