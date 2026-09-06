namespace LoaSchedule.App.ViewModels;

public sealed record CharacterListItem(
    Guid Id,
    string OwnerName,
    string CharacterName,
    string ClassName,
    string BuildName,
    string RoleName,
    decimal ItemLevel,
    int CombatPower,
    bool IsActive);
