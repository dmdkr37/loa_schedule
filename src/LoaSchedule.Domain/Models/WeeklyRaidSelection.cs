namespace LoaSchedule.Domain.Models;

public sealed class WeeklyRaidSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime WeekStartLocal { get; set; }
    public Guid CharacterId { get; set; }
    public Guid RaidContentId { get; set; }
    public int CompletedGateCount { get; set; }
    public bool IsConfirmed { get; set; } = true;
}
