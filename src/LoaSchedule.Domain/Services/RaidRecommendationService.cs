using LoaSchedule.Domain.Models;

namespace LoaSchedule.Domain.Services;

public sealed class RaidRecommendationService
{
    public IReadOnlyList<RaidContent> RecommendTopRaids(
        GameCharacter character,
        IEnumerable<RaidContent> raids,
        int maximumCount = 3)
    {
        if (maximumCount <= 0)
        {
            return [];
        }

        return raids
            .Where(raid => raid.IsActive && raid.MinimumItemLevel <= character.ItemLevel)
            .GroupBy(raid => raid.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(raid => raid.Priority)
                .ThenByDescending(raid => raid.MinimumItemLevel)
                .First())
            .OrderByDescending(raid => raid.Priority)
            .ThenByDescending(raid => raid.MinimumItemLevel)
            .ThenBy(raid => raid.Name)
            .Take(maximumCount)
            .ToArray();
    }
}
