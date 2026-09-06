using LoaSchedule.Domain.Models;

namespace LoaSchedule.Domain.Services;

public sealed class ScheduleReferenceService
{
    public bool HasConfirmedCharacterReference(
        IEnumerable<RaidTeamSchedule> schedules,
        IReadOnlyCollection<Guid> characterIds) =>
        characterIds.Count > 0
        && schedules.Any(schedule =>
            schedule.IsConfirmed
            && schedule.Members.Any(member => characterIds.Contains(member.CharacterId)));

    public bool HasConfirmedRaidReference(
        IEnumerable<RaidTeamSchedule> schedules,
        Guid raidContentId) =>
        schedules.Any(schedule => schedule.IsConfirmed && schedule.RaidContentId == raidContentId);

    public int RemoveDraftsContainingCharacters(
        List<RaidTeamSchedule> schedules,
        IReadOnlyCollection<Guid> characterIds) =>
        characterIds.Count == 0
            ? 0
            : schedules.RemoveAll(schedule =>
                !schedule.IsConfirmed
                && schedule.Members.Any(member => characterIds.Contains(member.CharacterId)));

    public int RemoveDraftsForRaid(
        List<RaidTeamSchedule> schedules,
        Guid raidContentId) =>
        schedules.RemoveAll(schedule =>
            !schedule.IsConfirmed && schedule.RaidContentId == raidContentId);
}
