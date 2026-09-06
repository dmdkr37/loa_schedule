namespace LoaSchedule.Domain.Models;

public sealed class PartyCompositionSettings
{
    public decimal SynergyWeight { get; set; } = 1m;
    public decimal CombatPowerBalanceWeight { get; set; } = 1m;
    public decimal AvailabilityWeight { get; set; } = 1m;
    public decimal CharacterSwitchPenalty { get; set; } = 25m;
    public int MinimumCommonMinutes { get; set; } = 30;
}
