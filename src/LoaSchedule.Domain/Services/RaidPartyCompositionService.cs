using LoaSchedule.Domain.Models;

namespace LoaSchedule.Domain.Services;

public sealed class RaidPartyCompositionService
{
    // 공통 가능 시간이 없는 조합도 편성할 수 있도록 기본 일정 값을 유지합니다.
    private const DayOfWeek DefaultDay = DayOfWeek.Wednesday;

    private readonly SynergyEvaluationService _synergyService = new();

    public PartyAssignmentMetrics EvaluatePartyAssignment(
        IEnumerable<IEnumerable<GameCharacter>> parties,
        IEnumerable<SynergyDefinition> synergies,
        IEnumerable<SynergyExclusion> exclusions)
    {
        var partyList = parties.Select(party => party.ToArray()).ToArray();
        var synergyList = synergies.ToArray();
        var exclusionList = exclusions.ToArray();
        var synergyScore = partyList.Sum(party => party.Sum(receiver => _synergyService
            .GetEffectiveSynergies(receiver, party, synergyList, exclusionList)
            .Sum(applied => applied.Definition.Value)));
        var averages = partyList
            .Where(party => party.Length > 0)
            .Select(party => party.Average(character => (decimal)character.CombatPower))
            .ToArray();

        return new PartyAssignmentMetrics(
            synergyScore,
            averages.Length == 0 ? 0 : averages.Max() - averages.Min());
    }

    public RaidPartyCompositionResult Compose(
        DateTime weekStartLocal,
        IEnumerable<Person> people,
        IEnumerable<GameCharacter> characters,
        IEnumerable<AvailabilityWindow> availabilityWindows,
        IEnumerable<WeeklyRaidSelection> weeklySelections,
        IEnumerable<RaidContent> raids,
        IEnumerable<SynergyDefinition> synergies,
        IEnumerable<SynergyExclusion> exclusions,
        PartyCompositionSettings? settings = null)
    {
        settings ??= new PartyCompositionSettings();
        var activePeople = people.Where(person => person.IsActive).ToDictionary(person => person.Id);
        var activeCharacters = characters
            .Where(character => character.IsActive && activePeople.ContainsKey(character.OwnerId))
            .ToDictionary(character => character.Id);
        var raidsById = raids.Where(raid => raid.IsActive).ToDictionary(raid => raid.Id);
        var availabilityByPerson = availabilityWindows
            .Where(window => window.EndMinute > window.StartMinute)
            .GroupBy(window => window.PersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AvailabilityWindow>)group.ToArray());
        var synergyList = synergies.ToArray();
        var exclusionList = exclusions.ToArray();

        var candidates = weeklySelections
            .Where(selection => selection.IsConfirmed && selection.WeekStartLocal == weekStartLocal)
            .Select(selection =>
            {
                activeCharacters.TryGetValue(selection.CharacterId, out var character);
                raidsById.TryGetValue(selection.RaidContentId, out var raid);
                return character is null || raid is null || selection.CompletedGateCount >= raid.GateCount
                    ? null
                    : new CompositionCandidate(selection, character, activePeople[character.OwnerId], raid);
            })
            .OfType<CompositionCandidate>()
            .GroupBy(candidate => (candidate.Character.Id, candidate.Raid.Id))
            .Select(group => group.First())
            .ToArray();

