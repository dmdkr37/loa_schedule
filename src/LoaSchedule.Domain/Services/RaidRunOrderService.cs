using LoaSchedule.Domain.Models;

namespace LoaSchedule.Domain.Services;

public sealed class RaidRunOrderService
{
    public RaidRunOrderResult Optimize(
        IEnumerable<RaidTeamSchedule> schedules,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById)
    {
        var source = schedules.ToArray();
        if (source.Length == 0)
        {
            return new RaidRunOrderResult([], 0, 0, 0);
        }

        var sourceOrder = source
            .Select((schedule, index) => (schedule.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        IReadOnlyList<RaidTeamSchedule>? bestOrder = null;
        var bestScore = new OrderScore(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);

        // 시작 공격대에 따라 탐욕 결과가 달라지므로 모든 시작점을 비교합니다.
        foreach (var first in source)
        {
            var remaining = source.Where(schedule => schedule.Id != first.Id).ToList();
            var ordered = new List<RaidTeamSchedule> { first };
            var lastCharacterByOwner = new Dictionary<Guid, Guid>();
            var previousOwnerIds = GetCharactersByOwner(first, charactersById).Keys.ToHashSet();
            var seenOwnerIds = previousOwnerIds.ToHashSet();
            ApplySchedule(first, lastCharacterByOwner, charactersById);

            while (remaining.Count > 0)
            {
                var next = remaining
                    .Select(schedule => new
                    {
                        Schedule = schedule,
                        Score = ScoreNextSchedule(
                            schedule,
                            lastCharacterByOwner,
                            previousOwnerIds,
                            seenOwnerIds,
                            charactersById)
                    })
                    .OrderByDescending(candidate => candidate.Score.ParticipantCount)
                    .ThenBy(candidate => candidate.Score.ReentryCount)
                    .ThenByDescending(candidate => candidate.Score.ContinuingOwnerCount)
                    .ThenBy(candidate => candidate.Score.SwitchCount)
                    .ThenByDescending(candidate => candidate.Score.ReuseCount)
                    .ThenBy(candidate => sourceOrder[candidate.Schedule.Id])
                    .First()
                    .Schedule;

                ordered.Add(next);
                remaining.Remove(next);
                ApplySchedule(next, lastCharacterByOwner, charactersById);
                previousOwnerIds = GetCharactersByOwner(next, charactersById).Keys.ToHashSet();
                seenOwnerIds.UnionWith(previousOwnerIds);
            }

            var score = EvaluateOrder(ordered, charactersById);
            if (IsBetter(score, bestScore))
            {
                bestOrder = ordered;
                bestScore = score;
            }
        }

        var optimized = ImproveByRelocation(bestOrder ?? source, charactersById);
        var steps = BuildSteps(optimized, charactersById);
        var finalScore = EvaluateOrder(optimized, charactersById);
        return new RaidRunOrderResult(
            steps,
            finalScore.SwitchCount,
            finalScore.ReentryCount,
            finalScore.GapRaidCount);
    }

    private static IReadOnlyList<RaidTeamSchedule> ImproveByRelocation(
        IReadOnlyList<RaidTeamSchedule> initial,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById)
    {
        var best = initial.ToList();
        var bestScore = EvaluateOrder(best, charactersById);
        var improved = true;

        while (improved)
        {
            improved = false;
            for (var from = 0; from < best.Count && !improved; from++)
            {
                for (var to = 0; to < best.Count && !improved; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    var candidate = best.ToList();
                    var moved = candidate[from];
                    candidate.RemoveAt(from);
                    candidate.Insert(to, moved);
                    var score = EvaluateOrder(candidate, charactersById);
                    if (!IsBetter(score, bestScore))
                    {
                        continue;
                    }

                    best = candidate;
                    bestScore = score;
                    improved = true;
                }
            }
        }

        return best;
    }

    private static (
        int ParticipantCount,
        int ReentryCount,
        int SwitchCount,
        int ReuseCount,
        int ContinuingOwnerCount) ScoreNextSchedule(
        RaidTeamSchedule schedule,
        IReadOnlyDictionary<Guid, Guid> lastCharacterByOwner,
        IReadOnlySet<Guid> previousOwnerIds,
        IReadOnlySet<Guid> seenOwnerIds,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById)
    {
        var charactersByOwner = GetCharactersByOwner(schedule, charactersById);
        var switchCount = 0;
        var reuseCount = 0;
        foreach (var character in charactersByOwner.Values)
        {
            if (!lastCharacterByOwner.TryGetValue(character.OwnerId, out var previousCharacterId))
            {
                continue;
            }

            if (previousCharacterId == character.Id)
            {
                reuseCount++;
            }
            else
            {
                switchCount++;
            }
        }

        var continuingOwnerCount = charactersByOwner.Keys
            .Count(previousOwnerIds.Contains);
        var reentryCount = charactersByOwner.Keys
            .Count(ownerId => seenOwnerIds.Contains(ownerId) && !previousOwnerIds.Contains(ownerId));
        return (schedule.Members.Count, reentryCount, switchCount, reuseCount, continuingOwnerCount);
    }

    private static void ApplySchedule(
        RaidTeamSchedule schedule,
        IDictionary<Guid, Guid> lastCharacterByOwner,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById)
    {
        foreach (var character in GetCharactersByOwner(schedule, charactersById).Values)
        {
            lastCharacterByOwner[character.OwnerId] = character.Id;
        }
    }

    private static OrderScore EvaluateOrder(
        IReadOnlyList<RaidTeamSchedule> schedules,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById)
    {
        var participantOrderPenalty = 0;
        var switches = 0;
        var reentries = 0;
        var gapRaids = 0;
        var lastCharacterByOwner = new Dictionary<Guid, Guid>();
        var lastAttendanceIndexByOwner = new Dictionary<Guid, int>();
        for (var left = 0; left < schedules.Count; left++)
        {
            for (var right = left + 1; right < schedules.Count; right++)
            {
                if (schedules[left].Members.Count < schedules[right].Members.Count)
                {
                    participantOrderPenalty++;
                }
            }
        }

        for (var index = 0; index < schedules.Count; index++)
        {
            foreach (var character in GetCharactersByOwner(schedules[index], charactersById).Values)
            {
                if (lastCharacterByOwner.TryGetValue(character.OwnerId, out var previousCharacterId)
                    && previousCharacterId != character.Id)
                {
                    switches++;
                }

                lastCharacterByOwner[character.OwnerId] = character.Id;
                if (lastAttendanceIndexByOwner.TryGetValue(character.OwnerId, out var previousIndex))
                {
                    var gap = index - previousIndex - 1;
                    if (gap > 0)
                    {
                        reentries++;
                        gapRaids += gap;
                    }
                }

                lastAttendanceIndexByOwner[character.OwnerId] = index;
            }
        }

        return new OrderScore(participantOrderPenalty, switches, reentries, gapRaids);
    }

    private static bool IsBetter(OrderScore candidate, OrderScore current) =>
        candidate.ParticipantOrderPenalty < current.ParticipantOrderPenalty
        || candidate.ParticipantOrderPenalty == current.ParticipantOrderPenalty
        && (candidate.ReentryCount < current.ReentryCount
            || candidate.ReentryCount == current.ReentryCount
            && (candidate.GapRaidCount < current.GapRaidCount
                || candidate.GapRaidCount == current.GapRaidCount
                && candidate.SwitchCount < current.SwitchCount));

    private static IReadOnlyList<RaidRunOrderStep> BuildSteps(
        IEnumerable<RaidTeamSchedule> schedules,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById)
    {
        var steps = new List<RaidRunOrderStep>();
        var lastCharacterByOwner = new Dictionary<Guid, Guid>();
        var previousOwnerIds = new HashSet<Guid>();
        var seenOwnerIds = new HashSet<Guid>();
        var order = 1;

        foreach (var schedule in schedules)
        {
            var charactersByOwner = GetCharactersByOwner(schedule, charactersById);
            var currentOwnerIds = charactersByOwner.Keys.ToHashSet();
            var reenteredOwnerIds = currentOwnerIds
                .Where(ownerId => seenOwnerIds.Contains(ownerId) && !previousOwnerIds.Contains(ownerId))
                .ToArray();
            var joiningCount = currentOwnerIds.Count(ownerId => !previousOwnerIds.Contains(ownerId));
            var continuingCount = currentOwnerIds.Count(ownerId => previousOwnerIds.Contains(ownerId));
            var leavingCount = previousOwnerIds.Count(ownerId => !currentOwnerIds.Contains(ownerId));
            var changes = new List<RaidCharacterChange>();
            var reuseCount = 0;
            foreach (var character in charactersByOwner.Values)
            {
                if (lastCharacterByOwner.TryGetValue(character.OwnerId, out var previousCharacterId))
                {
                    if (previousCharacterId == character.Id)
                    {
                        reuseCount++;
                    }
                    else
                    {
                        changes.Add(new RaidCharacterChange(
                            character.OwnerId,
                            previousCharacterId,
                            character.Id));
                    }
                }

                lastCharacterByOwner[character.OwnerId] = character.Id;
            }

            steps.Add(new RaidRunOrderStep(
                order++,
                schedule,
                changes.Count,
                reuseCount,
                joiningCount,
                continuingCount,
                leavingCount,
                reenteredOwnerIds,
                changes));
            seenOwnerIds.UnionWith(currentOwnerIds);
            previousOwnerIds = currentOwnerIds;
        }

        return steps;
    }

    private static IReadOnlyDictionary<Guid, GameCharacter> GetCharactersByOwner(
        RaidTeamSchedule schedule,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById) =>
        schedule.Members
            .Select(member => charactersById.GetValueOrDefault(member.CharacterId))
            .OfType<GameCharacter>()
            .GroupBy(character => character.OwnerId)
            .ToDictionary(group => group.Key, group => group.First());

    private readonly record struct OrderScore(
        int ParticipantOrderPenalty,
        int SwitchCount,
        int ReentryCount,
        int GapRaidCount);
}

public sealed record RaidRunOrderResult(
    IReadOnlyList<RaidRunOrderStep> Steps,
    int TotalSwitchCount,
    int TotalReentryCount,
    int TotalGapRaidCount);

public sealed record RaidRunOrderStep(
    int Order,
    RaidTeamSchedule Schedule,
    int SwitchCount,
    int ReuseCount,
    int JoiningCount,
    int ContinuingCount,
    int LeavingCount,
    IReadOnlyList<Guid> ReenteredOwnerIds,
    IReadOnlyList<RaidCharacterChange> Changes);

public sealed record RaidCharacterChange(
    Guid OwnerId,
    Guid PreviousCharacterId,
    Guid NextCharacterId);
