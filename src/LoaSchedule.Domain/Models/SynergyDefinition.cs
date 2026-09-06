namespace LoaSchedule.Domain.Models;

public sealed class SynergyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClassName { get; set; } = string.Empty;
    public string? RequiredBuildName { get; set; }
    public SynergyKind Kind { get; set; }
    public decimal Value { get; set; }
    public string StackingGroup { get; set; } = string.Empty;
    public bool IsConditional { get; set; }
    public string? ConditionDescription { get; set; }
    public bool IsActive { get; set; } = true;
}
