namespace LoaSchedule.Domain.Models;

public sealed class RaidContent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GroupKey { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public decimal MinimumItemLevel { get; set; }
    public int PlayerCount { get; set; }
    public int PartySize { get; set; } = 4;
    public int GateCount { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
