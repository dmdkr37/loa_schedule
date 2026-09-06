using LoaSchedule.Domain.Models;

namespace LoaSchedule.App.ViewModels;

public sealed record DayOption(DayOfWeek Value, string DisplayName);

public sealed record TimeOption(int Minutes, string DisplayName);

public sealed record SynergyKindOption(SynergyKind Value, string DisplayName);

public sealed record CharacterOwnerFilterOption(Guid? Value, string DisplayName);

public sealed record AvailabilityListItem(
    Guid Id,
    string PersonName,
    string DayName,
    string StartTime,
    string EndTime);

public sealed class RaidRecommendationChoice : ObservableObject
{
    private bool _isSelected;

    public RaidRecommendationChoice(Guid? raidId, string displayName, bool isSelected)
    {
        RaidId = raidId;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    public Guid? RaidId { get; }
    public string DisplayName { get; }
    public bool IsAvailable => RaidId.HasValue;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record RaidRecommendationItem(
    Guid CharacterId,
    string OwnerName,
    string CharacterName,
    decimal ItemLevel,
    RaidRecommendationChoice FirstRaid,
    RaidRecommendationChoice SecondRaid,
    RaidRecommendationChoice ThirdRaid)
{
    public IReadOnlyList<Guid> SelectedRaidIds =>
        new[] { FirstRaid, SecondRaid, ThirdRaid }
            .Where(choice => choice.IsAvailable && choice.IsSelected)
            .Select(choice => choice.RaidId!.Value)
            .ToArray();
}

public sealed record WeeklyRaidPlanItem(
    Guid SelectionId,
    Guid CharacterId,
    string OwnerName,
    string CharacterName,
    string RaidName,
    string Difficulty,
    string ProgressText,
    string StatusText,
    string EditState);

public sealed record RaidChoiceOption(Guid Value, string DisplayName);

public sealed record SynergyListItem(
    Guid Id,
    string ClassName,
    string BuildName,
    string KindName,
    string ValueText,
    string StackingGroup,
    string ConditionText);

public sealed record SynergyExclusionListItem(
    Guid Id,
    string ReceiverClassName,
    string ReceiverBuildName,
    string KindName,
    string Reason);

public sealed record AutoPartyPlanItem(
    string RaidName,
    string Difficulty,
    string TeamName,
    string PartyName,
    bool IsTeamStart,
    string Schedule,
    string Members,
    string AverageCombatPower,
    string SynergyScore,
    string PowerSpread,
    string VacancyText);

public sealed record AutoPartyTeamCardItem(
    Guid ScheduleId,
    string RaidName,
    string Difficulty,
    string TeamName,
    string StatusText,
    string AccentColor,
    string FilledText,
    string VacancyText,
    string AverageCombatPower,
    string SynergyScore,
    string PowerSpread,
    IReadOnlyList<AutoPartyCardPartyItem> Parties);

public sealed record AutoPartyRunOrderItem(
    int Order,
    AutoPartyTeamCardItem Team,
    string TransitionSummary,
    string AttendanceSummary,
    string ChangeDetails);

public sealed record AutoPartyCardPartyItem(
    string PartyName,
    string MemberCountText,
    string AverageCombatPower,
    IReadOnlyList<AutoPartyCardSlotItem> Slots);

public sealed record AutoPartyCardSlotItem(
    bool IsVacancy,
    string OwnerName,
    string CharacterName,
    string ClassName,
    string RoleName,
    string ItemLevel,
    string CombatPower,
    string AccentColor);

public sealed record AutoPartyIssueItem(
    string RaidName,
    string Difficulty,
    string Characters,
    string Reason);

public sealed record AutoPartyMemberItem(
    Guid MemberId,
    Guid ScheduleId,
    Guid CharacterId,
    string RaidName,
    string Difficulty,
    string TeamName,
    string PartyName,
    string Schedule,
    string OwnerName,
    string CharacterName,
    string RoleName,
    string CombatPower,
    string SelectionState,
    string ReplacementState,
    string ConfirmationState);

public sealed record UnassignedCharacterItem(
    Guid CharacterId,
    Guid RaidContentId,
    string RaidName,
    string Difficulty,
    string OwnerName,
    string CharacterName,
    string RoleName,
    string CombatPower,
    string SelectionState,
    string Reason);