        var plans = new List<RaidTeamPlan>();
        var unassignedGroups = new List<UnassignedRaidCandidates>();
        var usedCharactersByOwner = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var raidGroup in candidates
                     .GroupBy(candidate => candidate.Raid.Id)
                     .OrderByDescending(group => group.First().Raid.Priority))
        {
            var raid = raidGroup.First().Raid;
            var remaining = raidGroup.ToList();
            var teamNumber = 1;

            // 공대 수에는 제한을 두지 않습니다. 한 공대 안에서 같은 참여자만
            // 겹치지 않게 하고, 유효한 조합이 남아 있는 동안 계속 편성합니다.
            while (true)
            {
                var selected = FindBestTeam(
                    raid,
                    remaining,
                    synergyList,
                    exclusionList,
                    usedCharactersByOwner,
                    availabilityByPerson,
                    settings);
                if (selected is null)
                {
                    break;
                }

                plans.Add(BuildTeamPlan(raid, teamNumber, selected, synergyList, exclusionList, settings));
                foreach (var candidate in selected.Candidates)
                {
                    if (!usedCharactersByOwner.TryGetValue(candidate.Person.Id, out var usedCharacters))
                    {
                        usedCharacters = [];
                        usedCharactersByOwner[candidate.Person.Id] = usedCharacters;
                    }

                    usedCharacters.Add(candidate.Character.Id);
                }

                // 편성된 캐릭터만 후보에서 빼고, 같은 사람의 다른 부캐는 남겨둡니다.
                // 한 사람이 부캐를 달리해서 같은 레이드를 여러 번 도는 것이 정상입니다.
                var selectedCharacterIds = selected.Candidates
                    .Select(candidate => candidate.Character.Id)
                    .ToHashSet();
                remaining.RemoveAll(candidate => selectedCharacterIds.Contains(candidate.Character.Id));
                teamNumber++;
            }

            var rebalanceLeftovers = BalanceTeamsAcrossPower(
                plans,
                raid,
                availabilityByPerson,
                settings);

            var unassignedCharacters = remaining
                .Select(candidate => candidate.Character)
                .Concat(rebalanceLeftovers)
                .ToArray();
            if (unassignedCharacters.Length > 0)
            {
                unassignedGroups.Add(new UnassignedRaidCandidates(
                    raid,
                    unassignedCharacters,
                    BuildUnassignedReason(remaining)));
            }
        }

        return new RaidPartyCompositionResult(plans, unassignedGroups);
    }

    private TeamCandidate? FindBestTeam(
        RaidContent raid,
        IReadOnlyCollection<CompositionCandidate> candidates,
        IReadOnlyCollection<SynergyDefinition> synergies,
        IReadOnlyCollection<SynergyExclusion> exclusions,
        IReadOnlyDictionary<Guid, HashSet<Guid>> usedCharactersByOwner,
        IReadOnlyDictionary<Guid, IReadOnlyList<AvailabilityWindow>> availabilityByPerson,
        PartyCompositionSettings settings)
    {
        var selectedCandidates = SelectRoleBalancedTeam(
            raid,
            candidates,
            usedCharactersByOwner,
            availabilityByPerson,
            settings);
        if (selectedCandidates is null)
        {
            return null;
        }

        var parties = AssignSubParties(raid, selectedCandidates, synergies, exclusions, settings);
        var assignedCandidates = parties
            .SelectMany(party => party)
            .DistinctBy(candidate => candidate.Character.Id)
            .ToArray();
        if (assignedCandidates.Length == 0)
        {
            return null;
        }

        var synergyScore = CalculateSynergyScore(parties, synergies, exclusions);
        var powerSpread = CalculatePowerSpread(parties);
        var switchCount = assignedCandidates.Count(candidate =>
            usedCharactersByOwner.TryGetValue(candidate.Person.Id, out var usedCharacters)
            && usedCharacters.Count > 0
            && !usedCharacters.Contains(candidate.Character.Id));
        var commonAvailability = FindBestCommonAvailability(
            assignedCandidates.Select(candidate => candidate.Person.Id),
            availabilityByPerson,
            settings.MinimumCommonMinutes);
        var availabilityScore = commonAvailability is null
            ? 0m
            : commonAvailability.DurationMinutes / 60m;
        var optimizationScore = synergyScore * Math.Max(0, settings.SynergyWeight)
                                - powerSpread / 1000m * Math.Max(0, settings.CombatPowerBalanceWeight)
                                + availabilityScore * Math.Max(0, settings.AvailabilityWeight)
                                - switchCount * Math.Max(0, settings.CharacterSwitchPenalty);

        return new TeamCandidate(
            assignedCandidates,
            commonAvailability?.Day ?? DefaultDay,
            commonAvailability?.StartMinute ?? 0,
            commonAvailability?.EndMinute ?? 0,
            synergyScore,
            optimizationScore);
    }

    // 인원이 부족하거나 서포터가 없어도 공격대를 구성합니다.
    // 모자란 인원은 공석으로 남겨 실시간으로 모집할 수 있습니다.
    private static IReadOnlyList<CompositionCandidate>? SelectRoleBalancedTeam(
        RaidContent raid,
        IReadOnlyCollection<CompositionCandidate> candidates,
        IReadOnlyDictionary<Guid, HashSet<Guid>> usedCharactersByOwner,
        IReadOnlyDictionary<Guid, IReadOnlyList<AvailabilityWindow>> availabilityByPerson,
        PartyCompositionSettings settings)
    {
        if (raid.PlayerCount <= 0 || raid.PartySize <= 0)
        {
            return null;
        }

        var partyCount = (int)Math.Ceiling((decimal)raid.PlayerCount / raid.PartySize);
        var requiredSupporters = partyCount;
        var dealerOwnerIds = candidates
            .Where(candidate => candidate.Character.Role == CharacterRole.Dealer)
            .Select(candidate => candidate.Person.Id)
            .ToHashSet();

        var supporterCandidates = candidates
            .Where(candidate => candidate.Character.Role == CharacterRole.Supporter)
            .GroupBy(candidate => candidate.Person.Id)
            .Select(group => PickPreferredAlt(group, usedCharactersByOwner, settings))
            .ToList();
        var classCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var supporters = new List<CompositionCandidate>();
        while (supporters.Count < requiredSupporters)
        {
            var supporter = supporterCandidates
                .Where(candidate => CanAddClassToRaidTeam(classCounts, candidate.Character, partyCount))
                .OrderBy(candidate => dealerOwnerIds.Contains(candidate.Person.Id))
                .ThenByDescending(candidate => CandidateTeamFitScore(
                    candidate,
                    supporters,
                    availabilityByPerson,
                    usedCharactersByOwner,
                    settings))
                .FirstOrDefault();
            if (supporter is null)
            {
                break;
            }

            supporters.Add(supporter);
            supporterCandidates.Remove(supporter);
            IncrementClassCount(classCounts, supporter.Character);
        }

        // 서포터로 채우지 못한 자리는 딜러가 대신 들어갈 수 있게 남은 정원을 계산합니다.
        var remainingSlots = raid.PlayerCount - supporters.Count;
        var usedOwnerIds = supporters.Select(candidate => candidate.Person.Id).ToHashSet();
        var dealerCandidates = candidates
            .Where(candidate => candidate.Character.Role == CharacterRole.Dealer)
            .Where(candidate => !usedOwnerIds.Contains(candidate.Person.Id))
            .GroupBy(candidate => candidate.Person.Id)
            .Select(group => PickPreferredAlt(group, usedCharactersByOwner, settings))
            .ToList();
        var dealers = new List<CompositionCandidate>();
        while (dealers.Count < remainingSlots)
        {
            var currentMembers = supporters.Concat(dealers).ToArray();
            var dealer = dealerCandidates
                .Where(candidate => CanAddClassToRaidTeam(classCounts, candidate.Character, partyCount))
                .OrderByDescending(candidate => CandidateTeamFitScore(
                    candidate,
                    currentMembers,
                    availabilityByPerson,
                    usedCharactersByOwner,
                    settings))
                .FirstOrDefault();
            if (dealer is null)
            {
                break;
            }

            dealers.Add(dealer);
            dealerCandidates.Remove(dealer);
            IncrementClassCount(classCounts, dealer.Character);
        }

        var selected = new List<CompositionCandidate>(supporters.Count + dealers.Count);
        selected.AddRange(supporters);
        selected.AddRange(dealers);
        return selected.Count == 0 ? null : selected;
    }

    private RaidTeamPlan BuildTeamPlan(
        RaidContent raid,
        int teamNumber,
        TeamCandidate selected,
        IReadOnlyCollection<SynergyDefinition> synergies,
        IReadOnlyCollection<SynergyExclusion> exclusions,
        PartyCompositionSettings settings)
    {
        var parties = AssignSubParties(raid, selected.Candidates, synergies, exclusions, settings);
        var partyPlans = parties
            .Select((members, index) => new SubPartyPlan(index + 1, members.Select(candidate => candidate.Character).ToArray()))
            .ToArray();
        var partyAverages = partyPlans
            .Select(party => party.Members.Count == 0
                ? 0m
                : party.Members.Average(member => (decimal)member.CombatPower))
            .ToArray();

        return new RaidTeamPlan(
            raid,
            teamNumber,
            selected.Day,
            selected.StartMinute,
            selected.EndMinute,
            partyPlans,
            selected.Candidates.Average(candidate => (decimal)candidate.Character.CombatPower),
            partyAverages.Length == 0 ? 0 : partyAverages.Max() - partyAverages.Min(),
            CalculateSynergyScore(parties, synergies, exclusions));
    }

    private IReadOnlyList<IReadOnlyList<CompositionCandidate>> AssignSubParties(
        RaidContent raid,
        IReadOnlyCollection<CompositionCandidate> selected,
        IReadOnlyCollection<SynergyDefinition> synergies,
        IReadOnlyCollection<SynergyExclusion> exclusions,
        PartyCompositionSettings settings)
    {
        var partyCount = (int)Math.Ceiling((decimal)raid.PlayerCount / raid.PartySize);
        var parties = Enumerable.Range(0, partyCount)
            .Select(_ => new List<CompositionCandidate>())
            .ToArray();
        var supporters = selected
            .Where(candidate => candidate.Character.Role == CharacterRole.Supporter)
            .OrderBy(candidate => candidate.Character.CombatPower)
            .ToArray();

        foreach (var supporter in supporters)
        {
            var targetParty = parties
                .Where(party => party.Count < raid.PartySize && !ContainsSameClass(party, supporter.Character))
                .OrderBy(party => party.Count)
                .FirstOrDefault();
            if (targetParty is not null)
            {
                targetParty.Add(supporter);
            }
        }

        foreach (var dealer in selected
                     .Where(candidate => candidate.Character.Role == CharacterRole.Dealer)
                     .OrderByDescending(candidate => candidate.Character.CombatPower))
        {
            var targetParty = parties
                .Where(party => party.Count < raid.PartySize && !ContainsSameClass(party, dealer.Character))
                .Select(party => new
                {
                    Party = party,
                    Score = CalculatePartySynergyScore([.. party, dealer], synergies, exclusions),
                    Power = party.Sum(member => member.Character.CombatPower) + dealer.Character.CombatPower
                })
                // 공석이 한 파티에 몰리지 않도록 인원이 적은 파티부터 채웁니다.
                .OrderBy(candidate => candidate.Party.Count)
                .ThenByDescending(candidate =>
                    candidate.Score * Math.Max(0, settings.SynergyWeight)
                    - candidate.Power / 1000m * Math.Max(0, settings.CombatPowerBalanceWeight))
                .FirstOrDefault();
            if (targetParty is null)
            {
                continue;
            }

            targetParty.Party.Add(dealer);
        }

        OptimizeDealerSwaps(parties, synergies, exclusions, settings);

        return parties;
    }

    private decimal CalculateSynergyScore(
        IEnumerable<IReadOnlyList<CompositionCandidate>> parties,
        IReadOnlyCollection<SynergyDefinition> synergies,
        IReadOnlyCollection<SynergyExclusion> exclusions) =>
        parties.Sum(party => CalculatePartySynergyScore(party, synergies, exclusions));

    private decimal CalculatePartySynergyScore(
        IReadOnlyCollection<CompositionCandidate> party,
        IReadOnlyCollection<SynergyDefinition> synergies,
        IReadOnlyCollection<SynergyExclusion> exclusions)
    {
        var characters = party.Select(candidate => candidate.Character).ToArray();
        return characters.Sum(receiver => _synergyService
            .GetEffectiveSynergies(receiver, characters, synergies, exclusions)
            .Sum(applied => applied.Definition.Value));
    }

    private void OptimizeDealerSwaps(
        IReadOnlyList<List<CompositionCandidate>> parties,
        IReadOnlyCollection<SynergyDefinition> synergies,
        IReadOnlyCollection<SynergyExclusion> exclusions,
        PartyCompositionSettings settings)
    {
        var improved = true;
        var pass = 0;
        while (improved && pass++ < 20)
        {
            improved = false;
            var currentScore = CalculatePartyAssignmentScore(parties, synergies, exclusions, settings);

            for (var firstPartyIndex = 0; firstPartyIndex < parties.Count; firstPartyIndex++)
            {
                for (var secondPartyIndex = firstPartyIndex + 1; secondPartyIndex < parties.Count; secondPartyIndex++)
                {
                    var firstParty = parties[firstPartyIndex];
                    var secondParty = parties[secondPartyIndex];
                    for (var firstMemberIndex = 0; firstMemberIndex < firstParty.Count; firstMemberIndex++)
                    {
                        if (firstParty[firstMemberIndex].Character.Role != CharacterRole.Dealer)
                        {
                            continue;
                        }

                        for (var secondMemberIndex = 0; secondMemberIndex < secondParty.Count; secondMemberIndex++)
                        {
                            if (secondParty[secondMemberIndex].Character.Role != CharacterRole.Dealer)
                            {
                                continue;
                            }

                            (firstParty[firstMemberIndex], secondParty[secondMemberIndex]) =
                                (secondParty[secondMemberIndex], firstParty[firstMemberIndex]);
                            if (HasDuplicateClasses(firstParty) || HasDuplicateClasses(secondParty))
                            {
                                (firstParty[firstMemberIndex], secondParty[secondMemberIndex]) =
                                    (secondParty[secondMemberIndex], firstParty[firstMemberIndex]);
                                continue;
                            }

                            var swappedScore = CalculatePartyAssignmentScore(parties, synergies, exclusions, settings);
                            if (swappedScore > currentScore)
                            {
                                currentScore = swappedScore;
                                improved = true;
                            }
                            else
                            {
                                (firstParty[firstMemberIndex], secondParty[secondMemberIndex]) =
                                    (secondParty[secondMemberIndex], firstParty[firstMemberIndex]);
                            }
                        }
                    }
                }
            }
        }
    }

    private decimal CalculatePartyAssignmentScore(
        IReadOnlyList<List<CompositionCandidate>> parties,
        IReadOnlyCollection<SynergyDefinition> synergies,
        IReadOnlyCollection<SynergyExclusion> exclusions,
        PartyCompositionSettings settings) =>
        CalculateSynergyScore(parties, synergies, exclusions) * Math.Max(0, settings.SynergyWeight)
        - CalculatePowerSpread(parties) / 1000m * Math.Max(0, settings.CombatPowerBalanceWeight);

    private static decimal CalculatePowerSpread(IEnumerable<IReadOnlyCollection<CompositionCandidate>> parties)
    {
        var averages = parties
            .Where(party => party.Count > 0)
            .Select(party => party.Average(candidate => (decimal)candidate.Character.CombatPower))
            .ToArray();
        return averages.Length == 0 ? 0 : averages.Max() - averages.Min();
    }

    private static bool WasPreviouslyUsed(
        CompositionCandidate candidate,
        IReadOnlyDictionary<Guid, HashSet<Guid>> usedCharactersByOwner) =>
        usedCharactersByOwner.TryGetValue(candidate.Person.Id, out var usedCharacters)
        && usedCharacters.Contains(candidate.Character.Id);

    // 한 사람이 이 레이드에 낼 수 있는 캐릭터 중 하나를 고릅니다.
    // 이미 다른 레이드에서 쓴 캐릭터를 우선해 사람마다 갈아타는 횟수를 줄이고,
    // 그 다음은 전투력이 높은 순입니다.
    private static CompositionCandidate PickPreferredAlt(
        IEnumerable<CompositionCandidate> alts,
        IReadOnlyDictionary<Guid, HashSet<Guid>> usedCharactersByOwner,
        PartyCompositionSettings settings) =>
        alts
            .OrderByDescending(candidate => WasPreviouslyUsed(candidate, usedCharactersByOwner))
            .ThenByDescending(candidate => candidate.Character.CombatPower)
            .ThenBy(candidate => candidate.Character.Name, StringComparer.Ordinal)
            .First();

    private static decimal BucketAverage(List<GameCharacter> bucket) =>
        bucket.Count == 0 ? 0 : bucket.Average(member => (decimal)member.CombatPower);

    // 공대 간 전력 쏠림을 줄입니다. 강한 사람과 약한 사람을 섞어
    // 한 공대에 고인물만 몰리지 않게 재배치합니다.
    private static IReadOnlyList<GameCharacter> BalanceTeamsAcrossPower(
        List<RaidTeamPlan> plans,
        RaidContent raid,
        IReadOnlyDictionary<Guid, IReadOnlyList<AvailabilityWindow>> availabilityByPerson,
        PartyCompositionSettings settings)
    {
        var raidTeams = plans
            .Where(plan => plan.Raid.Id == raid.Id)
            .OrderBy(plan => plan.TeamNumber)
            .ToArray();
        if (raidTeams.Length < 2)
        {
            return [];
        }

        var maxSameClassPerTeam = Math.Max(
            1,
            (int)Math.Ceiling((decimal)raid.PlayerCount / Math.Max(1, raid.PartySize)));

        // 전투력이 높은 순으로, 이미 인원이 많은 공대부터 채웁니다.
        // 3명+3명처럼 공석이 흩어지는 것보다 4명+2명처럼 완성 파티를 먼저 만듭니다.
        var everyone = raidTeams
            .SelectMany(team => team.Parties.SelectMany(party => party.Members))
            .OrderByDescending(member => member.CombatPower)
            .ToArray();
        var buckets = raidTeams.Select(_ => new List<GameCharacter>()).ToArray();
        var leftovers = new List<GameCharacter>();

        foreach (var member in everyone)
        {
            // 인원이 가장 적은 공대부터, 같은 사람이 겹치지 않는 곳을 고릅니다.
            var target = buckets
                .Where(bucket => bucket.Count < raid.PlayerCount
                                 && CanJoinRaidTeamBucket(bucket, member, maxSameClassPerTeam))
                .OrderByDescending(bucket => bucket.Count)
                .ThenByDescending(bucket => CalculateAvailabilityHours(
                    bucket.Select(existing => existing.OwnerId).Append(member.OwnerId),
                    availabilityByPerson,
                    settings.MinimumCommonMinutes))
                .ThenBy(bucket => bucket.Sum(existing => existing.CombatPower))
                .FirstOrDefault();

            // 같은 사람이 한 공대에 두 번 들어가는 것은 어떤 경우에도 허용하지 않습니다.
            // 넣을 자리가 없으면 이 캐릭터는 편성하지 않고 미편성으로 남깁니다.
            if (target is null)
            {
                leftovers.Add(member);
                continue;
            }

            target.Add(member);
        }

        // 인원이 적은 공대에서 옮길 수 있는 캐릭터를 한 명씩 큰 공대로 이동합니다.
        // 공대 전체를 한 번에 옮길 수 없어도 3명+3명을 4명+2명으로 압축할 수 있습니다.
        for (var pass = 0; pass < buckets.Length * Math.Max(1, raid.PlayerCount); pass++)
        {
            var moved = false;
            foreach (var donor in buckets
                         .Where(bucket => bucket.Count > 0)
                         .OrderBy(bucket => bucket.Count))
            {
                foreach (var member in donor
                             .OrderByDescending(candidate => candidate.CombatPower)
                             .ToArray())
                {
                    var receiver = buckets
                        .Where(bucket => !ReferenceEquals(bucket, donor)
                                         && bucket.Count >= donor.Count
                                         && bucket.Count < raid.PlayerCount
                                         && CanJoinRaidTeamBucket(bucket, member, maxSameClassPerTeam))
                        .OrderByDescending(bucket => bucket.Count)
                        .ThenByDescending(bucket => CalculateAvailabilityHours(
                            bucket.Select(existing => existing.OwnerId).Append(member.OwnerId),
                            availabilityByPerson,
                            settings.MinimumCommonMinutes))
                        .ThenBy(bucket => bucket.Sum(existing => existing.CombatPower))
                        .FirstOrDefault();
                    if (receiver is null)
                    {
                        continue;
                    }

                    donor.Remove(member);
                    receiver.Add(member);
                    moved = true;
                    break;
                }

                if (moved)
                {
                    break;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        // 인원을 당긴 뒤에는 전력이 다시 쏠리므로, 같은 인원수를 유지한 채
        // 공대 간 평균 전투력 차이가 줄어드는 교환을 반복합니다.
        for (var pass = 0; pass < 40; pass++)
        {
            var improved = false;
            for (var a = 0; a < buckets.Length && !improved; a++)
            {
                for (var b = a + 1; b < buckets.Length && !improved; b++)
                {
                    if (buckets[a].Count == 0 || buckets[b].Count == 0)
                    {
                        continue;
                    }

                    var currentScore = CalculateRebalanceScore(
                        buckets[a],
                        buckets[b],
                        availabilityByPerson,
                        settings);
                    for (var i = 0; i < buckets[a].Count && !improved; i++)
                    {
                        for (var j = 0; j < buckets[b].Count && !improved; j++)
                        {
                            var left = buckets[a][i];
                            var right = buckets[b][j];

                            // 교환해도 같은 사람이 한 공대에 겹치지 않아야 합니다.
                            if (buckets[a].Any(m => !ReferenceEquals(m, left) && m.OwnerId == right.OwnerId)
                                || buckets[b].Any(m => !ReferenceEquals(m, right) && m.OwnerId == left.OwnerId))
                            {
                                continue;
                            }

                            buckets[a][i] = right;
                            buckets[b][j] = left;
                            if (!HasValidRaidTeamClassCounts(buckets[a], maxSameClassPerTeam)
                                || !HasValidRaidTeamClassCounts(buckets[b], maxSameClassPerTeam))
                            {
                                buckets[a][i] = left;
                                buckets[b][j] = right;
                                continue;
                            }

                            var swappedScore = CalculateRebalanceScore(
                                buckets[a],
                                buckets[b],
                                availabilityByPerson,
                                settings);
                            if (swappedScore > currentScore)
                            {
                                improved = true;
                            }
                            else
                            {
                                buckets[a][i] = left;
                                buckets[b][j] = right;
                            }
                        }
                    }
                }
            }

            if (!improved)
            {
                break;
            }
        }

        // 비어버린 공대는 결과에서 제외합니다.
        var keptTeams = new List<RaidTeamPlan>();
        var keptBuckets = new List<List<GameCharacter>>();
        for (var bucketIndex = 0; bucketIndex < buckets.Length; bucketIndex++)
        {
            if (buckets[bucketIndex].Count == 0)
            {
                plans.Remove(raidTeams[bucketIndex]);
                continue;
            }

            keptTeams.Add(raidTeams[bucketIndex]);
            keptBuckets.Add(buckets[bucketIndex]);
        }

        raidTeams = keptTeams.ToArray();
        buckets = keptBuckets.ToArray();

        for (var teamIndex = 0; teamIndex < raidTeams.Length; teamIndex++)
        {
            var plan = raidTeams[teamIndex];
            var members = buckets[teamIndex];
            var parties = new List<SubPartyPlan>();
            var partyCount = Math.Max(1, (int)Math.Ceiling((decimal)raid.PlayerCount / raid.PartySize));

            // 파티마다 서포터가 한 명씩 가도록 하되, 같은 파티의 직업은 중복시키지 않습니다.
            var supporters = members
                .Where(m => m.Role == CharacterRole.Supporter)
                .OrderBy(m => m.CombatPower)
                .ToList();
            var dealers = members.Where(m => m.Role == CharacterRole.Dealer)
                .OrderByDescending(m => m.CombatPower)
                .ToList();
            var slots = Enumerable.Range(0, partyCount).Select(_ => new List<GameCharacter>()).ToArray();
            foreach (var supporter in supporters)
            {
                var slot = slots
                    .Where(candidate => candidate.Count < raid.PartySize
                                        && !ContainsSameClass(candidate, supporter))
                    .OrderBy(candidate => candidate.Any(member => member.Role == CharacterRole.Supporter))
                    .ThenBy(candidate => candidate.Count)
                    .FirstOrDefault();
                if (slot is null)
                {
                    leftovers.Add(supporter);
                    continue;
                }

                slot.Add(supporter);
            }

            foreach (var dealer in dealers)
            {
                var slot = slots
                    .Where(candidate => candidate.Count < raid.PartySize
                                        && !ContainsSameClass(candidate, dealer))
                    .OrderBy(x => x.Count)
                    .FirstOrDefault();
                if (slot is null)
                {
                    leftovers.Add(dealer);
                    continue;
                }

                slot.Add(dealer);
            }

            for (var partyIndex = 0; partyIndex < slots.Length; partyIndex++)
            {
                parties.Add(new SubPartyPlan(partyIndex + 1, slots[partyIndex]));
            }

            var averages = parties
                .Where(party => party.Members.Count > 0)
                .Select(party => party.Members.Average(m => (decimal)m.CombatPower))
                .ToArray();
            var placedMembers = slots.SelectMany(slot => slot).ToArray();
            var commonAvailability = FindBestCommonAvailability(
                placedMembers.Select(member => member.OwnerId),
                availabilityByPerson,
                settings.MinimumCommonMinutes);

            plans[plans.IndexOf(plan)] = plan with
            {
                Day = commonAvailability?.Day ?? DefaultDay,
                StartMinute = commonAvailability?.StartMinute ?? 0,
                EndMinute = commonAvailability?.EndMinute ?? 0,
                Parties = parties,
                AverageCombatPower = placedMembers.Length == 0
                    ? 0
                    : placedMembers.Average(m => (decimal)m.CombatPower),
                PartyAveragePowerSpread = averages.Length == 0 ? 0 : averages.Max() - averages.Min()
            };
        }

        return leftovers;
    }

    private static decimal CharacterPreferenceScore(
        CompositionCandidate candidate,
        IReadOnlyDictionary<Guid, HashSet<Guid>> usedCharactersByOwner,
        PartyCompositionSettings settings) =>
        candidate.Character.CombatPower / 1000m
        + (WasPreviouslyUsed(candidate, usedCharactersByOwner) ? Math.Max(0, settings.CharacterSwitchPenalty) : 0);

    private static decimal CandidateTeamFitScore(
        CompositionCandidate candidate,
        IReadOnlyCollection<CompositionCandidate> selected,
        IReadOnlyDictionary<Guid, IReadOnlyList<AvailabilityWindow>> availabilityByPerson,
        IReadOnlyDictionary<Guid, HashSet<Guid>> usedCharactersByOwner,
        PartyCompositionSettings settings)
    {
        var ownerIds = selected
            .Select(member => member.Person.Id)
            .Append(candidate.Person.Id);
        var availabilityHours = CalculateAvailabilityHours(
            ownerIds,
            availabilityByPerson,
            settings.MinimumCommonMinutes);
        return CharacterPreferenceScore(candidate, usedCharactersByOwner, settings)
               + availabilityHours * Math.Max(0, settings.AvailabilityWeight);
    }

    private static decimal CalculateRebalanceScore(
        IReadOnlyCollection<GameCharacter> first,
        IReadOnlyCollection<GameCharacter> second,
        IReadOnlyDictionary<Guid, IReadOnlyList<AvailabilityWindow>> availabilityByPerson,
        PartyCompositionSettings settings)
    {
        var availabilityHours = CalculateAvailabilityHours(
                                    first.Select(member => member.OwnerId),
                                    availabilityByPerson,
                                    settings.MinimumCommonMinutes)
                                + CalculateAvailabilityHours(
                                    second.Select(member => member.OwnerId),
                                    availabilityByPerson,
                                    settings.MinimumCommonMinutes);
        var powerGap = Math.Abs(BucketAverage(first.ToList()) - BucketAverage(second.ToList()));
        return availabilityHours * Math.Max(0, settings.AvailabilityWeight)
               - powerGap / 1000m * Math.Max(0, settings.CombatPowerBalanceWeight);
    }

    private static decimal CalculateAvailabilityHours(
        IEnumerable<Guid> ownerIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<AvailabilityWindow>> availabilityByPerson,
        int minimumCommonMinutes)
    {
        var slot = FindBestCommonAvailability(ownerIds, availabilityByPerson, minimumCommonMinutes);
        return slot is null ? 0m : slot.DurationMinutes / 60m;
    }

    private static CommonAvailabilitySlot? FindBestCommonAvailability(
        IEnumerable<Guid> ownerIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<AvailabilityWindow>> availabilityByPerson,
        int minimumCommonMinutes)
    {
        var owners = ownerIds.Distinct().ToArray();
        if (owners.Length == 0 || owners.Any(ownerId => !availabilityByPerson.ContainsKey(ownerId)))
        {
            return null;
        }

        CommonAvailabilitySlot? best = null;
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var intersections = availabilityByPerson[owners[0]]
                .Where(window => window.Day == day && window.EndMinute > window.StartMinute)
                .Select(window => (window.StartMinute, window.EndMinute))
                .ToList();

            foreach (var ownerId in owners.Skip(1))
            {
                var ownerWindows = availabilityByPerson[ownerId]
                    .Where(window => window.Day == day && window.EndMinute > window.StartMinute)
                    .ToArray();
                intersections = intersections
                    .SelectMany(current => ownerWindows.Select(window => (
                        StartMinute: Math.Max(current.StartMinute, window.StartMinute),
                        EndMinute: Math.Min(current.EndMinute, window.EndMinute))))
                    .Where(interval => interval.EndMinute > interval.StartMinute)
                    .Distinct()
                    .ToList();
                if (intersections.Count == 0)
                {
                    break;
                }
            }

            foreach (var intersection in intersections)
            {
                var duration = intersection.EndMinute - intersection.StartMinute;
                if (duration < Math.Max(1, minimumCommonMinutes))
                {
                    continue;
                }

                var candidate = new CommonAvailabilitySlot(
                    day,
                    intersection.StartMinute,
                    intersection.EndMinute);
                if (best is null
                    || candidate.DurationMinutes > best.DurationMinutes
                    || candidate.DurationMinutes == best.DurationMinutes
                    && GetDayOrder(candidate.Day) < GetDayOrder(best.Day))
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static int GetDayOrder(DayOfWeek day) =>
        ((int)day - (int)DayOfWeek.Wednesday + 7) % 7;

    private static bool CanAddClassToRaidTeam(
        IReadOnlyDictionary<string, int> classCounts,
        GameCharacter character,
        int partyCount)
    {
        var classKey = GetClassKey(character);
        return classKey is null
               || !classCounts.TryGetValue(classKey, out var count)
               || count < partyCount;
    }

    private static void IncrementClassCount(
        IDictionary<string, int> classCounts,
        GameCharacter character)
    {
        var classKey = GetClassKey(character);
        if (classKey is null)
        {
            return;
        }

        classCounts.TryGetValue(classKey, out var currentCount);
        classCounts[classKey] = currentCount + 1;
    }

    private static bool ContainsSameClass(
        IEnumerable<CompositionCandidate> party,
        GameCharacter character) =>
        party.Any(candidate => SameClass(candidate.Character, character));

    private static bool ContainsSameClass(
        IEnumerable<GameCharacter> party,
        GameCharacter character) =>
        party.Any(candidate => SameClass(candidate, character));

    private static bool HasDuplicateClasses(IEnumerable<CompositionCandidate> party) =>
        party.Select(candidate => GetClassKey(candidate.Character))
            .Where(classKey => classKey is not null)
            .GroupBy(classKey => classKey!, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);

    private static bool CanJoinRaidTeamBucket(
        IReadOnlyCollection<GameCharacter> bucket,
        GameCharacter character,
        int maxSameClassCount) =>
        bucket.All(existing => existing.OwnerId != character.OwnerId)
        && (GetClassKey(character) is not { } classKey
            || bucket.Count(existing => string.Equals(
                GetClassKey(existing),
                classKey,
                StringComparison.OrdinalIgnoreCase)) < maxSameClassCount);

    private static bool HasValidRaidTeamClassCounts(
        IEnumerable<GameCharacter> members,
        int maxSameClassCount) =>
        members.Select(GetClassKey)
            .Where(classKey => classKey is not null)
            .GroupBy(classKey => classKey!, StringComparer.OrdinalIgnoreCase)
            .All(group => group.Count() <= maxSameClassCount);

    private static bool SameClass(GameCharacter left, GameCharacter right)
    {
        var leftClass = GetClassKey(left);
        var rightClass = GetClassKey(right);
        return leftClass is not null
               && rightClass is not null
               && string.Equals(leftClass, rightClass, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetClassKey(GameCharacter character) =>
        string.IsNullOrWhiteSpace(character.ClassName) ? null : character.ClassName.Trim();

    private static string BuildUnassignedReason(
        IReadOnlyCollection<CompositionCandidate> remaining)
    {
        var distinctOwners = remaining.Select(candidate => candidate.Person.Id).Distinct().Count();
        if (distinctOwners == 0)
        {
            return "편성할 수 있는 캐릭터가 남지 않았습니다.";
        }

        return $"남은 참여자 {distinctOwners}명의 캐릭터로는 추가 공격대의 역할 조건을 충족할 수 없습니다.";
    }

    private sealed record CompositionCandidate(
        WeeklyRaidSelection Selection,
        GameCharacter Character,
        Person Person,
        RaidContent Raid);

    private sealed record TeamCandidate(
        IReadOnlyList<CompositionCandidate> Candidates,
        DayOfWeek Day,
        int StartMinute,
        int EndMinute,
        decimal SynergyScore,
        decimal OptimizationScore)
    {
        public int DurationMinutes => EndMinute - StartMinute;
        public int TotalCombatPower => Candidates.Sum(candidate => candidate.Character.CombatPower);
    }

    private sealed record CommonAvailabilitySlot(
        DayOfWeek Day,
        int StartMinute,
        int EndMinute)
    {
        public int DurationMinutes => EndMinute - StartMinute;
    }
}

public sealed record RaidPartyCompositionResult(
    IReadOnlyList<RaidTeamPlan> Teams,
    IReadOnlyList<UnassignedRaidCandidates> UnassignedGroups);

public sealed record RaidTeamPlan(
    RaidContent Raid,
    int TeamNumber,
    DayOfWeek Day,
    int StartMinute,
    int EndMinute,
    IReadOnlyList<SubPartyPlan> Parties,
    decimal AverageCombatPower,
    decimal PartyAveragePowerSpread,
    decimal SynergyScore)
{
    public int FilledCount => Parties.Sum(party => party.Members.Count);

    public int VacancyCount => Math.Max(0, Raid.PlayerCount - FilledCount);

    public int SupporterVacancyCount => Math.Max(
        0,
        (int)Math.Ceiling((decimal)Raid.PlayerCount / Math.Max(1, Raid.PartySize))
        - Parties.Sum(party => party.Members.Count(member => member.Role == CharacterRole.Supporter)));

    public int DealerVacancyCount => Math.Max(0, VacancyCount - SupporterVacancyCount);
}

public sealed record SubPartyPlan(
    int PartyNumber,
    IReadOnlyList<GameCharacter> Members);

public sealed record UnassignedRaidCandidates(
    RaidContent Raid,
    IReadOnlyList<GameCharacter> Characters,
    string Reason);

public sealed record PartyAssignmentMetrics(
    decimal SynergyScore,
    decimal PartyAveragePowerSpread);
