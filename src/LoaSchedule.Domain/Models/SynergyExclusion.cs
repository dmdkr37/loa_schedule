namespace LoaSchedule.Domain.Models;

public sealed class SynergyExclusion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReceiverClassName { get; set; } = string.Empty;
    public string? ReceiverBuildName { get; set; }
    public SynergyKind Kind { get; set; }
    public string? Reason { get; set; }
}
