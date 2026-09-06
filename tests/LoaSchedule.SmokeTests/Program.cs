using LoaSchedule.Domain.Models;
using LoaSchedule.Domain.Services;
using LoaSchedule.Infrastructure.LostArkApi;
using LoaSchedule.Infrastructure.Persistence;

if (args is ["--rewrite-json", var jsonPath])
{
    if (!File.Exists(jsonPath))
    {
        throw new FileNotFoundException("다시 저장할 JSON 파일을 찾을 수 없습니다.", jsonPath);
    }

    var rewriteStore = new JsonAppStateStore(Path.GetFullPath(jsonPath));
    var rewriteState = await rewriteStore.LoadAsync();
    await rewriteStore.SaveAsync(rewriteState);
    Console.WriteLine($"JSON을 UTF-8 한글 형식으로 다시 저장했습니다: {rewriteStore.FilePath}");
    return;
}

var temporaryFile = Path.Combine(Path.GetTempPath(), $"loa-schedule-{Guid.NewGuid():N}.json");

try
{
    var store = new JsonAppStateStore(temporaryFile);
    var state = await store.LoadAsync();
    Ensure(state.Raids.Count > 0, "초기 레이드 데이터가 없습니다.");
    await TestLostArkApiClientAsync();

    var person = new Person { Name = "테스트 관리자" };
    var character = new GameCharacter
    {
        OwnerId = person.Id,
        Name = "테스트 캐릭터",
        ClassName = "바드",
        Role = CharacterRole.Supporter,
        ItemLevel = 1730,
        CombatPower = 5000
    };

    state.People.Add(person);
    state.Characters.Add(character);
    state.AvailabilityWindows.Add(new AvailabilityWindow
    {
        PersonId = person.Id,
        Day = DayOfWeek.Saturday,
        StartMinute = 19 * 60,
        EndMinute = 25 * 60
    });

    var recommendationService = new RaidRecommendationService();
    var recommendations = recommendationService.RecommendTopRaids(character, state.Raids);
    Ensure(recommendations.Count == 3, "상위 레이드가 3개 추천되지 않았습니다.");
    Ensure(recommendations.All(raid => raid.MinimumItemLevel <= character.ItemLevel), "입장 불가능한 레이드가 추천되었습니다.");
    Ensure(recommendations.Select(raid => raid.GroupKey).Distinct().Count() == 3, "동일 콘텐츠 난이도가 중복 추천되었습니다.");

    var providerOne = new GameCharacter
    {
        OwnerId = person.Id,
        Name = "시너지 제공자 1",
        ClassName = "배틀마스터",
        BuildName = "오의 강화",
        ItemLevel = 1730
    };
    var providerTwo = new GameCharacter
    {
        OwnerId = person.Id,
        Name = "시너지 제공자 2",
        ClassName = "스트라이커",
        ItemLevel = 1730
    };
    var receiver = new GameCharacter
    {
        OwnerId = person.Id,
        Name = "시너지 수혜자",
        ClassName = "데빌헌터",
        BuildName = "핸드거너",
        ItemLevel = 1730
    };
    var damageSynergyOne = new SynergyDefinition
    {
        ClassName = "배틀마스터",
        RequiredBuildName = "오의 강화",
        Kind = SynergyKind.DamageIncrease,
        Value = 6,
        StackingGroup = "동일 피해 그룹"
    };
    var damageSynergyTwo = new SynergyDefinition
    {
        ClassName = "스트라이커",
        Kind = SynergyKind.DamageIncrease,
        Value = 8,
        StackingGroup = "동일 피해 그룹"
    };
    var criticalSynergy = new SynergyDefinition
    {
        ClassName = "배틀마스터",
        Kind = SynergyKind.CriticalRateIncrease,
        Value = 10,
        StackingGroup = "치명타 그룹"
    };
    var criticalExclusion = new SynergyExclusion
    {
        ReceiverClassName = "데빌헌터",
        ReceiverBuildName = "핸드거너",
        Kind = SynergyKind.CriticalRateIncrease,
        Reason = "테스트 적용 불가"
    };
    state.Synergies.AddRange([damageSynergyOne, damageSynergyTwo, criticalSynergy]);
    state.SynergyExclusions.Add(criticalExclusion);

    var synergyService = new SynergyEvaluationService();
    var effectiveSynergies = synergyService.GetEffectiveSynergies(
        receiver,
        [receiver, providerOne, providerTwo],
        state.Synergies,
        state.SynergyExclusions);
    Ensure(effectiveSynergies.Count == 1, "중첩 또는 적용 불가 시너지 판정에 실패했습니다.");
    Ensure(effectiveSynergies[0].Definition.Value == 8, "같은 중첩 그룹의 최고 수치가 선택되지 않았습니다.");
    Ensure(effectiveSynergies.All(applied => applied.Definition.Kind != SynergyKind.CriticalRateIncrease), "적용 불가 시너지가 포함되었습니다.");

    var gameWeekService = new GameWeekService();
    var beforeReset = gameWeekService.GetWeekStart(new DateTime(2026, 9, 2, 5, 59, 0));
    var atReset = gameWeekService.GetWeekStart(new DateTime(2026, 9, 2, 6, 0, 0));
    Ensure(beforeReset == new DateTime(2026, 8, 26, 6, 0, 0), "수요일 06시 이전 주차 계산이 잘못되었습니다.");
    Ensure(atReset == new DateTime(2026, 9, 2, 6, 0, 0), "수요일 06시 초기화 주차 계산이 잘못되었습니다.");

    TestPartyComposition(atReset);
    TestAvailabilityWeight(atReset);
    TestDuplicateClassSeparation(atReset);
    TestFullPartyPacking(atReset);
    TestUnlimitedRaidTeams(atReset);
    TestRaidRunOrderOptimization();
    TestCharacterReuseOptimization(atReset);
    TestScheduleReferenceProtection(atReset);

    var weeklySelection = new WeeklyRaidSelection
    {
        WeekStartLocal = atReset,
        CharacterId = character.Id,
        RaidContentId = recommendations[0].Id
    };
    var progressService = new WeeklyRaidProgressService();
    progressService.Advance(weeklySelection, recommendations[0]);
    Ensure(weeklySelection.CompletedGateCount == 1, "관문 진행 처리에 실패했습니다.");
    for (var gate = 1; gate < recommendations[0].GateCount; gate++)
    {
        progressService.Advance(weeklySelection, recommendations[0]);
    }
    Ensure(progressService.IsCompleted(weeklySelection, recommendations[0]), "레이드 완료 판정에 실패했습니다.");
    progressService.Advance(weeklySelection, recommendations[0]);
    Ensure(weeklySelection.CompletedGateCount == recommendations[0].GateCount, "완료 관문 수가 최대 관문을 초과했습니다.");
    state.WeeklyRaidSelections.Add(weeklySelection);
    state.PartyCompositionSettings.CharacterSwitchPenalty = 40;
    state.PartyCompositionSettings.AvailabilityWeight = 3;
    state.LostArkApiToken = "json-api-token";
    state.RaidTeamSchedules.Add(new RaidTeamSchedule
    {
        WeekStartLocal = atReset,
        RaidContentId = recommendations[0].Id,
        TeamNumber = 1,
        Day = DayOfWeek.Saturday,
        StartMinute = 19 * 60,
        EndMinute = 23 * 60,
        IsConfirmed = true,
        Members =
        [
            new RaidTeamMember
            {
                CharacterId = character.Id,
                PartyNumber = 1
            }
        ]
    });

    await store.SaveAsync(state);
    var savedJson = await File.ReadAllTextAsync(temporaryFile);
    Ensure(savedJson.Contains("테스트 관리자", StringComparison.Ordinal), "한글이 JSON에 그대로 저장되지 않았습니다.");
    Ensure(!savedJson.Contains("\\uD14C\\uC2A4\\uD2B8", StringComparison.OrdinalIgnoreCase), "한글이 유니코드 이스케이프로 저장되었습니다.");
    var reloaded = await store.LoadAsync();
    Ensure(reloaded.People.Count == 1, "참여자 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.Characters.Count == 1, "캐릭터 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.AvailabilityWindows.Count == 1, "가능 시간 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.AvailabilityWindows[0].EndMinute == 25 * 60, "익일 시간 저장 검증에 실패했습니다.");
    Ensure(reloaded.WeeklyRaidSelections.Count == 1, "주간 레이드 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.WeeklyRaidSelections[0].CompletedGateCount == recommendations[0].GateCount, "관문 진행 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.Synergies.Count == 3, "시너지 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.SynergyExclusions.Count == 1, "시너지 적용 불가 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.RaidTeamSchedules.Count == 1 && reloaded.RaidTeamSchedules[0].IsConfirmed, "공격대 편성 확정 상태 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.RaidTeamSchedules[0].Members.Count == 1, "공격대 구성원 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.PartyCompositionSettings.CharacterSwitchPenalty == 40, "편성 최적화 설정 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.PartyCompositionSettings.AvailabilityWeight == 3, "가능 시간 가중치 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.LostArkApiToken == "json-api-token", "Lost Ark API 키 JSON 저장 검증에 실패했습니다.");
    Ensure(reloaded.SchemaVersion == 5, "최신 데이터 스키마 버전이 저장되지 않았습니다.");

    Console.WriteLine("Smoke tests passed: Lost Ark API parsing/cache, weekly reset, recommendations, optimized composition, reference protection, JSON round-trip.");
}
finally
{
    if (File.Exists(temporaryFile))
    {
        File.Delete(temporaryFile);
    }
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void TestPartyComposition(DateTime weekStart)
{
    foreach (var playerCount in new[] { 4, 8, 16 })
    {
        var raid = new RaidContent
        {
            Name = $"{playerCount}인 테스트",
            Difficulty = "테스트",
            PlayerCount = playerCount,
            PartySize = 4,
            GateCount = 2,
            IsActive = true
        };
        var people = Enumerable.Range(0, playerCount)
            .Select(index => new Person { Name = $"참여자 {index + 1}" })
            .ToArray();
        var characters = people
            .Select((owner, index) => new GameCharacter
            {
                OwnerId = owner.Id,
                Name = $"편성 캐릭터 {index + 1}",
                ClassName = index < playerCount / 4
                    ? $"서포터 클래스 {index + 1}"
                    : $"딜러 클래스 {index + 1}",
                Role = index < playerCount / 4 ? CharacterRole.Supporter : CharacterRole.Dealer,
                CombatPower = 10_000 + index * 100,
                ItemLevel = 1700
            })
            .ToArray();
        var windows = people
            .Select(owner => new AvailabilityWindow
            {
                PersonId = owner.Id,
                Day = DayOfWeek.Saturday,
                StartMinute = 19 * 60,
                EndMinute = 23 * 60
            })
            .ToArray();
        var selections = characters
            .Select(character => new WeeklyRaidSelection
            {
                WeekStartLocal = weekStart,
                CharacterId = character.Id,
                RaidContentId = raid.Id
            })
            .ToArray();

        var result = new RaidPartyCompositionService().Compose(
            weekStart,
            people,
            characters,
            windows,
            selections,
            [raid],
            [],
            []);

        Ensure(result.Teams.Count == 1, $"{playerCount}인 공격대가 완성되지 않았습니다.");
        Ensure(result.Teams[0].Parties.Count == playerCount / 4, $"{playerCount}인 공격대의 파티 수가 잘못되었습니다.");
        Ensure(result.Teams[0].Parties.All(party => party.Members.Count == 4), "4인 파티 인원 구성이 잘못되었습니다.");
        Ensure(result.Teams[0].Parties.All(party => party.Members.Count(member => member.Role == CharacterRole.Supporter) == 1), "파티별 서포터 배치가 잘못되었습니다.");
        Ensure(
            result.Teams[0].Parties.All(party =>
                party.Members.Select(member => member.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                == party.Members.Count),
            "같은 4인 파티에 동일 직업이 중복 편성되었습니다.");
        Ensure(result.Teams[0].Parties.SelectMany(party => party.Members).Select(member => member.OwnerId).Distinct().Count() == playerCount, "한 공격대에 동일 참여자가 중복 배치되었습니다.");
        // 가능 시간은 소프트 가중치이므로 등록하지 않아도 공격대와 인원 수는 유지되어야 합니다.
        var withoutWindows = new RaidPartyCompositionService().Compose(
            weekStart,
            people,
            characters,
            [],
            selections,
            [raid],
            [],
            []);
        Ensure(withoutWindows.Teams.Count == result.Teams.Count, "가능 시간이 없을 때 공격대 수가 달라집니다.");
        Ensure(
            withoutWindows.Teams[0].FilledCount == result.Teams[0].FilledCount,
            "가능 시간이 없을 때 편성 인원이 누락되었습니다.");
        Ensure(result.Teams[0].PartyAveragePowerSpread <= 200, $"{playerCount}인 공격대의 파티 전투력 격차가 충분히 최적화되지 않았습니다.");
        Ensure(result.UnassignedGroups.Count == 0, $"{playerCount}인 정상 편성에 미편성 인원이 발생했습니다.");
    }

    var incompleteRaid = new RaidContent
    {
        Name = "인원 부족 테스트",
        Difficulty = "테스트",
        PlayerCount = 4,
        PartySize = 4,
        GateCount = 1
    };
    // 4인 레이드에 3명(서포터 1, 딜러 2)만 있어 한 자리가 공석으로 남는 상황입니다.
    var incompletePeople = Enumerable.Range(0, 3)
        .Select(index => new Person { Name = $"미편성 참여자 {index + 1}" })
        .ToArray();
    var incompleteCharacters = incompletePeople
        .Select((owner, index) => new GameCharacter
        {
            OwnerId = owner.Id,
            Name = $"미편성 캐릭터 {index + 1}",
            ClassName = $"인원 부족 직업 {index + 1}",
            Role = index == 0 ? CharacterRole.Supporter : CharacterRole.Dealer
        })
        .ToArray();
    var incompleteResult = new RaidPartyCompositionService().Compose(
        weekStart,
        incompletePeople,
        incompleteCharacters,
        [],
        incompleteCharacters.Select(character => new WeeklyRaidSelection
        {
            WeekStartLocal = weekStart,
            CharacterId = character.Id,
            RaidContentId = incompleteRaid.Id
        }),
        [incompleteRaid],
        [],
        []);

    // 인원이 모자라도 공석을 남기고 공격대를 구성합니다.
    Ensure(incompleteResult.Teams.Count == 1, "인원이 부족할 때 공격대가 구성되지 않았습니다.");

    var incompleteTeam = incompleteResult.Teams[0];
    Ensure(
        incompleteTeam.FilledCount == 3,
        $"참여자 3명이 모두 편성되어야 하는데 {incompleteTeam.FilledCount}명이 편성되었습니다.");
    Ensure(incompleteTeam.VacancyCount == 1, "공석 1자리가 계산되지 않았습니다.");
    Ensure(
        incompleteTeam.SupporterVacancyCount == 0 && incompleteTeam.DealerVacancyCount == 1,
        "공석의 역할 구분이 올바르지 않습니다. 딜러 1자리여야 합니다.");
    Ensure(
        incompleteTeam.Parties.Sum(party => party.Members.Count(member => member.Role == CharacterRole.Supporter)) == 1,
        "서포터가 편성되지 않았습니다.");

    // 인원을 모두 소진했으므로 남는 미편성 캐릭터는 없습니다.
    Ensure(incompleteResult.UnassignedGroups.Count == 0, "남은 캐릭터가 없는데 미편성이 발생했습니다.");

    // 서포터가 한 명도 없어도 딜러만으로 공격대를 구성합니다.
    var dealerOnlyPeople = Enumerable.Range(0, 4)
        .Select(index => new Person { Name = $"딜러만 {index + 1}" })
        .ToArray();
    var dealerOnlyCharacters = dealerOnlyPeople
        .Select((owner, index) => new GameCharacter
        {
            OwnerId = owner.Id,
            Name = $"딜러 전용 {index + 1}",
            ClassName = $"딜러 전용 직업 {index + 1}",
            Role = CharacterRole.Dealer
        })
        .ToArray();
    var dealerOnlyResult = new RaidPartyCompositionService().Compose(
        weekStart,
        dealerOnlyPeople,
        dealerOnlyCharacters,
        [],
        dealerOnlyCharacters.Select(character => new WeeklyRaidSelection
        {
            WeekStartLocal = weekStart,
            CharacterId = character.Id,
            RaidContentId = incompleteRaid.Id
        }),
        [incompleteRaid],
        [],
        []);

    Ensure(dealerOnlyResult.Teams.Count == 1, "서포터 없는 공격대가 구성되지 않았습니다.");
    Ensure(
        dealerOnlyResult.Teams[0].FilledCount == 4
        && dealerOnlyResult.Teams[0].Parties
            .SelectMany(party => party.Members)
            .All(member => member.Role == CharacterRole.Dealer),
        "서포터 없는 공격대에 딜러 4명이 올바르게 편성되지 않았습니다.");
    Ensure(dealerOnlyResult.UnassignedGroups.Count == 0, "편성 가능한 딜러가 미편성으로 남았습니다.");
}

static void TestAvailabilityWeight(DateTime weekStart)
{
    var raid = new RaidContent
    {
        Name = "가능 시간 가중치 테스트",
        Difficulty = "테스트",
        PlayerCount = 4,
        PartySize = 4,
        GateCount = 1,
        IsActive = true
    };
    var people = Enumerable.Range(0, 5)
        .Select(index => new Person { Name = $"시간 참여자 {index + 1}" })
        .ToArray();
    var characters = people
        .Select((owner, index) => new GameCharacter
        {
            OwnerId = owner.Id,
            Name = $"시간 캐릭터 {index + 1}",
            ClassName = $"시간 직업 {index + 1}",
            Role = CharacterRole.Dealer,
            ItemLevel = 1800,
            CombatPower = index == 4 ? 9000 : 5000,
            IsActive = true
        })
        .ToArray();
    var windows = people
        .Take(4)
        .Select(owner => new AvailabilityWindow
        {
            PersonId = owner.Id,
            Day = DayOfWeek.Thursday,
            StartMinute = 19 * 60,
            EndMinute = 23 * 60
        })
        .Append(new AvailabilityWindow
        {
            PersonId = people[4].Id,
            Day = DayOfWeek.Friday,
            StartMinute = 19 * 60,
            EndMinute = 20 * 60
        })
        .ToArray();
    var selections = characters.Select(character => new WeeklyRaidSelection
    {
        WeekStartLocal = weekStart,
        CharacterId = character.Id,
        RaidContentId = raid.Id,
        IsConfirmed = true
    }).ToArray();
    var service = new RaidPartyCompositionService();

    var weighted = service.Compose(
        weekStart,
        people,
        characters,
        windows,
        selections,
        [raid],
        [],
        [],
        new PartyCompositionSettings
        {
            AvailabilityWeight = 20,
            CombatPowerBalanceWeight = 0,
            CharacterSwitchPenalty = 0,
            MinimumCommonMinutes = 30
        });
    var weightedFullTeam = weighted.Teams.First(team => team.FilledCount == 4);
    var weightedCharacterIds = weightedFullTeam.Parties
        .SelectMany(party => party.Members)
        .Select(character => character.Id)
        .ToHashSet();
    Ensure(!weightedCharacterIds.Contains(characters[4].Id), "가능 시간이 겹치지 않는 참여자가 높은 가중치 편성에 우선 포함되었습니다.");
    Ensure(weightedFullTeam.Day == DayOfWeek.Thursday
           && weightedFullTeam.StartMinute == 19 * 60
           && weightedFullTeam.EndMinute == 23 * 60,
        "공격대 공통 가능 시간이 일정에 반영되지 않았습니다.");

    var unweighted = service.Compose(
        weekStart,
        people,
        characters,
        windows,
        selections,
        [raid],
        [],
        [],
        new PartyCompositionSettings
        {
            AvailabilityWeight = 0,
            CombatPowerBalanceWeight = 0,
            CharacterSwitchPenalty = 0,
            MinimumCommonMinutes = 30
        });
    var unweightedCharacterIds = unweighted.Teams.First(team => team.FilledCount == 4).Parties
        .SelectMany(party => party.Members)
        .Select(character => character.Id)
        .ToHashSet();
    Ensure(unweightedCharacterIds.Contains(characters[4].Id), "가능 시간 가중치 0에서도 시간 점수가 적용되었습니다.");
}

static void TestDuplicateClassSeparation(DateTime weekStart)
{
    var raid = new RaidContent
    {
        Name = "동일 직업 분리 테스트",
        Difficulty = "테스트",
        PlayerCount = 4,
        PartySize = 4,
        GateCount = 1,
        IsActive = true
    };
    var classes = new[] { "소서리스", "소서리스", "바드", "블레이드", "기공사" };
    var people = classes
        .Select((_, index) => new Person { Name = $"직업 분리 참여자 {index + 1}" })
        .ToArray();
    var characters = people
        .Select((owner, index) => new GameCharacter
        {
            OwnerId = owner.Id,
            Name = $"직업 분리 캐릭터 {index + 1}",
            ClassName = classes[index],
            Role = classes[index] == "바드" ? CharacterRole.Supporter : CharacterRole.Dealer,
            CombatPower = 10_000 - index * 100,
            ItemLevel = 1770
        })
        .ToArray();

    var result = new RaidPartyCompositionService().Compose(
        weekStart,
        people,
        characters,
        [],
        characters.Select(character => new WeeklyRaidSelection
        {
            WeekStartLocal = weekStart,
            CharacterId = character.Id,
            RaidContentId = raid.Id
        }),
        [raid],
        [],
        []);

    var assigned = result.Teams
        .SelectMany(team => team.Parties)
        .SelectMany(party => party.Members)
        .ToArray();
    Ensure(assigned.Length == characters.Length, "동일 직업 분리 과정에서 캐릭터가 누락되었습니다.");
    Ensure(
        result.Teams.SelectMany(team => team.Parties).All(party =>
            party.Members.Select(member => member.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == party.Members.Count),
        "동일 직업 두 명이 같은 4인 파티에 편성되었습니다.");
    Ensure(result.Teams.Count == 2, "중복 직업을 분리한 후순위 공격대가 생성되지 않았습니다.");
}

static void TestFullPartyPacking(DateTime weekStart)
{
    var raid = new RaidContent
    {
        Name = "완성 파티 우선 테스트",
        Difficulty = "테스트",
        PlayerCount = 4,
        PartySize = 4,
        GateCount = 1,
        IsActive = true
    };
    var people = Enumerable.Range(0, 6)
        .Select(index => new Person { Name = $"파티 압축 참여자 {index + 1}" })
        .ToArray();
    var characters = people
        .Select((owner, index) => new GameCharacter
        {
            OwnerId = owner.Id,
            Name = $"파티 압축 캐릭터 {index + 1}",
            ClassName = $"파티 압축 직업 {index + 1}",
            Role = index == 0 ? CharacterRole.Supporter : CharacterRole.Dealer,
            CombatPower = 10_000 + index * 100,
            ItemLevel = 1770
        })
        .ToArray();

    var result = new RaidPartyCompositionService().Compose(
        weekStart,
        people,
        characters,
        [],
        characters.Select(character => new WeeklyRaidSelection
        {
            WeekStartLocal = weekStart,
            CharacterId = character.Id,
            RaidContentId = raid.Id
        }),
        [raid],
        [],
        []);

    var filledCounts = result.Teams
        .Select(team => team.FilledCount)
        .OrderByDescending(count => count)
        .ToArray();
    Ensure(
        filledCounts.SequenceEqual(new[] { 4, 2 }),
        $"6명을 4명+2명으로 편성해야 하지만 {string.Join("+", filledCounts)}명으로 나뉘었습니다.");
    Ensure(
        result.Teams.SelectMany(team => team.Parties).All(party =>
            party.Members.Select(member => member.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == party.Members.Count),
        "완성 파티 압축 과정에서 동일 직업이 중복되었습니다.");
}

static void TestUnlimitedRaidTeams(DateTime weekStart)
{
    var raid = new RaidContent
    {
        Name = "공대 수 무제한 테스트",
        Difficulty = "하드",
        PlayerCount = 8,
        PartySize = 4,
        GateCount = 2,
        IsActive = true
    };
    var people = Enumerable.Range(0, 5)
        .Select(index => new Person { Name = $"다캐릭 참여자 {index + 1}" })
        .ToArray();
    var characters = new List<GameCharacter>();

    void AddCharacters(Person owner, int count, CharacterRole role, string prefix)
    {
        for (var index = 1; index <= count; index++)
        {
            characters.Add(new GameCharacter
            {
                OwnerId = owner.Id,
                Name = $"{prefix} {index}",
                ClassName = $"{prefix} 직업 {index}",
                Role = role,
                CombatPower = 10_000 + index,
                ItemLevel = 1770
            });
        }
    }

    // 한 참여자의 부캐가 다섯 개씩 있어 최소 다섯 공대가 필요한 구성입니다.
    AddCharacters(people[0], 5, CharacterRole.Dealer, "주력 딜러");
    AddCharacters(people[1], 5, CharacterRole.Supporter, "주력 서포터");
    AddCharacters(people[2], 3, CharacterRole.Dealer, "보조 딜러");
    AddCharacters(people[3], 1, CharacterRole.Dealer, "단일 딜러 A");
    AddCharacters(people[4], 1, CharacterRole.Dealer, "단일 딜러 B");

    var result = new RaidPartyCompositionService().Compose(
        weekStart,
        people,
        characters,
        [],
        characters.Select(character => new WeeklyRaidSelection
        {
            WeekStartLocal = weekStart,
            CharacterId = character.Id,
            RaidContentId = raid.Id
        }),
        [raid],
        [],
        []);

    var assignedCharacters = result.Teams
        .SelectMany(team => team.Parties)
        .SelectMany(party => party.Members)
        .ToArray();

    Ensure(result.Teams.Count == 5, "참여자의 부캐 수만큼 필요한 공격대가 모두 생성되지 않았습니다.");
    Ensure(assignedCharacters.Length == characters.Count, "편성 가능한 부캐가 미편성으로 남았습니다.");
    Ensure(result.UnassignedGroups.Count == 0, "편성 가능한 조합이 미편성으로 분류되었습니다.");
    Ensure(
        result.Teams.All(team =>
        {
            var members = team.Parties.SelectMany(party => party.Members).ToArray();
            return members.Select(member => member.OwnerId).Distinct().Count() == members.Length;
        }),
        "한 공격대 안에 같은 참여자의 캐릭터가 중복 배치되었습니다.");
    Ensure(
        result.Teams.Count(team => team.FilledCount == 2) == 2,
        "마지막 딜러·서포터 2인 조합 공격대가 생성되지 않았습니다.");
}

static void TestRaidRunOrderOptimization()
{
    var owner = new Person { Name = "진행 순서 참여자" };
    var firstCharacter = new GameCharacter
    {
        OwnerId = owner.Id,
        Name = "진행 순서 캐릭터 A"
    };
    var secondCharacter = new GameCharacter
    {
        OwnerId = owner.Id,
        Name = "진행 순서 캐릭터 B"
    };
    var charactersById = new[] { firstCharacter, secondCharacter }
        .ToDictionary(character => character.Id);

    RaidTeamSchedule CreateSchedule(GameCharacter character) => new()
    {
        Members =
        [
            new RaidTeamMember
            {
                CharacterId = character.Id,
                PartyNumber = 1
            }
        ]
    };

    // A-B-A-B 순서를 A-A-B-B로 묶으면 캐릭터 변경이 3회에서 1회로 줄어듭니다.
    var schedules = new[]
    {
        CreateSchedule(firstCharacter),
        CreateSchedule(secondCharacter),
        CreateSchedule(firstCharacter),
        CreateSchedule(secondCharacter)
    };
    var result = new RaidRunOrderService().Optimize(schedules, charactersById);
    var orderedCharacterIds = result.Steps
        .Select(step => step.Schedule.Members[0].CharacterId)
        .ToArray();

    Ensure(result.Steps.Count == schedules.Length, "진행 순서에서 공격대가 누락되었습니다.");
    Ensure(result.TotalSwitchCount == 1, "캐릭터 변경 횟수가 최소 순서로 정리되지 않았습니다.");
    Ensure(
        orderedCharacterIds.Take(2).Distinct().Count() == 1
        && orderedCharacterIds.Skip(2).Distinct().Count() == 1,
        "같은 캐릭터를 사용하는 공격대가 연속으로 배치되지 않았습니다.");

    var attendanceOwnerA = new Person { Name = "연속 참여자 A" };
    var attendanceOwnerB = new Person { Name = "연속 참여자 B" };
    var attendanceCharacterA = new GameCharacter
    {
        OwnerId = attendanceOwnerA.Id,
        Name = "연속 캐릭터 A"
    };
    var attendanceCharacterB = new GameCharacter
    {
        OwnerId = attendanceOwnerB.Id,
        Name = "연속 캐릭터 B"
    };
    var attendanceCharacters = new[] { attendanceCharacterA, attendanceCharacterB }
        .ToDictionary(character => character.Id);
    var attendanceSchedules = new[]
    {
        CreateSchedule(attendanceCharacterA),
        CreateSchedule(attendanceCharacterB),
        CreateSchedule(attendanceCharacterA),
        CreateSchedule(attendanceCharacterB)
    };
    var attendanceResult = new RaidRunOrderService().Optimize(
        attendanceSchedules,
        attendanceCharacters);
    var orderedOwnerIds = attendanceResult.Steps
        .Select(step => attendanceCharacters[step.Schedule.Members[0].CharacterId].OwnerId)
        .ToArray();

    Ensure(attendanceResult.TotalSwitchCount == 0, "참여 연속성 계산 중 불필요한 캐릭터 변경이 발생했습니다.");
    Ensure(attendanceResult.TotalReentryCount == 0, "같은 참여자의 공격대가 분리되어 중간 재참여가 발생했습니다.");
    Ensure(attendanceResult.TotalGapRaidCount == 0, "같은 참여자의 공격대 사이에 불필요한 대기가 발생했습니다.");
    Ensure(
        orderedOwnerIds.Take(2).Distinct().Count() == 1
        && orderedOwnerIds.Skip(2).Distinct().Count() == 1,
        "같은 참여자가 가는 공격대가 연속으로 배치되지 않았습니다.");

    var crowdCharacters = Enumerable.Range(1, 4)
        .Select(index =>
        {
            var crowdOwner = new Person { Name = $"인원 우선 참여자 {index}" };
            return new GameCharacter
            {
                OwnerId = crowdOwner.Id,
                Name = $"인원 우선 캐릭터 {index}"
            };
        })
        .ToArray();
    var crowdCharactersById = crowdCharacters.ToDictionary(character => character.Id);
    RaidTeamSchedule CreateSizedSchedule(int memberCount) => new()
    {
        Members = crowdCharacters
            .Take(memberCount)
            .Select(character => new RaidTeamMember
            {
                CharacterId = character.Id,
                PartyNumber = 1
            })
            .ToList()
    };

    var crowdResult = new RaidRunOrderService().Optimize(
        [
            CreateSizedSchedule(4),
            CreateSizedSchedule(3),
            CreateSizedSchedule(4),
            CreateSizedSchedule(2)
        ],
        crowdCharactersById);
    var orderedMemberCounts = crowdResult.Steps
        .Select(step => step.Schedule.Members.Count)
        .ToArray();
    Ensure(
        orderedMemberCounts.SequenceEqual(orderedMemberCounts.OrderByDescending(count => count)),
        "편성 인원이 적은 공격대가 인원이 많은 공격대보다 먼저 배치되었습니다.");

    var continuityOwnerA = new Person { Name = "연속성 캐릭터 참여자" };
    var continuityOwnerB = new Person { Name = "연속성 참여자 B" };
    var continuityOwnerC = new Person { Name = "연속성 참여자 C" };
    var continuityA1 = new GameCharacter { OwnerId = continuityOwnerA.Id, Name = "연속성 A1" };
    var continuityA2 = new GameCharacter { OwnerId = continuityOwnerA.Id, Name = "연속성 A2" };
    var continuityB = new GameCharacter { OwnerId = continuityOwnerB.Id, Name = "연속성 B" };
    var continuityC = new GameCharacter { OwnerId = continuityOwnerC.Id, Name = "연속성 C" };
    var continuityCharacters = new[] { continuityA1, continuityA2, continuityB, continuityC }
        .ToDictionary(character => character.Id);
    RaidTeamSchedule CreateContinuitySchedule(params GameCharacter[] members) => new()
    {
        Members = members.Select(character => new RaidTeamMember
        {
            CharacterId = character.Id,
            PartyNumber = 1
        }).ToList()
    };

    // 캐릭터만 묶으면 B와 C가 중간에 빠졌다 복귀합니다. B 공격대와 C 공격대를
    // 각각 연속 배치하면 A의 교체는 조금 늘어도 참여자의 재참여는 없어집니다.
    var continuityResult = new RaidRunOrderService().Optimize(
        [
            CreateContinuitySchedule(continuityA1, continuityB),
            CreateContinuitySchedule(continuityA2, continuityB),
            CreateContinuitySchedule(continuityA1, continuityC),
            CreateContinuitySchedule(continuityA2, continuityC)
        ],
        continuityCharacters);
    Ensure(
        continuityResult.TotalReentryCount == 0 && continuityResult.TotalGapRaidCount == 0,
        "캐릭터 교체보다 참여 연속성을 우선해야 하는 구성에서 중간 재참여가 발생했습니다.");
}

static void TestCharacterReuseOptimization(DateTime weekStart)
{
    var firstRaid = new RaidContent
    {
        Name = "재사용 테스트 1",
        Difficulty = "테스트",
        PlayerCount = 4,
        PartySize = 4,
        GateCount = 1,
        Priority = 20
    };
    var secondRaid = new RaidContent
    {
        Name = "재사용 테스트 2",
        Difficulty = "테스트",
        PlayerCount = 4,
        PartySize = 4,
        GateCount = 1,
        Priority = 10
    };
    var people = Enumerable.Range(0, 4)
        .Select(index => new Person { Name = $"재사용 참여자 {index + 1}" })
        .ToArray();
    var reusableCharacters = people
        .Select((owner, index) => new GameCharacter
        {
            OwnerId = owner.Id,
            Name = $"재사용 캐릭터 {index + 1}",
            ClassName = $"재사용 직업 {index + 1}",
            Role = index == 0 ? CharacterRole.Supporter : CharacterRole.Dealer,
            CombatPower = 10_000
        })
        .ToArray();
    var alternativeCharacter = new GameCharacter
    {
        OwnerId = people[1].Id,
        Name = "교체 후보 캐릭터",
        ClassName = "재사용 직업 2",
        Role = CharacterRole.Dealer,
        CombatPower = 20_000
    };
    var allCharacters = reusableCharacters.Append(alternativeCharacter).ToArray();
    var windows = people.Select(owner => new AvailabilityWindow
    {
        PersonId = owner.Id,
        Day = DayOfWeek.Saturday,
        StartMinute = 19 * 60,
        EndMinute = 23 * 60
    });
    var selections = reusableCharacters.SelectMany(character => new[]
    {
        new WeeklyRaidSelection
        {
            WeekStartLocal = weekStart,
            CharacterId = character.Id,
            RaidContentId = firstRaid.Id
        },
        new WeeklyRaidSelection
        {
            WeekStartLocal = weekStart,
            CharacterId = character.Id,
            RaidContentId = secondRaid.Id
        }
    }).Append(new WeeklyRaidSelection
    {
        WeekStartLocal = weekStart,
        CharacterId = alternativeCharacter.Id,
        RaidContentId = secondRaid.Id
    });

    var result = new RaidPartyCompositionService().Compose(
        weekStart,
        people,
        allCharacters,
        windows,
        selections,
        [firstRaid, secondRaid],
        [],
        [],
        new PartyCompositionSettings { CharacterSwitchPenalty = 25 });

    Ensure(result.Teams.Count == 3, "서포터 없는 잔여 캐릭터까지 공격대로 편성되지 않았습니다.");
    var reusedSecondTeam = result.Teams
        .Where(team => team.Raid.Id == secondRaid.Id)
        .Single(team => team.Parties
            .SelectMany(party => party.Members)
            .Any(character => character.Id == reusableCharacters[1].Id));
    var secondTeamCharacterIds = reusedSecondTeam
        .Parties
        .SelectMany(party => party.Members)
        .Select(character => character.Id)
        .ToHashSet();
    Ensure(secondTeamCharacterIds.Contains(reusableCharacters[1].Id), "이전 공격대의 캐릭터가 재사용되지 않았습니다.");
    Ensure(!secondTeamCharacterIds.Contains(alternativeCharacter.Id), "같은 참여자의 두 캐릭터가 한 공격대에 중복 편성되었습니다.");
    Ensure(
        result.Teams
            .Where(team => team.Raid.Id == secondRaid.Id)
            .SelectMany(team => team.Parties)
            .SelectMany(party => party.Members)
            .Any(character => character.Id == alternativeCharacter.Id),
        "서포터 없는 잔여 캐릭터가 별도 공격대로 편성되지 않았습니다.");
}

static void TestScheduleReferenceProtection(DateTime weekStart)
{
    var characterId = Guid.NewGuid();
    var raidId = Guid.NewGuid();
    var confirmed = new RaidTeamSchedule
    {
        WeekStartLocal = weekStart,
        RaidContentId = raidId,
        IsConfirmed = true,
        Members = [new RaidTeamMember { CharacterId = characterId, PartyNumber = 1 }]
    };
    var draft = new RaidTeamSchedule
    {
        WeekStartLocal = weekStart,
        RaidContentId = raidId,
        Members = [new RaidTeamMember { CharacterId = characterId, PartyNumber = 1 }]
    };
    var schedules = new List<RaidTeamSchedule> { confirmed, draft };
    var service = new ScheduleReferenceService();

    Ensure(service.HasConfirmedCharacterReference(schedules, [characterId]), "확정 편성의 캐릭터 참조를 찾지 못했습니다.");
    Ensure(service.HasConfirmedRaidReference(schedules, raidId), "확정 편성의 레이드 참조를 찾지 못했습니다.");
    Ensure(service.RemoveDraftsContainingCharacters(schedules, [characterId]) == 1, "연결된 초안 편성을 제거하지 못했습니다.");
    Ensure(schedules.Count == 1 && schedules[0].IsConfirmed, "초안 정리 중 확정 편성이 변경되었습니다.");
    Ensure(service.RemoveDraftsForRaid(schedules, raidId) == 0, "확정 편성이 레이드 초안 정리에서 제거되었습니다.");
}

static async Task TestLostArkApiClientAsync()
{
    var handler = new FakeLostArkApiHandler();
    var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://developer-lostark.game.onstove.com/")
    };
    var client = new LostArkArmoryClient(httpClient, () => "bearer test-token");

    var profile = await client.GetCharacterProfileAsync("테스트캐릭터");
    var cachedProfile = await client.GetCharacterProfileAsync("테스트캐릭터");

    Ensure(profile.CharacterName == "테스트캐릭터", "API 캐릭터명 파싱에 실패했습니다.");
    Ensure(profile.ClassName == "배틀마스터", "API 클래스 파싱에 실패했습니다.");
    Ensure(profile.ItemLevel == 1730.5m, "API 아이템 레벨 파싱에 실패했습니다.");
    Ensure(cachedProfile == profile && handler.RequestCount == 1, "API 프로필 캐시가 적용되지 않았습니다.");
    Ensure(handler.AuthorizationScheme == "bearer" && handler.AuthorizationToken == "test-token", "API 인증 헤더가 올바르지 않습니다.");
}

sealed class FakeLostArkApiHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public string? AuthorizationScheme { get; private set; }
    public string? AuthorizationToken { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        AuthorizationScheme = request.Headers.Authorization?.Scheme;
        AuthorizationToken = request.Headers.Authorization?.Parameter;
        const string json = """
            {
              "ServerName": "루페온",
              "CharacterName": "테스트캐릭터",
              "CharacterClassName": "배틀마스터",
              "ItemAvgLevel": "1,730.50"
            }
            """;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
