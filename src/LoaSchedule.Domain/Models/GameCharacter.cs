namespace LoaSchedule.Domain.Models;

public sealed class GameCharacter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string BuildName { get; set; } = string.Empty;
    public CharacterRole Role { get; set; } = CharacterRole.Dealer;
    public PartyPosition Position { get; set; } = PartyPosition.Unknown;
    public decimal ItemLevel { get; set; }
    public int CombatPower { get; set; }
    public bool IsActive { get; set; } = true;
}
