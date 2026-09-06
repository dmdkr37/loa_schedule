using LoaSchedule.Domain.Models;

namespace LoaSchedule.Domain.Services;

public sealed class SynergyEvaluationService
{
    public IReadOnlyList<SynergyDefinition> GetProvidedSynergies(
        GameCharacter provider,
        IEnumerable<SynergyDefinition> definitions) =>
        definitions
            .Where(definition => definition.IsActive)
            .Where(definition => Matches(definition.ClassName, provider.ClassName))
            .Where(definition => string.IsNullOrWhiteSpace(definition.RequiredBuildName)
                                 || Matches(definition.RequiredBuildName, provider.BuildName))
            .ToArray();

    public bool CanReceive(
        GameCharacter receiver,
        SynergyDefinition synergy,
        IEnumerable<SynergyExclusion> exclusions) =>
        !exclusions.Any(exclusion =>
            exclusion.Kind == synergy.Kind
            && Matches(exclusion.ReceiverClassName, receiver.ClassName)
            && (string.IsNullOrWhiteSpace(exclusion.ReceiverBuildName)
                || Matches(exclusion.ReceiverBuildName, receiver.BuildName)));

    public IReadOnlyList<AppliedSynergy> GetEffectiveSynergies(
        GameCharacter receiver,
        IEnumerable<GameCharacter> partyMembers,
        IEnumerable<SynergyDefinition> definitions,
        IEnumerable<SynergyExclusion> exclusions)
    {
        var definitionList = definitions.ToArray();
        var exclusionList = exclusions.ToArray();

        return partyMembers
            .Where(provider => provider.IsActive && provider.Id != receiver.Id)
            .SelectMany(provider => GetProvidedSynergies(provider, definitionList)
                .Where(synergy => CanReceive(receiver, synergy, exclusionList))
                .Select(synergy => new AppliedSynergy(provider.Id, synergy)))
            .GroupBy(
                applied => string.IsNullOrWhiteSpace(applied.Definition.StackingGroup)
                    ? $"definition:{applied.Definition.Id}"
                    : $"group:{applied.Definition.StackingGroup.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(applied => applied.Definition.Value)
                .ThenBy(applied => applied.ProviderCharacterId)
                .First())
            .OrderBy(applied => applied.Definition.Kind)
            .ThenByDescending(applied => applied.Definition.Value)
            .ToArray();
    }

    private static bool Matches(string? expected, string? actual) =>
        string.Equals(expected?.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record AppliedSynergy(
    Guid ProviderCharacterId,
    SynergyDefinition Definition);
