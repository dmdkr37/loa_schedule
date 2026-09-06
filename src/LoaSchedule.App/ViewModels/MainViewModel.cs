using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Net.Http;
using LoaSchedule.Domain.Models;
using LoaSchedule.Domain.Services;
using LoaSchedule.Infrastructure.LostArkApi;
using LoaSchedule.Infrastructure.Persistence;

namespace LoaSchedule.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<DayOfWeek, string> KoreanDayNames =
        new Dictionary<DayOfWeek, string>
        {
            [DayOfWeek.Monday] = "월요일",
            [DayOfWeek.Tuesday] = "화요일",
            [DayOfWeek.Wednesday] = "수요일",
            [DayOfWeek.Thursday] = "목요일",
            [DayOfWeek.Friday] = "금요일",
            [DayOfWeek.Saturday] = "토요일",
            [DayOfWeek.Sunday] = "일요일"
        };

    private static readonly IReadOnlyDictionary<SynergyKind, string> KoreanSynergyKindNames =
        new Dictionary<SynergyKind, string>
        {
            [SynergyKind.DamageIncrease] = "피해 증가",
            [SynergyKind.AttackPowerIncrease] = "공격력 증가",
            [SynergyKind.DefenseReduction] = "방어력 감소",
            [SynergyKind.CriticalRateIncrease] = "치명타 적중률 증가",
            [SynergyKind.CriticalDamageIncrease] = "치명타 피해 증가",
            [SynergyKind.BackHeadDamageIncrease] = "백·헤드 피해 증가",
            [SynergyKind.AttackSpeedIncrease] = "공격 속도 증가",
            [SynergyKind.MoveSpeedIncrease] = "이동 속도 증가",
            [SynergyKind.ManaRecoveryIncrease] = "마나 회복 증가",
            [SynergyKind.DamageTakenReduction] = "받는 피해 감소"
        };

    private readonly JsonAppStateStore _store;
    private readonly RaidRecommendationService _raidRecommendationService = new();
    private readonly GameWeekService _gameWeekService = new();
    private readonly WeeklyRaidProgressService _weeklyRaidProgressService = new();
    private readonly RaidPartyCompositionService _partyCompositionService = new();
    private readonly RaidRunOrderService _raidRunOrderService = new();
    private readonly ScheduleReferenceService _scheduleReferenceService = new();
    private readonly LostArkArmoryClient _lostArkArmoryClient;
    private readonly HashSet<Guid> _selectedSwapMemberIds = [];
    private Guid? _editingPersonId;
    private Guid? _editingCharacterId;
    private Guid? _characterOwnerFilterId;
    private Guid? _editingSelectionId;
    private Guid? _replacementRaidChoiceId;
    private Guid? _editingRaidId;
    private Guid? _replacementMemberId;
    private Guid? _replacementCharacterId;
    private Guid? _replacementRaidContentId;
    private AppState _state = new();
    private DateTime _currentWeekStart;
    private string _newPersonName = string.Empty;
    private Guid? _newCharacterOwnerId;
    private string _newCharacterName = string.Empty;
    private string? _verifiedCharacterName;
    private string _characterLookupSummary = "캐릭터명을 입력하고 API 조회를 실행하세요.";
    private bool _isCharacterLookupInProgress;
    private bool _isApiTokenSaved;
    private string _newCharacterClass = string.Empty;
    private string _newCharacterBuild = string.Empty;
    private CharacterRole _newCharacterRole = CharacterRole.Dealer;
    private decimal _newCharacterItemLevel = 1700;
    private int _newCharacterCombatPower;
    private Guid? _newAvailabilityPersonId;
    private DayOption? _newAvailabilityDay;
    private TimeOption? _newAvailabilityStart;
    private TimeOption? _newAvailabilityEnd;
    private string _newSynergyClassName = string.Empty;
    private string _newSynergyBuildName = string.Empty;
    private SynergyKind _newSynergyKind = SynergyKind.DamageIncrease;
    private decimal _newSynergyValue;
    private string _newSynergyStackingGroup = string.Empty;
    private bool _newSynergyIsConditional;
    private string _newSynergyConditionDescription = string.Empty;
    private string _newExclusionClassName = string.Empty;
    private string _newExclusionBuildName = string.Empty;
    private SynergyKind _newExclusionKind = SynergyKind.DamageIncrease;
    private string _newExclusionReason = string.Empty;
    private string _autoPartySummary = "자동 편성을 실행하면 결과가 표시됩니다.";
    private string _autoPartyRunOrderSummary = "공격대를 생성하면 교체 최소 순서를 계산합니다.";
    private string _newRaidGroupKey = string.Empty;
    private string _newRaidCategory = string.Empty;
    private string _newRaidName = string.Empty;
    private string _newRaidDifficulty = "노말";
    private decimal _newRaidMinimumItemLevel = 1700;
    private int _newRaidPlayerCount = 8;
    private int _newRaidPartySize = 4;
    private int _newRaidGateCount = 2;
    private int _newRaidPriority;
    private string _statusMessage = "데이터를 불러오는 중입니다.";

    public MainViewModel(JsonAppStateStore store)
    {
        _store = store;
        _lostArkArmoryClient = new LostArkArmoryClient(() => _state.LostArkApiToken);

        WeekDays =
        [
            new(DayOfWeek.Wednesday, "수요일"),
            new(DayOfWeek.Thursday, "목요일"),
            new(DayOfWeek.Friday, "금요일"),
            new(DayOfWeek.Saturday, "토요일"),
            new(DayOfWeek.Sunday, "일요일"),
            new(DayOfWeek.Monday, "월요일"),
            new(DayOfWeek.Tuesday, "화요일")
        ];

        TimeOptions = BuildTimeOptions();
        _newAvailabilityDay = WeekDays[0];
        _newAvailabilityStart = TimeOptions.First(option => option.Minutes == 19 * 60);
        _newAvailabilityEnd = TimeOptions.First(option => option.Minutes == 23 * 60);

        AddPersonCommand = new RelayCommand(AddPerson, () => !string.IsNullOrWhiteSpace(NewPersonName));
        BeginEditPersonCommand = new RelayCommand<Person>(BeginEditPerson);
        CancelPersonEditCommand = new RelayCommand(CancelPersonEdit);
        TogglePersonActiveCommand = new RelayCommand<Person>(TogglePersonActive);
        DeletePersonCommand = new RelayCommand<Person>(DeletePerson);
        AddCharacterCommand = new RelayCommand(
            AddCharacter,
            () => NewCharacterOwnerId.HasValue
                  && !string.IsNullOrWhiteSpace(NewCharacterName)
                  && !string.IsNullOrWhiteSpace(NewCharacterClass)
                  && TextEquals(_verifiedCharacterName, NewCharacterName));
        LookupCharacterCommand = new RelayCommand(
            LookupCharacter,
            () => !string.IsNullOrWhiteSpace(NewCharacterName) && !IsCharacterLookupInProgress);
        SaveApiTokenCommand = new RelayCommand(SaveApiToken);
        BeginEditCharacterCommand = new RelayCommand<CharacterListItem>(BeginEditCharacter);
        CancelCharacterEditCommand = new RelayCommand(CancelCharacterEdit);
        ToggleCharacterActiveCommand = new RelayCommand<CharacterListItem>(ToggleCharacterActive);
        DeleteCharacterCommand = new RelayCommand<CharacterListItem>(DeleteCharacter);
        AddAvailabilityCommand = new RelayCommand(AddAvailability, CanAddAvailability);
        RemoveAvailabilityCommand = new RelayCommand<AvailabilityListItem>(RemoveAvailability);
        RefreshRecommendationsCommand = new RelayCommand(RefreshRaidRecommendations);
        ConfirmOwnerRecommendationsCommand = new RelayCommand<RaidRecommendationItem>(ConfirmOwnerRecommendations);
        AdvanceRaidGateCommand = new RelayCommand<WeeklyRaidPlanItem>(AdvanceRaidGate);
        ResetRaidProgressCommand = new RelayCommand<WeeklyRaidPlanItem>(ResetRaidProgress);
        RemoveWeeklyRaidCommand = new RelayCommand<WeeklyRaidPlanItem>(RemoveWeeklyRaid);
        AddSynergyCommand = new RelayCommand(AddSynergy, CanAddSynergy);
        RemoveSynergyCommand = new RelayCommand<SynergyListItem>(RemoveSynergy);
        AddSynergyExclusionCommand = new RelayCommand(AddSynergyExclusion, CanAddSynergyExclusion);
        RemoveSynergyExclusionCommand = new RelayCommand<SynergyExclusionListItem>(RemoveSynergyExclusion);
        SaveRaidCommand = new RelayCommand(SaveRaid, CanSaveRaid);
        BeginEditRaidCommand = new RelayCommand<RaidContent>(BeginEditRaid);
        CancelRaidEditCommand = new RelayCommand(CancelRaidEdit);
        ToggleRaidActiveCommand = new RelayCommand<RaidContent>(ToggleRaidActive);
        DeleteRaidCommand = new RelayCommand<RaidContent>(DeleteRaid);
        GeneratePartyCompositionCommand = new RelayCommand(GeneratePartyComposition);
        ToggleSwapMemberCommand = new RelayCommand<AutoPartyMemberItem>(ToggleSwapMember);
        SwapSelectedPartyMembersCommand = new RelayCommand(
            SwapSelectedPartyMembers,
            () => _selectedSwapMemberIds.Count == 2);
        ConfirmPartyCompositionCommand = new RelayCommand(ConfirmPartyComposition);
        UnlockPartyCompositionCommand = new RelayCommand(UnlockPartyComposition);
        SelectReplacementMemberCommand = new RelayCommand<AutoPartyMemberItem>(SelectReplacementMember);
        SelectUnassignedCharacterCommand = new RelayCommand<UnassignedCharacterItem>(SelectUnassignedCharacter);
        ReplacePartyMemberCommand = new RelayCommand(
            ReplacePartyMember,
            () => _replacementMemberId.HasValue && _replacementCharacterId.HasValue);
        FillVacancyCommand = new RelayCommand(
            FillVacancy,
            () => _replacementCharacterId.HasValue);
        ClearPartyCompositionCommand = new RelayCommand(ClearPartyComposition);
        BeginEditWeeklyRaidCommand = new RelayCommand<WeeklyRaidPlanItem>(BeginEditWeeklyRaid);
        CancelWeeklyRaidEditCommand = new RelayCommand(CancelWeeklyRaidEdit);
        ApplyWeeklyRaidChangeCommand = new RelayCommand(
            ApplyWeeklyRaidChange,
            () => _editingSelectionId.HasValue && _replacementRaidChoiceId.HasValue);
        AddWeeklyRaidCommand = new RelayCommand(
            AddWeeklyRaid,
            () => WeeklyRaidTargetCharacterId.HasValue && _replacementRaidChoiceId.HasValue);

        CharactersView = CollectionViewSource.GetDefaultView(Characters);
        CharactersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CharacterListItem.OwnerName)));

        RaidRecommendationsView = CollectionViewSource.GetDefaultView(RaidRecommendations);
        RaidRecommendationsView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(RaidRecommendationItem.OwnerName)));

        WeeklyRaidPlanView = CollectionViewSource.GetDefaultView(WeeklyRaidPlan);
        WeeklyRaidPlanView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(WeeklyRaidPlanItem.OwnerName)));
    }

    public ObservableCollection<Person> People { get; } = [];
    public ObservableCollection<CharacterListItem> Characters { get; } = [];
    public ObservableCollection<CharacterOwnerFilterOption> CharacterOwnerFilters { get; } = [];
    public ObservableCollection<RaidChoiceOption> RaidChoices { get; } = [];

    // 소유자별 그룹 헤더를 보여주기 위한 뷰입니다.
    public ICollectionView CharactersView { get; }
    public ICollectionView RaidRecommendationsView { get; }
    public ICollectionView WeeklyRaidPlanView { get; }
    public ObservableCollection<AvailabilityListItem> AvailabilityWindows { get; } = [];
    public ObservableCollection<RaidContent> Raids { get; } = [];
    public ObservableCollection<RaidRecommendationItem> RaidRecommendations { get; } = [];
    public ObservableCollection<WeeklyRaidPlanItem> WeeklyRaidPlan { get; } = [];
    public ObservableCollection<SynergyListItem> Synergies { get; } = [];
    public ObservableCollection<SynergyExclusionListItem> SynergyExclusions { get; } = [];
    public ObservableCollection<AutoPartyPlanItem> AutoPartyPlans { get; } = [];
    public ObservableCollection<AutoPartyTeamCardItem> AutoPartyTeamCards { get; } = [];
    public ObservableCollection<AutoPartyRunOrderItem> AutoPartyRunOrder { get; } = [];
    public ObservableCollection<AutoPartyIssueItem> AutoPartyIssues { get; } = [];
    public ObservableCollection<AutoPartyMemberItem> AutoPartyMembers { get; } = [];
    public ObservableCollection<UnassignedCharacterItem> UnassignedCharacters { get; } = [];
    public IReadOnlyList<CharacterRole> CharacterRoles { get; } = Enum.GetValues<CharacterRole>();
    public IReadOnlyList<SynergyKindOption> SynergyKinds { get; } = Enum
        .GetValues<SynergyKind>()
        .Select(kind => new SynergyKindOption(kind, KoreanSynergyKindNames[kind]))
        .ToArray();
    public IReadOnlyList<DayOption> WeekDays { get; }
    public IReadOnlyList<TimeOption> TimeOptions { get; }

    public RelayCommand AddPersonCommand { get; }
    public RelayCommand<Person> BeginEditPersonCommand { get; }
    public RelayCommand CancelPersonEditCommand { get; }
    public RelayCommand<Person> TogglePersonActiveCommand { get; }
    public RelayCommand<Person> DeletePersonCommand { get; }
    public RelayCommand AddCharacterCommand { get; }
    public RelayCommand LookupCharacterCommand { get; }
    public RelayCommand SaveApiTokenCommand { get; }
    public RelayCommand<CharacterListItem> BeginEditCharacterCommand { get; }
    public RelayCommand CancelCharacterEditCommand { get; }
    public RelayCommand<CharacterListItem> ToggleCharacterActiveCommand { get; }
    public RelayCommand<CharacterListItem> DeleteCharacterCommand { get; }
    public RelayCommand AddAvailabilityCommand { get; }
    public RelayCommand<AvailabilityListItem> RemoveAvailabilityCommand { get; }
    public RelayCommand RefreshRecommendationsCommand { get; }
    public RelayCommand<RaidRecommendationItem> ConfirmOwnerRecommendationsCommand { get; }
    public RelayCommand<WeeklyRaidPlanItem> AdvanceRaidGateCommand { get; }
    public RelayCommand<WeeklyRaidPlanItem> ResetRaidProgressCommand { get; }
    public RelayCommand<WeeklyRaidPlanItem> RemoveWeeklyRaidCommand { get; }
    public RelayCommand AddSynergyCommand { get; }
    public RelayCommand<SynergyListItem> RemoveSynergyCommand { get; }
    public RelayCommand AddSynergyExclusionCommand { get; }
    public RelayCommand<SynergyExclusionListItem> RemoveSynergyExclusionCommand { get; }
    public RelayCommand SaveRaidCommand { get; }
    public RelayCommand<RaidContent> BeginEditRaidCommand { get; }
    public RelayCommand CancelRaidEditCommand { get; }
    public RelayCommand<RaidContent> ToggleRaidActiveCommand { get; }
    public RelayCommand<RaidContent> DeleteRaidCommand { get; }
    public RelayCommand GeneratePartyCompositionCommand { get; }
    public RelayCommand<AutoPartyMemberItem> ToggleSwapMemberCommand { get; }
    public RelayCommand SwapSelectedPartyMembersCommand { get; }
    public RelayCommand ConfirmPartyCompositionCommand { get; }
    public RelayCommand UnlockPartyCompositionCommand { get; }
    public RelayCommand<AutoPartyMemberItem> SelectReplacementMemberCommand { get; }
    public RelayCommand<UnassignedCharacterItem> SelectUnassignedCharacterCommand { get; }
    public RelayCommand ReplacePartyMemberCommand { get; }
    public RelayCommand FillVacancyCommand { get; }
    public RelayCommand ClearPartyCompositionCommand { get; }
    public RelayCommand<WeeklyRaidPlanItem> BeginEditWeeklyRaidCommand { get; }
    public RelayCommand CancelWeeklyRaidEditCommand { get; }
    public RelayCommand ApplyWeeklyRaidChangeCommand { get; }
    public RelayCommand AddWeeklyRaidCommand { get; }

    public string NewPersonName
    {
        get => _newPersonName;
        set
        {
            if (SetProperty(ref _newPersonName, value))
            {
                AddPersonCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PersonSubmitText => _editingPersonId.HasValue ? "참여자 수정 저장" : "참여자 추가";

    public Guid? NewCharacterOwnerId
    {
        get => _newCharacterOwnerId;
        set
        {
            if (SetProperty(ref _newCharacterOwnerId, value))
            {
                AddCharacterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewCharacterName
    {
        get => _newCharacterName;
        set
        {
            if (SetProperty(ref _newCharacterName, value))
            {
                if (!TextEquals(_verifiedCharacterName, value))
                {
                    _verifiedCharacterName = null;
                    NewCharacterClass = string.Empty;
                    NewCharacterItemLevel = 0;
                    CharacterLookupSummary = "캐릭터명이 변경되었습니다. API 조회를 다시 실행하세요.";
                }

                AddCharacterCommand.RaiseCanExecuteChanged();
                LookupCharacterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LostArkApiToken
    {
        get => _state.LostArkApiToken ?? string.Empty;
        set
        {
            if (_state.LostArkApiToken != value)
            {
                _state.LostArkApiToken = value;
                _isApiTokenSaved = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LostArkApiTokenStatus));
            }
        }
    }

    public string LostArkApiTokenStatus => string.IsNullOrWhiteSpace(_state.LostArkApiToken)
        ? "API 키 미설정"
        : _isApiTokenSaved ? "API 키 저장됨" : "저장 필요";

    public string CharacterLookupSummary
    {
        get => _characterLookupSummary;
        private set => SetProperty(ref _characterLookupSummary, value);
    }

    public bool IsCharacterLookupInProgress
    {
        get => _isCharacterLookupInProgress;
        private set
        {
            if (SetProperty(ref _isCharacterLookupInProgress, value))
            {
                LookupCharacterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewCharacterClass
    {
        get => _newCharacterClass;
        set
        {
            if (SetProperty(ref _newCharacterClass, value))
            {
                AddCharacterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewCharacterBuild
    {
        get => _newCharacterBuild;
        set => SetProperty(ref _newCharacterBuild, value);
    }

    public CharacterRole NewCharacterRole
    {
        get => _newCharacterRole;
        set => SetProperty(ref _newCharacterRole, value);
    }

    public decimal NewCharacterItemLevel
    {
        get => _newCharacterItemLevel;
        set => SetProperty(ref _newCharacterItemLevel, value);
    }

    public int NewCharacterCombatPower
    {
        get => _newCharacterCombatPower;
        set => SetProperty(ref _newCharacterCombatPower, value);
    }

    public string CharacterSubmitText => _editingCharacterId.HasValue ? "캐릭터 수정 저장" : "캐릭터 추가";

    public Guid? NewAvailabilityPersonId
    {
        get => _newAvailabilityPersonId;
        set
        {
            if (SetProperty(ref _newAvailabilityPersonId, value))
            {
                AddAvailabilityCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public DayOption? NewAvailabilityDay
    {
        get => _newAvailabilityDay;
        set
        {
            if (SetProperty(ref _newAvailabilityDay, value))
            {
                AddAvailabilityCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public TimeOption? NewAvailabilityStart
    {
        get => _newAvailabilityStart;
        set
        {
            if (SetProperty(ref _newAvailabilityStart, value))
            {
                AddAvailabilityCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public TimeOption? NewAvailabilityEnd
    {
        get => _newAvailabilityEnd;
        set
        {
            if (SetProperty(ref _newAvailabilityEnd, value))
            {
                AddAvailabilityCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewSynergyClassName
    {
        get => _newSynergyClassName;
        set
        {
            if (SetProperty(ref _newSynergyClassName, value))
            {
                AddSynergyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewSynergyBuildName
    {
        get => _newSynergyBuildName;
        set => SetProperty(ref _newSynergyBuildName, value);
    }

    public SynergyKind NewSynergyKind
    {
        get => _newSynergyKind;
        set => SetProperty(ref _newSynergyKind, value);
    }

    public decimal NewSynergyValue
    {
        get => _newSynergyValue;
        set
        {
            if (SetProperty(ref _newSynergyValue, value))
            {
                AddSynergyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewSynergyStackingGroup
    {
        get => _newSynergyStackingGroup;
        set => SetProperty(ref _newSynergyStackingGroup, value);
    }

    public bool NewSynergyIsConditional
    {
        get => _newSynergyIsConditional;
        set => SetProperty(ref _newSynergyIsConditional, value);
    }

    public string NewSynergyConditionDescription
    {
        get => _newSynergyConditionDescription;
        set => SetProperty(ref _newSynergyConditionDescription, value);
    }

    public string NewExclusionClassName
    {
        get => _newExclusionClassName;
        set
        {
            if (SetProperty(ref _newExclusionClassName, value))
            {
                AddSynergyExclusionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewExclusionBuildName
    {
        get => _newExclusionBuildName;
        set => SetProperty(ref _newExclusionBuildName, value);
    }

    public SynergyKind NewExclusionKind
    {
        get => _newExclusionKind;
        set => SetProperty(ref _newExclusionKind, value);
    }

    public string NewExclusionReason
    {
        get => _newExclusionReason;
        set => SetProperty(ref _newExclusionReason, value);
    }

    public string NewRaidGroupKey
    {
        get => _newRaidGroupKey;
        set
        {
            if (SetProperty(ref _newRaidGroupKey, value))
            {
                SaveRaidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewRaidCategory
    {
        get => _newRaidCategory;
        set => SetProperty(ref _newRaidCategory, value);
    }

    public string NewRaidName
    {
        get => _newRaidName;
        set
        {
            if (SetProperty(ref _newRaidName, value))
            {
                SaveRaidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewRaidDifficulty
    {
        get => _newRaidDifficulty;
        set => SetProperty(ref _newRaidDifficulty, value);
    }

    public decimal NewRaidMinimumItemLevel
    {
        get => _newRaidMinimumItemLevel;
        set => SetProperty(ref _newRaidMinimumItemLevel, value);
    }

    public int NewRaidPlayerCount
    {
        get => _newRaidPlayerCount;
        set
        {
            if (SetProperty(ref _newRaidPlayerCount, value))
            {
                SaveRaidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int NewRaidPartySize
    {
        get => _newRaidPartySize;
        set
        {
            if (SetProperty(ref _newRaidPartySize, value))
            {
                SaveRaidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int NewRaidGateCount
    {
        get => _newRaidGateCount;
        set
        {
            if (SetProperty(ref _newRaidGateCount, value))
            {
                SaveRaidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int NewRaidPriority
    {
        get => _newRaidPriority;
        set => SetProperty(ref _newRaidPriority, value);
    }

    public string RaidSubmitText => _editingRaidId.HasValue ? "레이드 수정 저장" : "레이드 추가";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string AutoPartySummary
    {
        get => _autoPartySummary;
        private set => SetProperty(ref _autoPartySummary, value);
    }

    public string AutoPartyRunOrderSummary
    {
        get => _autoPartyRunOrderSummary;
        private set => SetProperty(ref _autoPartyRunOrderSummary, value);
    }

    public decimal SynergyWeight
    {
        get => _state.PartyCompositionSettings.SynergyWeight;
        set
        {
            if (_state.PartyCompositionSettings.SynergyWeight != value)
            {
                _state.PartyCompositionSettings.SynergyWeight = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal CombatPowerBalanceWeight
    {
        get => _state.PartyCompositionSettings.CombatPowerBalanceWeight;
        set
        {
            if (_state.PartyCompositionSettings.CombatPowerBalanceWeight != value)
            {
                _state.PartyCompositionSettings.CombatPowerBalanceWeight = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal AvailabilityWeight
    {
        get => _state.PartyCompositionSettings.AvailabilityWeight;
        set
        {
            if (_state.PartyCompositionSettings.AvailabilityWeight != value)
            {
                _state.PartyCompositionSettings.AvailabilityWeight = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal CharacterSwitchPenalty
    {
        get => _state.PartyCompositionSettings.CharacterSwitchPenalty;
        set
        {
            if (_state.PartyCompositionSettings.CharacterSwitchPenalty != value)
            {
                _state.PartyCompositionSettings.CharacterSwitchPenalty = value;
                OnPropertyChanged();
            }
        }
    }

    public int MinimumCommonMinutes
    {
        get => _state.PartyCompositionSettings.MinimumCommonMinutes;
        set
        {
            var normalized = Math.Max(30, value);
            if (_state.PartyCompositionSettings.MinimumCommonMinutes != normalized)
            {
                _state.PartyCompositionSettings.MinimumCommonMinutes = normalized;
                OnPropertyChanged();
            }
        }
    }

    public int ActivePeopleCount => People.Count(person => person.IsActive);
    public int ActiveCharacterCount => _state.Characters.Count(character => character.IsActive);
    public int ActiveRaidCount => Raids.Count(raid => raid.IsActive);
    public string CurrentWeekLabel =>
        $"{_currentWeekStart:yyyy.MM.dd HH:mm} ~ {_currentWeekStart.AddDays(7):yyyy.MM.dd HH:mm}";

    public async Task InitializeAsync()
    {
        try
        {
            _state = await _store.LoadAsync();
            _state.SchemaVersion = 5;
            _isApiTokenSaved = !string.IsNullOrWhiteSpace(_state.LostArkApiToken);
            _currentWeekStart = _gameWeekService.GetWeekStart(DateTime.Now);
            RefreshCollections();
            OnPropertyChanged(nameof(LostArkApiToken));
            OnPropertyChanged(nameof(LostArkApiTokenStatus));
            await _store.SaveAsync(_state);
            StatusMessage = $"로컬 데이터: {_store.FilePath}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"데이터를 불러오지 못했습니다: {exception.Message}";
        }
    }

    private async void AddPerson()
    {
        var name = NewPersonName.Trim();
        if (_state.People.Any(person => person.Id != _editingPersonId && TextEquals(person.Name, name)))
        {
            StatusMessage = "같은 이름의 참여자가 이미 있습니다.";
            return;
        }

        var person = _editingPersonId.HasValue
            ? _state.People.FirstOrDefault(candidate => candidate.Id == _editingPersonId.Value)
            : null;
        var message = person is null ? "참여자를 추가했습니다." : "참여자 이름을 수정했습니다.";
        if (person is null)
        {
            person = new Person { Name = name };
            _state.People.Add(person);
        }
        else
        {
            person.Name = name;
        }

        _editingPersonId = null;
        NewPersonName = string.Empty;
        OnPropertyChanged(nameof(PersonSubmitText));
        RefreshCollections();
        NewCharacterOwnerId ??= person.Id;
        NewAvailabilityPersonId ??= person.Id;
        await SaveAndReportAsync(message);
    }

    private void BeginEditPerson(Person person)
    {
        _editingPersonId = person.Id;
        NewPersonName = person.Name;
        OnPropertyChanged(nameof(PersonSubmitText));
        StatusMessage = $"{person.Name} 참여자를 수정 중입니다.";
    }

    private void CancelPersonEdit()
    {
        _editingPersonId = null;
        NewPersonName = string.Empty;
        OnPropertyChanged(nameof(PersonSubmitText));
        StatusMessage = "참여자 수정을 취소했습니다.";
    }

    private async void TogglePersonActive(Person person)
    {
        var ownedCharacterIds = _state.Characters
            .Where(character => character.OwnerId == person.Id)
            .Select(character => character.Id)
            .ToHashSet();
        if (person.IsActive && HasConfirmedScheduleForCharacters(ownedCharacterIds))
        {
            StatusMessage = "확정 편성에 포함된 참여자는 비활성화할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        person.IsActive = !person.IsActive;
        if (!person.IsActive)
        {
            RemoveDraftSchedulesContainingCharacters(ownedCharacterIds);
        }

        RefreshCollections();
        await SaveAndReportAsync($"{person.Name} 참여자를 {(person.IsActive ? "활성화" : "비활성화")}했습니다.");
    }

    private async void DeletePerson(Person person)
    {
        var ownedCharacters = _state.Characters.Where(character => character.OwnerId == person.Id).ToArray();
        var ownedCharacterIds = ownedCharacters.Select(character => character.Id).ToHashSet();
        if (HasConfirmedScheduleForCharacters(ownedCharacterIds))
        {
            StatusMessage = "확정 편성에 포함된 참여자는 삭제할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        RemoveDraftSchedulesContainingCharacters(ownedCharacterIds);
        _state.WeeklyRaidSelections.RemoveAll(selection => ownedCharacterIds.Contains(selection.CharacterId));
        _state.AvailabilityWindows.RemoveAll(window => window.PersonId == person.Id);
        _state.Characters.RemoveAll(character => character.OwnerId == person.Id);
        _state.People.Remove(person);
        if (_editingPersonId == person.Id)
        {
            CancelPersonEdit();
        }

        RefreshCollections();
        await SaveAndReportAsync($"{person.Name} 참여자와 연결된 캐릭터·가능 시간을 삭제했습니다.");
    }

    private async void AddCharacter()
    {
        if (!NewCharacterOwnerId.HasValue)
        {
            return;
        }

        if (!TextEquals(_verifiedCharacterName, NewCharacterName))
        {
            StatusMessage = "캐릭터명을 API로 조회한 후 추가하세요.";
            return;
        }

        var character = _editingCharacterId.HasValue
            ? _state.Characters.FirstOrDefault(candidate => candidate.Id == _editingCharacterId.Value)
            : null;
        if (character is not null && HasConfirmedScheduleForCharacters([character.Id]))
        {
            StatusMessage = "확정 편성에 포함된 캐릭터는 수정할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        var message = character is null ? "캐릭터를 추가했습니다." : "캐릭터 정보를 수정했습니다.";
        if (character is null)
        {
            character = new GameCharacter();
            _state.Characters.Add(character);
        }

        character.OwnerId = NewCharacterOwnerId.Value;
        character.Name = NewCharacterName.Trim();
        character.ClassName = NewCharacterClass.Trim();
        character.BuildName = NewCharacterBuild.Trim();
        character.Role = NewCharacterRole;
        character.ItemLevel = NewCharacterItemLevel;
        character.CombatPower = NewCharacterCombatPower;

        _editingCharacterId = null;
        _verifiedCharacterName = null;
        NewCharacterName = string.Empty;
        NewCharacterClass = string.Empty;
        NewCharacterBuild = string.Empty;
        NewCharacterCombatPower = 0;
        CharacterLookupSummary = "캐릭터명을 입력하고 API 조회를 실행하세요.";
        OnPropertyChanged(nameof(CharacterSubmitText));
        RefreshCharacterOwnerFilters();
        RefreshRaidChoices();
        RefreshCharacterRows();
        RefreshRaidRecommendations();
        RefreshAutoPartyRows();
        await SaveAndReportAsync(message);
        OnPropertyChanged(nameof(ActiveCharacterCount));
    }

    private async void SaveApiToken()
    {
        _state.LostArkApiToken = (_state.LostArkApiToken ?? string.Empty).Trim();
        _isApiTokenSaved = !string.IsNullOrWhiteSpace(_state.LostArkApiToken);
        _lostArkArmoryClient.ClearCache();
        OnPropertyChanged(nameof(LostArkApiToken));
        OnPropertyChanged(nameof(LostArkApiTokenStatus));
        await SaveAndReportAsync(string.IsNullOrWhiteSpace(_state.LostArkApiToken)
            ? "Lost Ark API 키를 삭제했습니다."
            : "Lost Ark API 키를 로컬 JSON에 저장했습니다.");
    }

    private async void LookupCharacter()
    {
        if (string.IsNullOrWhiteSpace(NewCharacterName))
        {
            return;
        }

        IsCharacterLookupInProgress = true;
        CharacterLookupSummary = "Lost Ark API에서 캐릭터 프로필을 조회하는 중입니다...";
        try
        {
            var profile = await _lostArkArmoryClient.GetCharacterProfileAsync(NewCharacterName);
            _verifiedCharacterName = profile.CharacterName;
            NewCharacterName = profile.CharacterName;
            NewCharacterClass = profile.ClassName;
            NewCharacterBuild = string.Empty;
            NewCharacterItemLevel = profile.ItemLevel;
            CharacterLookupSummary = $"{profile.ServerName} · {profile.ClassName} · 아이템 레벨 {profile.ItemLevel:N2}";
            StatusMessage = $"{profile.CharacterName} 캐릭터 정보를 불러왔습니다.";
            AddCharacterCommand.RaiseCanExecuteChanged();
        }
        catch (LostArkApiException exception)
        {
            _verifiedCharacterName = null;
            NewCharacterClass = string.Empty;
            NewCharacterItemLevel = 0;
            CharacterLookupSummary = exception.Message;
            StatusMessage = exception.Message;
        }
        catch (TaskCanceledException)
        {
            CharacterLookupSummary = "API 응답 시간이 초과되었습니다. 잠시 후 다시 시도하세요.";
            StatusMessage = CharacterLookupSummary;
        }
        catch (HttpRequestException exception)
        {
            CharacterLookupSummary = $"API에 연결하지 못했습니다: {exception.Message}";
            StatusMessage = CharacterLookupSummary;
        }
        finally
        {
            IsCharacterLookupInProgress = false;
        }
    }

    private void BeginEditCharacter(CharacterListItem item)
    {
        var character = _state.Characters.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (character is null)
        {
            return;
        }

        _editingCharacterId = character.Id;
        _verifiedCharacterName = character.Name;
        NewCharacterOwnerId = character.OwnerId;
        NewCharacterName = character.Name;
        NewCharacterClass = character.ClassName;
        NewCharacterBuild = character.BuildName;
        NewCharacterRole = character.Role;
        NewCharacterItemLevel = character.ItemLevel;
        NewCharacterCombatPower = character.CombatPower;
        CharacterLookupSummary = $"저장된 정보 · {character.ClassName} · 아이템 레벨 {character.ItemLevel:N2}";
        OnPropertyChanged(nameof(CharacterSubmitText));
        StatusMessage = $"{character.Name} 캐릭터를 수정 중입니다.";
    }

    private void CancelCharacterEdit()
    {
        _editingCharacterId = null;
        _verifiedCharacterName = null;
        NewCharacterName = string.Empty;
        NewCharacterClass = string.Empty;
        NewCharacterBuild = string.Empty;
        NewCharacterCombatPower = 0;
        NewCharacterItemLevel = 0;
        CharacterLookupSummary = "캐릭터명을 입력하고 API 조회를 실행하세요.";
        OnPropertyChanged(nameof(CharacterSubmitText));
        StatusMessage = "캐릭터 수정을 취소했습니다.";
    }

    private async void ToggleCharacterActive(CharacterListItem item)
    {
        var character = _state.Characters.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (character is null)
        {
            return;
        }

        if (character.IsActive && HasConfirmedScheduleForCharacters([character.Id]))
        {
            StatusMessage = "확정 편성에 포함된 캐릭터는 비활성화할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        character.IsActive = !character.IsActive;
        if (!character.IsActive)
        {
            RemoveDraftSchedulesContainingCharacters([character.Id]);
        }

        RefreshCollections();
        await SaveAndReportAsync($"{character.Name} 캐릭터를 {(character.IsActive ? "활성화" : "비활성화")}했습니다.");
    }

    private async void DeleteCharacter(CharacterListItem item)
    {
        var character = _state.Characters.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (character is null)
        {
            return;
        }

        if (HasConfirmedScheduleForCharacters([character.Id]))
        {
            StatusMessage = "확정 편성에 포함된 캐릭터는 삭제할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        RemoveDraftSchedulesContainingCharacters([character.Id]);
        _state.WeeklyRaidSelections.RemoveAll(selection => selection.CharacterId == character.Id);
        _state.Characters.Remove(character);
        if (_editingCharacterId == character.Id)
        {
            CancelCharacterEdit();
        }

        RefreshCollections();
        await SaveAndReportAsync($"{character.Name} 캐릭터와 주간 선택 기록을 삭제했습니다.");
    }

    private bool CanAddAvailability() =>
        NewAvailabilityPersonId.HasValue
        && NewAvailabilityDay is not null
        && NewAvailabilityStart is not null
        && NewAvailabilityEnd is not null
        && NewAvailabilityEnd.Minutes > NewAvailabilityStart.Minutes;

    private async void AddAvailability()
    {
        if (!CanAddAvailability())
        {
            StatusMessage = "종료 시간은 시작 시간보다 늦어야 합니다.";
            return;
        }

        var duplicateExists = _state.AvailabilityWindows.Any(window =>
            window.PersonId == NewAvailabilityPersonId
            && window.Day == NewAvailabilityDay!.Value
            && window.StartMinute == NewAvailabilityStart!.Minutes
            && window.EndMinute == NewAvailabilityEnd!.Minutes);

        if (duplicateExists)
        {
            StatusMessage = "동일한 참여 가능 시간이 이미 등록되어 있습니다.";
            return;
        }

        _state.AvailabilityWindows.Add(new AvailabilityWindow
        {
            PersonId = NewAvailabilityPersonId!.Value,
            Day = NewAvailabilityDay!.Value,
            StartMinute = NewAvailabilityStart!.Minutes,
            EndMinute = NewAvailabilityEnd!.Minutes
        });

        RefreshAvailabilityRows();
        await SaveAndReportAsync("참여 가능 시간을 추가했습니다.");
    }

    private async void RemoveAvailability(AvailabilityListItem item)
    {
        var window = _state.AvailabilityWindows.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (window is null)
        {
            return;
        }

        _state.AvailabilityWindows.Remove(window);
        var ownerCharacterIds = _state.Characters
            .Where(character => character.OwnerId == window.PersonId)
            .Select(character => character.Id)
            .ToHashSet();
        var invalidConfirmedSchedule = _state.RaidTeamSchedules
            .Where(schedule => schedule.IsConfirmed && schedule.WeekStartLocal == _currentWeekStart)
            .Where(schedule => schedule.Members.Any(member => ownerCharacterIds.Contains(member.CharacterId)))
            .Any(schedule =>
            {
                // 시간은 편성 조건이 아니므로 확정 편성을 막지 않습니다.
                _ = schedule;
                return false;
            });
        if (invalidConfirmedSchedule)
        {
            _state.AvailabilityWindows.Add(window);
            StatusMessage = "이 시간은 삭제할 수 없습니다.";
            return;
        }

        RemoveDraftSchedulesContainingCharacters(ownerCharacterIds);
        RefreshAvailabilityRows();
        RefreshAutoPartyRows();
        await SaveAndReportAsync("참여 가능 시간을 삭제했습니다.");
    }

    private bool CanAddSynergy() =>
        !string.IsNullOrWhiteSpace(NewSynergyClassName)
        && NewSynergyValue > 0;

    private async void AddSynergy()
    {
        if (!CanAddSynergy())
        {
            return;
        }

        var duplicateExists = _state.Synergies.Any(synergy =>
            TextEquals(synergy.ClassName, NewSynergyClassName)
            && TextEquals(synergy.RequiredBuildName, NewSynergyBuildName)
            && synergy.Kind == NewSynergyKind
            && TextEquals(synergy.StackingGroup, NewSynergyStackingGroup));

        if (duplicateExists)
        {
            StatusMessage = "같은 직업·세팅·효과·중첩 그룹의 시너지가 이미 등록되어 있습니다.";
            return;
        }

        _state.Synergies.Add(new SynergyDefinition
        {
            ClassName = NewSynergyClassName.Trim(),
            RequiredBuildName = NullIfWhiteSpace(NewSynergyBuildName),
            Kind = NewSynergyKind,
            Value = NewSynergyValue,
            StackingGroup = NewSynergyStackingGroup.Trim(),
            IsConditional = NewSynergyIsConditional,
            ConditionDescription = NewSynergyIsConditional
                ? NullIfWhiteSpace(NewSynergyConditionDescription)
                : null
        });

        NewSynergyClassName = string.Empty;
        NewSynergyBuildName = string.Empty;
        NewSynergyValue = 0;
        NewSynergyStackingGroup = string.Empty;
        NewSynergyIsConditional = false;
        NewSynergyConditionDescription = string.Empty;
        RefreshSynergyRows();
        await SaveAndReportAsync("시너지 제공 규칙을 추가했습니다.");
    }

    private async void RemoveSynergy(SynergyListItem item)
    {
        var synergy = _state.Synergies.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (synergy is null)
        {
            return;
        }

        _state.Synergies.Remove(synergy);
        RefreshSynergyRows();
        await SaveAndReportAsync("시너지 제공 규칙을 삭제했습니다.");
    }

    private bool CanAddSynergyExclusion() =>
        !string.IsNullOrWhiteSpace(NewExclusionClassName);

    private async void AddSynergyExclusion()
    {
        if (!CanAddSynergyExclusion())
        {
            return;
        }

        var duplicateExists = _state.SynergyExclusions.Any(exclusion =>
            TextEquals(exclusion.ReceiverClassName, NewExclusionClassName)
            && TextEquals(exclusion.ReceiverBuildName, NewExclusionBuildName)
            && exclusion.Kind == NewExclusionKind);

        if (duplicateExists)
        {
            StatusMessage = "같은 수혜 직업·세팅의 적용 불가 규칙이 이미 등록되어 있습니다.";
            return;
        }

        _state.SynergyExclusions.Add(new SynergyExclusion
        {
            ReceiverClassName = NewExclusionClassName.Trim(),
            ReceiverBuildName = NullIfWhiteSpace(NewExclusionBuildName),
            Kind = NewExclusionKind,
            Reason = NullIfWhiteSpace(NewExclusionReason)
        });

        NewExclusionClassName = string.Empty;
        NewExclusionBuildName = string.Empty;
        NewExclusionReason = string.Empty;
        RefreshSynergyRows();
        await SaveAndReportAsync("시너지 적용 불가 규칙을 추가했습니다.");
    }

    private async void RemoveSynergyExclusion(SynergyExclusionListItem item)
    {
        var exclusion = _state.SynergyExclusions.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (exclusion is null)
        {
            return;
        }

        _state.SynergyExclusions.Remove(exclusion);
        RefreshSynergyRows();
        await SaveAndReportAsync("시너지 적용 불가 규칙을 삭제했습니다.");
    }

    private bool CanSaveRaid() =>
        !string.IsNullOrWhiteSpace(NewRaidGroupKey)
        && !string.IsNullOrWhiteSpace(NewRaidName)
        && NewRaidPlayerCount > 0
        && NewRaidPartySize > 0
        && NewRaidPlayerCount % NewRaidPartySize == 0
        && NewRaidGateCount > 0;

    private async void SaveRaid()
    {
        if (!CanSaveRaid())
        {
            StatusMessage = "레이드 인원은 파티 크기로 나누어져야 하며 관문 수는 1개 이상이어야 합니다.";
            return;
        }

        var raid = _editingRaidId.HasValue
            ? _state.Raids.FirstOrDefault(candidate => candidate.Id == _editingRaidId.Value)
            : null;
        if (raid is not null && HasConfirmedScheduleForRaid(raid.Id))
        {
            StatusMessage = "확정 편성에 사용된 레이드는 수정할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        var duplicateExists = _state.Raids.Any(candidate =>
            candidate.Id != _editingRaidId
            && TextEquals(candidate.Name, NewRaidName)
            && TextEquals(candidate.Difficulty, NewRaidDifficulty));
        if (duplicateExists)
        {
            StatusMessage = "같은 레이드와 난이도가 이미 등록되어 있습니다.";
            return;
        }

        var message = raid is null ? "레이드 기준을 추가했습니다." : "레이드 기준을 수정했습니다.";
        if (raid is null)
        {
            raid = new RaidContent();
            _state.Raids.Add(raid);
        }
        else
        {
            RemoveDraftSchedulesForRaid(raid.Id);
        }

        raid.GroupKey = NewRaidGroupKey.Trim();
        raid.Category = NewRaidCategory.Trim();
        raid.Name = NewRaidName.Trim();
        raid.Difficulty = NewRaidDifficulty.Trim();
        raid.MinimumItemLevel = NewRaidMinimumItemLevel;
        raid.PlayerCount = NewRaidPlayerCount;
        raid.PartySize = NewRaidPartySize;
        raid.GateCount = NewRaidGateCount;
        raid.Priority = NewRaidPriority;

        CancelRaidEdit(clearStatus: false);
        RefreshCollections();
        await SaveAndReportAsync(message);
    }

    private void BeginEditRaid(RaidContent raid)
    {
        _editingRaidId = raid.Id;
        NewRaidGroupKey = raid.GroupKey;
        NewRaidCategory = raid.Category;
        NewRaidName = raid.Name;
        NewRaidDifficulty = raid.Difficulty;
        NewRaidMinimumItemLevel = raid.MinimumItemLevel;
        NewRaidPlayerCount = raid.PlayerCount;
        NewRaidPartySize = raid.PartySize;
        NewRaidGateCount = raid.GateCount;
        NewRaidPriority = raid.Priority;
        OnPropertyChanged(nameof(RaidSubmitText));
        StatusMessage = $"{raid.Name} {raid.Difficulty} 기준을 수정 중입니다.";
    }

    private void CancelRaidEdit() => CancelRaidEdit(clearStatus: true);

    private void CancelRaidEdit(bool clearStatus)
    {
        _editingRaidId = null;
        NewRaidGroupKey = string.Empty;
        NewRaidCategory = string.Empty;
        NewRaidName = string.Empty;
        NewRaidDifficulty = "노말";
        NewRaidMinimumItemLevel = 1700;
        NewRaidPlayerCount = 8;
        NewRaidPartySize = 4;
        NewRaidGateCount = 2;
        NewRaidPriority = 0;
        OnPropertyChanged(nameof(RaidSubmitText));
        if (clearStatus)
        {
            StatusMessage = "레이드 기준 수정을 취소했습니다.";
        }
    }

    private async void ToggleRaidActive(RaidContent raid)
    {
        if (raid.IsActive && HasConfirmedScheduleForRaid(raid.Id))
        {
            StatusMessage = "확정 편성에 사용된 레이드는 비활성화할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        raid.IsActive = !raid.IsActive;
        if (!raid.IsActive)
        {
            RemoveDraftSchedulesForRaid(raid.Id);
        }

        RefreshCollections();
        await SaveAndReportAsync($"{raid.Name} {raid.Difficulty}을 {(raid.IsActive ? "활성화" : "비활성화")}했습니다.");
    }

    private async void DeleteRaid(RaidContent raid)
    {
        if (HasConfirmedScheduleForRaid(raid.Id))
        {
            StatusMessage = "확정 편성에 사용된 레이드는 삭제할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        RemoveDraftSchedulesForRaid(raid.Id);
        _state.WeeklyRaidSelections.RemoveAll(selection => selection.RaidContentId == raid.Id);
        _state.Raids.Remove(raid);
        if (_editingRaidId == raid.Id)
        {
            CancelRaidEdit(clearStatus: false);
        }

        RefreshCollections();
        await SaveAndReportAsync($"{raid.Name} {raid.Difficulty} 기준과 주간 선택 기록을 삭제했습니다.");
    }

    private async void ConfirmOwnerRecommendations(RaidRecommendationItem item)
    {
        var selectedCharacter = _state.Characters.FirstOrDefault(candidate => candidate.Id == item.CharacterId);
        if (selectedCharacter is null)
        {
            StatusMessage = "일괄 확정할 참여자를 찾지 못했습니다.";
            return;
        }

        var owner = _state.People.FirstOrDefault(person => person.Id == selectedCharacter.OwnerId);
        if (owner is null)
        {
            StatusMessage = "일괄 확정할 참여자 정보를 찾지 못했습니다.";
            return;
        }

        var characters = _state.Characters
            .Where(character => character.OwnerId == owner.Id && character.IsActive)
            .OrderByDescending(character => character.ItemLevel)
            .ThenBy(character => character.Name)
            .ToArray();

        if (characters.Length == 0)
        {
            StatusMessage = $"{owner.Name} 참여자에게 활성 캐릭터가 없습니다.";
            return;
        }

        var confirmedCharacterIds = _state.RaidTeamSchedules
            .Where(schedule => schedule.IsConfirmed && schedule.WeekStartLocal == _currentWeekStart)
            .SelectMany(schedule => schedule.Members)
            .Select(member => member.CharacterId)
            .ToHashSet();
        var changedCharacterIds = new List<Guid>();
        var confirmedScheduleSkipCount = 0;
        var progressSkipCount = 0;
        var noRaidSelectionSkipCount = 0;
        var recommendationRows = RaidRecommendations
            .Where(recommendation => recommendation.OwnerName == owner.Name)
            .ToDictionary(recommendation => recommendation.CharacterId);

        foreach (var character in characters)
        {
            if (confirmedCharacterIds.Contains(character.Id))
            {
                confirmedScheduleSkipCount++;
                continue;
            }

            var existingSelections = _state.WeeklyRaidSelections
                .Where(selection => selection.WeekStartLocal == _currentWeekStart
                                    && selection.CharacterId == character.Id)
                .ToArray();

            if (existingSelections.Any(selection => selection.CompletedGateCount > 0))
            {
                progressSkipCount++;
                continue;
            }

            if (!recommendationRows.TryGetValue(character.Id, out var recommendationRow)
                || recommendationRow.SelectedRaidIds.Count == 0)
            {
                noRaidSelectionSkipCount++;
                continue;
            }

            var selectedRaidIds = recommendationRow.SelectedRaidIds.ToHashSet();
            var selectedRaids = _state.Raids
                .Where(raid => selectedRaidIds.Contains(raid.Id) && raid.IsActive)
                .ToArray();
            if (selectedRaids.Length == 0)
            {
                noRaidSelectionSkipCount++;
                continue;
            }

            foreach (var existing in existingSelections)
            {
                _state.WeeklyRaidSelections.Remove(existing);
            }

            foreach (var raid in selectedRaids)
            {
                _state.WeeklyRaidSelections.Add(new WeeklyRaidSelection
                {
                    WeekStartLocal = _currentWeekStart,
                    CharacterId = character.Id,
                    RaidContentId = raid.Id
                });
            }

            changedCharacterIds.Add(character.Id);
        }

        var skipReasons = new List<string>();
        if (confirmedScheduleSkipCount > 0)
        {
            skipReasons.Add($"확정 편성 {confirmedScheduleSkipCount}개");
        }

        if (progressSkipCount > 0)
        {
            skipReasons.Add($"진행 중 {progressSkipCount}개");
        }

        if (noRaidSelectionSkipCount > 0)
        {
            skipReasons.Add($"레이드 미선택 {noRaidSelectionSkipCount}개");
        }

        var skipSummary = skipReasons.Count == 0
            ? string.Empty
            : $" 건너뜀: {string.Join(", ", skipReasons)}.";

        if (changedCharacterIds.Count == 0)
        {
            StatusMessage = $"{owner.Name} 참여자에서 변경할 캐릭터가 없습니다.{skipSummary}";
            return;
        }

        RemoveDraftSchedulesContainingCharacters(changedCharacterIds);
        RefreshWeeklyPlanRows();
        RefreshAutoPartyRows();
        await SaveAndReportAsync(
            $"{owner.Name} 참여자의 활성 캐릭터 {changedCharacterIds.Count}개를 체크한 레이드로 일괄 확정했습니다.{skipSummary}");
    }

    private async void AdvanceRaidGate(WeeklyRaidPlanItem item)
    {
        if (!TryGetWeeklySelection(item.SelectionId, out var selection, out var raid))
        {
            StatusMessage = "주간 레이드 정보를 찾지 못했습니다.";
            return;
        }

        _weeklyRaidProgressService.Advance(selection!, raid!);
        RefreshWeeklyPlanRows();
        var message = _weeklyRaidProgressService.IsCompleted(selection!, raid!)
            ? $"{item.CharacterName}의 {item.RaidName}을 완료 처리했습니다."
            : $"{item.CharacterName}의 {item.RaidName} 진행 관문을 갱신했습니다.";
        await SaveAndReportAsync(message);
    }

    private async void ResetRaidProgress(WeeklyRaidPlanItem item)
    {
        if (!TryGetWeeklySelection(item.SelectionId, out var selection, out _))
        {
            return;
        }

        _weeklyRaidProgressService.Reset(selection!);
        RefreshWeeklyPlanRows();
        await SaveAndReportAsync($"{item.CharacterName}의 {item.RaidName} 진행도를 초기화했습니다.");
    }

    private async void RemoveWeeklyRaid(WeeklyRaidPlanItem item)
    {
        var selection = _state.WeeklyRaidSelections
            .FirstOrDefault(candidate => candidate.Id == item.SelectionId);
        if (selection is null)
        {
            return;
        }

        var linkedSchedules = _state.RaidTeamSchedules
            .Where(schedule => schedule.WeekStartLocal == _currentWeekStart
                               && schedule.RaidContentId == selection.RaidContentId
                               && schedule.Members.Any(member => member.CharacterId == selection.CharacterId))
            .ToArray();
        if (linkedSchedules.Any(schedule => schedule.IsConfirmed))
        {
            StatusMessage = "확정 공격대에 포함된 레이드는 제외할 수 없습니다. 편성 확정을 먼저 해제하세요.";
            return;
        }

        foreach (var schedule in linkedSchedules)
        {
            _state.RaidTeamSchedules.Remove(schedule);
        }

        _state.WeeklyRaidSelections.Remove(selection);
        RefreshWeeklyPlanRows();
        ClearManualSelections();
        RefreshAutoPartyRows();
        await SaveAndReportAsync($"{item.CharacterName}의 {item.RaidName}을 이번 주 계획에서 제외했습니다.");
    }

    private async void GeneratePartyComposition()
    {
        var drafts = _state.RaidTeamSchedules
            .Where(schedule => schedule.WeekStartLocal == _currentWeekStart && !schedule.IsConfirmed)
            .ToArray();
        foreach (var draft in drafts)
        {
            _state.RaidTeamSchedules.Remove(draft);
        }

        var confirmedSchedules = _state.RaidTeamSchedules
            .Where(schedule => schedule.WeekStartLocal == _currentWeekStart && schedule.IsConfirmed)
            .ToArray();
        var confirmedCharacterRaids = confirmedSchedules
            .SelectMany(schedule => schedule.Members.Select(member => (member.CharacterId, schedule.RaidContentId)))
            .ToHashSet();
        var remainingSelections = _state.WeeklyRaidSelections
            .Where(selection => !confirmedCharacterRaids.Contains((selection.CharacterId, selection.RaidContentId)))
            .ToArray();

        var result = _partyCompositionService.Compose(
            _currentWeekStart,
            _state.People,
            _state.Characters,
            _state.AvailabilityWindows,
            remainingSelections,
            _state.Raids,
            _state.Synergies,
            _state.SynergyExclusions,
            _state.PartyCompositionSettings);

        foreach (var team in result.Teams
                     .OrderByDescending(team => team.Raid.Priority)
                     .ThenBy(team => team.TeamNumber))
        {
            var existingTeamNumber = confirmedSchedules
                .Where(schedule => schedule.RaidContentId == team.Raid.Id)
                .Select(schedule => schedule.TeamNumber)
                .DefaultIfEmpty(0)
                .Max();
            var schedule = new RaidTeamSchedule
            {
                WeekStartLocal = _currentWeekStart,
                RaidContentId = team.Raid.Id,
                TeamNumber = existingTeamNumber + team.TeamNumber,
                Day = team.Day,
                StartMinute = team.StartMinute,
                EndMinute = team.EndMinute,
                SynergyScore = team.SynergyScore,
                PartyAveragePowerSpread = team.PartyAveragePowerSpread,
                Members = team.Parties
                    .SelectMany(party => party.Members.Select(character => new RaidTeamMember
                    {
                        CharacterId = character.Id,
                        PartyNumber = party.PartyNumber
                    }))
                    .ToList()
            };
            _state.RaidTeamSchedules.Add(schedule);
        }

        AutoPartyIssues.Clear();
        foreach (var issue in result.UnassignedGroups
                     .OrderByDescending(issue => issue.Raid.Priority))
        {
            AutoPartyIssues.Add(new AutoPartyIssueItem(
                issue.Raid.Name,
                issue.Raid.Difficulty,
                string.Join(", ", issue.Characters.Select(character => character.Name)),
                issue.Reason));
        }

        ClearManualSelections();
        RefreshAutoPartyRows();
        await SaveAndReportAsync("최적화 설정을 적용해 자동 편성 초안을 저장했습니다.");
    }

    private void ToggleSwapMember(AutoPartyMemberItem item)
    {
        var schedule = _state.RaidTeamSchedules.FirstOrDefault(candidate => candidate.Id == item.ScheduleId);
        if (schedule is null || schedule.IsConfirmed)
        {
            StatusMessage = "확정된 편성은 먼저 확정 해제해야 조정할 수 있습니다.";
            return;
        }

        if (!_selectedSwapMemberIds.Remove(item.MemberId))
        {
            if (_selectedSwapMemberIds.Count == 2)
            {
                _selectedSwapMemberIds.Clear();
            }

            _selectedSwapMemberIds.Add(item.MemberId);
        }

        RefreshAutoPartyRows();
        SwapSelectedPartyMembersCommand.RaiseCanExecuteChanged();
    }

    private async void SwapSelectedPartyMembers()
    {
        if (_selectedSwapMemberIds.Count != 2)
        {
            return;
        }

        var selectedMembers = _state.RaidTeamSchedules
            .SelectMany(schedule => schedule.Members.Select(member => new { Schedule = schedule, Member = member }))
            .Where(pair => _selectedSwapMemberIds.Contains(pair.Member.Id))
            .ToArray();
        if (selectedMembers.Length != 2 || selectedMembers[0].Schedule.Id != selectedMembers[1].Schedule.Id)
        {
            StatusMessage = "같은 공격대에 속한 두 캐릭터를 선택하세요.";
            return;
        }

        var schedule = selectedMembers[0].Schedule;
        if (schedule.IsConfirmed)
        {
            StatusMessage = "확정된 편성은 먼저 확정 해제해야 조정할 수 있습니다.";
            return;
        }

        var charactersById = _state.Characters.ToDictionary(character => character.Id);
        var firstCharacter = charactersById.GetValueOrDefault(selectedMembers[0].Member.CharacterId);
        var secondCharacter = charactersById.GetValueOrDefault(selectedMembers[1].Member.CharacterId);
        if (firstCharacter is null || secondCharacter is null)
        {
            StatusMessage = "교환할 캐릭터 정보를 찾지 못했습니다.";
            return;
        }

        if (firstCharacter.Role != secondCharacter.Role)
        {
            StatusMessage = "파티별 서포터 구성을 유지하기 위해 같은 역할끼리만 교환할 수 있습니다.";
            return;
        }

        if (selectedMembers[0].Member.PartyNumber == selectedMembers[1].Member.PartyNumber)
        {
            StatusMessage = "서로 다른 파티의 캐릭터를 선택하세요.";
            return;
        }

        if (PartyContainsSameClass(
                schedule,
                selectedMembers[1].Member.PartyNumber,
                firstCharacter,
                charactersById,
                selectedMembers[1].Member.Id)
            || PartyContainsSameClass(
                schedule,
                selectedMembers[0].Member.PartyNumber,
                secondCharacter,
                charactersById,
                selectedMembers[0].Member.Id))
        {
            StatusMessage = "교환하면 같은 4인 파티에 동일 직업이 중복됩니다.";
            return;
        }

        (selectedMembers[0].Member.PartyNumber, selectedMembers[1].Member.PartyNumber) =
            (selectedMembers[1].Member.PartyNumber, selectedMembers[0].Member.PartyNumber);
        RecalculateScheduleMetrics(schedule, charactersById);
        _selectedSwapMemberIds.Clear();
        RefreshAutoPartyRows();
        SwapSelectedPartyMembersCommand.RaiseCanExecuteChanged();
        await SaveAndReportAsync($"{firstCharacter.Name}과 {secondCharacter.Name}의 파티를 교환했습니다.");
    }

    private void SelectReplacementMember(AutoPartyMemberItem item)
    {
        var schedule = _state.RaidTeamSchedules.FirstOrDefault(candidate => candidate.Id == item.ScheduleId);
        if (schedule is null || schedule.IsConfirmed)
        {
            StatusMessage = "확정된 편성은 먼저 확정 해제해야 교체할 수 있습니다.";
            return;
        }

        _replacementMemberId = _replacementMemberId == item.MemberId ? null : item.MemberId;
        RefreshAutoPartyRows();
        ReplacePartyMemberCommand.RaiseCanExecuteChanged();
    }

    private void SelectUnassignedCharacter(UnassignedCharacterItem item)
    {
        var isSameSelection = _replacementCharacterId == item.CharacterId
                              && _replacementRaidContentId == item.RaidContentId;
        _replacementCharacterId = isSameSelection ? null : item.CharacterId;
        _replacementRaidContentId = isSameSelection ? null : item.RaidContentId;
        RefreshAutoPartyRows();
        ReplacePartyMemberCommand.RaiseCanExecuteChanged();
        FillVacancyCommand.RaiseCanExecuteChanged();
    }

    private async void ReplacePartyMember()
    {
        if (!_replacementMemberId.HasValue || !_replacementCharacterId.HasValue)
        {
            return;
        }

        var schedule = _state.RaidTeamSchedules.FirstOrDefault(candidate =>
            candidate.Members.Any(member => member.Id == _replacementMemberId.Value));
        var member = schedule?.Members.FirstOrDefault(candidate => candidate.Id == _replacementMemberId.Value);
        var replacement = _state.Characters.FirstOrDefault(candidate => candidate.Id == _replacementCharacterId.Value);
        var outgoing = member is null
            ? null
            : _state.Characters.FirstOrDefault(candidate => candidate.Id == member.CharacterId);
        if (schedule is null || member is null || replacement is null || outgoing is null)
        {
            StatusMessage = "교체 대상 정보를 찾지 못했습니다.";
            return;
        }

        if (schedule.IsConfirmed)
        {
            StatusMessage = "확정된 편성은 먼저 확정 해제해야 교체할 수 있습니다.";
            return;
        }

        if (_replacementRaidContentId != schedule.RaidContentId)
        {
            StatusMessage = "교체 대상과 같은 레이드의 미편성 캐릭터를 선택하세요.";
            return;
        }

        if (replacement.Role != outgoing.Role)
        {
            StatusMessage = "파티별 서포터 구성을 유지하기 위해 같은 역할 캐릭터끼리만 교체할 수 있습니다.";
            return;
        }

        var replacementOwner = _state.People.FirstOrDefault(person =>
            person.Id == replacement.OwnerId && person.IsActive);
        if (!replacement.IsActive || replacementOwner is null)
        {
            StatusMessage = "비활성 참여자 또는 캐릭터는 편성할 수 없습니다.";
            return;
        }

        var selection = _state.WeeklyRaidSelections.FirstOrDefault(candidate =>
            candidate.WeekStartLocal == _currentWeekStart
            && candidate.CharacterId == replacement.Id
            && candidate.RaidContentId == schedule.RaidContentId
            && candidate.IsConfirmed);
        var raid = _state.Raids.FirstOrDefault(candidate => candidate.Id == schedule.RaidContentId);
        if (selection is null || raid is null || selection.CompletedGateCount >= raid.GateCount)
        {
            StatusMessage = "교체 후보가 해당 레이드의 진행 가능한 주간 확정 목록에 없습니다.";
            return;
        }

        var charactersById = _state.Characters.ToDictionary(character => character.Id);
        var otherOwnerIds = schedule.Members
            .Where(candidate => candidate.Id != member.Id)
            .Select(candidate => charactersById.GetValueOrDefault(candidate.CharacterId)?.OwnerId)
            .OfType<Guid>()
            .ToHashSet();
        if (otherOwnerIds.Contains(replacement.OwnerId))
        {
            StatusMessage = "같은 참여자의 다른 캐릭터가 이미 이 공격대에 편성되어 있습니다.";
            return;
        }

        if (PartyContainsSameClass(
                schedule,
                member.PartyNumber,
                replacement,
                charactersById,
                member.Id))
        {
            StatusMessage = "교체하면 같은 4인 파티에 동일 직업이 중복됩니다.";
            return;
        }

        member.CharacterId = replacement.Id;
        RecalculateScheduleMetrics(schedule, charactersById);
        _replacementMemberId = null;
        _replacementCharacterId = null;
        _replacementRaidContentId = null;
        RefreshAutoPartyRows();
        ReplacePartyMemberCommand.RaiseCanExecuteChanged();
        FillVacancyCommand.RaiseCanExecuteChanged();
        await SaveAndReportAsync($"{outgoing.Name}을 {replacement.Name}으로 교체했습니다.");
    }

    // 확정된 편성은 그대로 두고 초안만 지워 목록을 비우게 합니다.
    private async void ClearPartyComposition()
    {
        var drafts = _state.RaidTeamSchedules
            .Where(schedule => schedule.WeekStartLocal == _currentWeekStart && !schedule.IsConfirmed)
            .ToArray();
        if (drafts.Length == 0)
        {
            StatusMessage = "지울 초안이 없습니다. 확정된 편성은 확정 해제 후에 지울 수 있습니다.";
            return;
        }

        foreach (var draft in drafts)
        {
            _state.RaidTeamSchedules.Remove(draft);
        }

        ClearManualSelections();
        AutoPartyIssues.Clear();
        RefreshAutoPartyRows();
        await SaveAndReportAsync($"초안 {drafts.Length}개를 지웠습니다. 확정된 편성은 유지됩니다.");
    }

    // 실시간으로 구한 인원을 공석에 바로 채워 넣습니다.
    private async void FillVacancy()
    {
        if (!_replacementCharacterId.HasValue || !_replacementRaidContentId.HasValue)
        {
            return;
        }

        var replacement = _state.Characters.FirstOrDefault(candidate => candidate.Id == _replacementCharacterId.Value);
        var raid = _state.Raids.FirstOrDefault(candidate => candidate.Id == _replacementRaidContentId.Value);
        if (replacement is null || raid is null)
        {
            StatusMessage = "투입할 캐릭터 정보를 찾지 못했습니다.";
            return;
        }

        var replacementOwner = _state.People.FirstOrDefault(person =>
            person.Id == replacement.OwnerId && person.IsActive);
        if (!replacement.IsActive || replacementOwner is null)
        {
            StatusMessage = "비활성 참여자 또는 캐릭터는 편성할 수 없습니다.";
            return;
        }

        var charactersById = _state.Characters.ToDictionary(character => character.Id);
        var partySize = Math.Max(1, raid.PartySize);
        var partyCount = Math.Max(1, (int)Math.Ceiling((decimal)raid.PlayerCount / partySize));

        foreach (var schedule in _state.RaidTeamSchedules
                     .Where(candidate => candidate.WeekStartLocal == _currentWeekStart
                                         && candidate.RaidContentId == raid.Id
                                         && !candidate.IsConfirmed)
                     .OrderBy(candidate => candidate.TeamNumber))
        {
            // 한 공대 안에서만 중복을 막습니다. 다른 공대에는 부캐로 들어갈 수 있습니다.
            var ownerIds = schedule.Members
                .Select(candidate => charactersById.GetValueOrDefault(candidate.CharacterId)?.OwnerId)
                .OfType<Guid>()
                .ToHashSet();
            if (ownerIds.Contains(replacement.OwnerId))
            {
                continue;
            }

            // 서포터가 없는 파티를 먼저 채워 파티마다 서포터가 가도록 유지합니다.
            var targetPartyNumber = Enumerable.Range(1, partyCount)
                .Select(partyNumber => new
                {
                    PartyNumber = partyNumber,
                    Members = schedule.Members.Where(m => m.PartyNumber == partyNumber).ToArray()
                })
                .Where(party => party.Members.Length < partySize)
                .Where(party => !party.Members.Any(member =>
                    charactersById.TryGetValue(member.CharacterId, out var existing)
                    && SameCharacterClass(existing, replacement)))
                .OrderByDescending(party => replacement.Role == CharacterRole.Supporter
                                            && !party.Members.Any(m =>
                                                charactersById.GetValueOrDefault(m.CharacterId)?.Role
                                                == CharacterRole.Supporter))
                .ThenBy(party => party.Members.Length)
                .Select(party => (int?)party.PartyNumber)
                .FirstOrDefault();
            if (targetPartyNumber is null)
            {
                continue;
            }

            schedule.Members.Add(new RaidTeamMember
            {
                CharacterId = replacement.Id,
                PartyNumber = targetPartyNumber.Value
            });
            RecalculateScheduleMetrics(schedule, charactersById);

            _replacementMemberId = null;
            _replacementCharacterId = null;
            _replacementRaidContentId = null;
            RefreshAutoPartyRows();
            ReplacePartyMemberCommand.RaiseCanExecuteChanged();
            FillVacancyCommand.RaiseCanExecuteChanged();
            await SaveAndReportAsync(
                $"{replacement.Name}을 {schedule.TeamNumber}공대 {targetPartyNumber.Value}파티 공석에 투입했습니다.");
            return;
        }

        StatusMessage = "투입할 수 있는 공석이 없습니다. 시간이 겹치는 공석 공대가 있는지 확인하세요.";
    }

    private async void ConfirmPartyComposition()
    {
        var schedules = _state.RaidTeamSchedules
            .Where(schedule => schedule.WeekStartLocal == _currentWeekStart)
            .ToArray();
        if (schedules.Length == 0)
        {
            StatusMessage = "확정할 자동 편성 결과가 없습니다.";
            return;
        }

        var charactersById = _state.Characters.ToDictionary(character => character.Id);
        if (schedules.Any(schedule => ScheduleHasDuplicatePartyClasses(schedule, charactersById)))
        {
            StatusMessage = "동일 직업이 같은 4인 파티에 포함된 편성이 있습니다. 초안을 다시 생성하거나 수동 조정하세요.";
            return;
        }

        foreach (var schedule in schedules)
        {
            schedule.IsConfirmed = true;
        }

        ClearManualSelections();
        RefreshAutoPartyRows();
        await SaveAndReportAsync("이번 주 공격대 편성을 확정했습니다.");
    }

    private async void UnlockPartyComposition()
    {
        var schedules = _state.RaidTeamSchedules
            .Where(schedule => schedule.WeekStartLocal == _currentWeekStart)
            .ToArray();
        if (schedules.Length == 0)
        {
            StatusMessage = "확정 해제할 편성 결과가 없습니다.";
            return;
        }

        foreach (var schedule in schedules)
        {
            schedule.IsConfirmed = false;
        }

        RefreshAutoPartyRows();
        await SaveAndReportAsync("이번 주 공격대 편성을 초안 상태로 전환했습니다.");
    }

    private void RecalculateScheduleMetrics(
        RaidTeamSchedule schedule,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById)
    {
        var parties = schedule.Members
            .GroupBy(member => member.PartyNumber)
            .OrderBy(group => group.Key)
            .Select(group => group
                .Select(member => charactersById.GetValueOrDefault(member.CharacterId))
                .OfType<GameCharacter>()
                .ToArray())
            .ToArray();
        var metrics = _partyCompositionService.EvaluatePartyAssignment(
            parties,
            _state.Synergies,
            _state.SynergyExclusions);
        schedule.SynergyScore = metrics.SynergyScore;
        schedule.PartyAveragePowerSpread = metrics.PartyAveragePowerSpread;
    }

    private void RefreshAutoPartyRows()
    {
        AutoPartyPlans.Clear();
        AutoPartyTeamCards.Clear();
        AutoPartyRunOrder.Clear();
        AutoPartyMembers.Clear();
        var peopleById = _state.People.ToDictionary(person => person.Id);
        var charactersById = _state.Characters.ToDictionary(character => character.Id);
        var raidsById = _state.Raids.ToDictionary(raid => raid.Id);
        var schedules = _state.RaidTeamSchedules
            .Where(schedule => schedule.WeekStartLocal == _currentWeekStart)
            .Where(schedule => raidsById.ContainsKey(schedule.RaidContentId))
            .OrderByDescending(schedule => raidsById[schedule.RaidContentId].Priority)
            .ThenBy(schedule => schedule.TeamNumber)
            .ToArray();

        foreach (var schedule in schedules)
        {
            var raid = raidsById[schedule.RaidContentId];

            // 인원이 없어 통째로 비어 있는 파티도 모집 대상이므로 목록에서 빠지지 않게 채워 넣습니다.
            var partyCount = Math.Max(
                1,
                (int)Math.Ceiling((decimal)raid.PlayerCount / Math.Max(1, raid.PartySize)));
            var memberGroups = schedule.Members
                .GroupBy(member => member.PartyNumber)
                .ToDictionary(group => group.Key, group => (IEnumerable<RaidTeamMember>)group);

            // 8인·16인 레이드도 공대 하나가 한 줄로 보이도록 파티를 묶어서 표시합니다.
            var partyTexts = new List<string>();
            var totalSupporterVacancies = 0;
            var totalDealerVacancies = 0;
            var allCharacters = new List<GameCharacter>();
            var cardParties = new List<AutoPartyCardPartyItem>();

            foreach (var partyNumber in Enumerable.Range(1, partyCount)
                         .Union(memberGroups.Keys)
                         .OrderBy(number => number))
            {
                var members = memberGroups.GetValueOrDefault(partyNumber, []).ToArray();
                var partyCharacters = members
                    .Select(member => charactersById.GetValueOrDefault(member.CharacterId))
                    .OfType<GameCharacter>()
                    .ToArray();
                allCharacters.AddRange(partyCharacters);

                var memberTexts = partyCharacters.Select(character =>
                {
                    var owner = peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "알 수 없음";
                    var role = character.Role == CharacterRole.Supporter ? "서폿" : "딜러";
                    return $"{owner}/{character.Name}({role})";
                }).ToList();

                // 정원을 채우지 못한 자리는 어떤 역할을 모집해야 하는지까지 함께 표시합니다.
                var supporterVacancies = partyCharacters.Any(character => character.Role == CharacterRole.Supporter)
                    ? 0
                    : 1;
                var totalVacancies = Math.Max(0, raid.PartySize - partyCharacters.Length);
                supporterVacancies = Math.Min(supporterVacancies, totalVacancies);
                var dealerVacancies = totalVacancies - supporterVacancies;
                totalSupporterVacancies += supporterVacancies;
                totalDealerVacancies += dealerVacancies;

                var cardSlots = partyCharacters
                    .OrderByDescending(character => character.Role == CharacterRole.Supporter)
                    .ThenBy(character => peopleById.GetValueOrDefault(character.OwnerId)?.Name)
                    .ThenBy(character => character.Name)
                    .Select(character => new AutoPartyCardSlotItem(
                        false,
                        peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "알 수 없음",
                        character.Name,
                        character.ClassName,
                        character.Role == CharacterRole.Supporter ? "서포터" : "딜러",
                        $"Lv. {character.ItemLevel:N2}",
                        $"전투력 {character.CombatPower:N0}",
                        character.Role == CharacterRole.Supporter ? "#47B7A5" : "#7C6CF2"))
                    .ToList();
                cardSlots.AddRange(Enumerable.Range(0, supporterVacancies)
                    .Select(_ => new AutoPartyCardSlotItem(
                        true,
                        "공석",
                        "서포터 모집",
                        string.Empty,
                        "서포터",
                        string.Empty,
                        string.Empty,
                        "#47B7A5")));
                cardSlots.AddRange(Enumerable.Range(0, dealerVacancies)
                    .Select(_ => new AutoPartyCardSlotItem(
                        true,
                        "공석",
                        "딜러 모집",
                        string.Empty,
                        "딜러",
                        string.Empty,
                        string.Empty,
                        "#596276")));

                var partyAverage = partyCharacters.Length == 0
                    ? 0
                    : partyCharacters.Average(character => character.CombatPower);
                cardParties.Add(new AutoPartyCardPartyItem(
                    $"{partyNumber}파티",
                    $"{partyCharacters.Length} / {raid.PartySize}",
                    $"평균 전투력 {partyAverage:N0}",
                    cardSlots));

                memberTexts.AddRange(Enumerable.Repeat("공석(서포터)", supporterVacancies));
                memberTexts.AddRange(Enumerable.Repeat("공석(딜러)", dealerVacancies));

                // 파티가 하나뿐이면 굳이 파티 번호를 붙이지 않습니다.
                partyTexts.Add(partyCount > 1
                    ? $"[{partyNumber}파티] {string.Join(", ", memberTexts)}"
                    : string.Join(", ", memberTexts));
            }

            var teamAverage = allCharacters.Count == 0
                ? 0
                : allCharacters.Average(character => character.CombatPower);

            AutoPartyPlans.Add(new AutoPartyPlanItem(
                raid.Name,
                raid.Difficulty,
                $"{schedule.TeamNumber}공대",
                partyCount > 1 ? $"{partyCount}개 파티" : "1파티",
                true,
                FormatAvailability(schedule.Day, schedule.StartMinute, schedule.EndMinute),
                string.Join("   ", partyTexts),
                $"{teamAverage:N0}",
                $"{schedule.SynergyScore:0.##}",
                $"{schedule.PartyAveragePowerSpread:N0}",
                BuildVacancyText(totalSupporterVacancies, totalDealerVacancies)));

            AutoPartyTeamCards.Add(new AutoPartyTeamCardItem(
                schedule.Id,
                raid.Name,
                raid.Difficulty,
                $"{schedule.TeamNumber}공대",
                schedule.IsConfirmed ? "확정" : "초안",
                GetRaidCardAccentColor(raid.Difficulty, schedule.IsConfirmed),
                $"{allCharacters.Count} / {raid.PlayerCount}명",
                BuildVacancyText(totalSupporterVacancies, totalDealerVacancies),
                $"평균 전투력 {teamAverage:N0}",
                $"시너지 {schedule.SynergyScore:0.##}",
                $"파티 격차 {schedule.PartyAveragePowerSpread:N0}",
                cardParties));

            foreach (var partyNumber in memberGroups.Keys.OrderBy(number => number))
            {
                var partyMembers = memberGroups[partyNumber].ToArray();
                foreach (var member in partyMembers)
                {
                    if (!charactersById.TryGetValue(member.CharacterId, out var character))
                    {
                        continue;
                    }

                    AutoPartyMembers.Add(new AutoPartyMemberItem(
                        member.Id,
                        schedule.Id,
                        character.Id,
                        raid.Name,
                        raid.Difficulty,
                        $"{schedule.TeamNumber}공대",
                        $"{member.PartyNumber}파티",
                        FormatAvailability(schedule.Day, schedule.StartMinute, schedule.EndMinute),
                        peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "알 수 없음",
                        character.Name,
                        character.Role == CharacterRole.Supporter ? "서포터" : "딜러",
                        $"{character.CombatPower:N0}",
                        _selectedSwapMemberIds.Contains(member.Id) ? "선택됨" : "선택",
                        _replacementMemberId == member.Id ? "교체 대상됨" : "교체 대상",
                        schedule.IsConfirmed ? "확정" : "초안"));
                }
            }
        }

        var cardsByScheduleId = AutoPartyTeamCards.ToDictionary(card => card.ScheduleId);
        var runOrder = _raidRunOrderService.Optimize(schedules, charactersById);
        foreach (var step in runOrder.Steps)
        {
            if (!cardsByScheduleId.TryGetValue(step.Schedule.Id, out var card))
            {
                continue;
            }

            var changeDetails = step.Changes
                .Select(change =>
                {
                    var ownerName = peopleById.GetValueOrDefault(change.OwnerId)?.Name ?? "알 수 없음";
                    var previousName = charactersById.GetValueOrDefault(change.PreviousCharacterId)?.Name ?? "알 수 없음";
                    var nextName = charactersById.GetValueOrDefault(change.NextCharacterId)?.Name ?? "알 수 없음";
                    return $"{ownerName}: {previousName} → {nextName}";
                })
                .ToArray();
            var reenteredOwners = step.ReenteredOwnerIds
                .Select(ownerId => peopleById.GetValueOrDefault(ownerId)?.Name ?? "알 수 없음")
                .ToArray();
            var attendanceSummary = step.Order == 1
                ? $"시작 참여 {step.JoiningCount}명"
                : $"참여 유지 {step.ContinuingCount}명 · 합류 {step.JoiningCount}명 · 이탈 {step.LeavingCount}명";
            if (reenteredOwners.Length > 0)
            {
                attendanceSummary += $" · 중간 재참여: {string.Join(", ", reenteredOwners)}";
            }

            AutoPartyRunOrder.Add(new AutoPartyRunOrderItem(
                step.Order,
                card,
                step.Order == 1
                    ? "시작 공격대"
                    : $"캐릭터 변경 {step.SwitchCount}명 · 유지 {step.ReuseCount}명",
                attendanceSummary,
                changeDetails.Length == 0
                    ? "추가 캐릭터 변경 없음"
                    : string.Join(" · ", changeDetails)));
        }

        AutoPartyRunOrderSummary = runOrder.Steps.Count == 0
            ? "저장된 공격대가 없습니다. 자동 편성 초안을 먼저 생성하세요."
            : $"편성 인원 많은 순 · {runOrder.Steps.Count}개 공격대 · 중간 재참여 {runOrder.TotalReentryCount}회 · 대기 공격대 {runOrder.TotalGapRaidCount}개 · 캐릭터 변경 {runOrder.TotalSwitchCount}회";

        RefreshUnassignedCharacterRows(schedules, peopleById, charactersById, raidsById);
        var confirmedCount = schedules.Count(schedule => schedule.IsConfirmed);
        var vacancyCount = schedules.Sum(schedule =>
            raidsById.TryGetValue(schedule.RaidContentId, out var raid)
                ? Math.Max(0, raid.PlayerCount - schedule.Members.Count)
                : 0);
        var vacancySummary = vacancyCount > 0 ? $" · 공석 {vacancyCount}자리" : string.Empty;
        AutoPartySummary = schedules.Length == 0
            ? "저장된 편성 결과가 없습니다. 자동 편성을 실행하세요."
            : $"{schedules.Length}개 공격대 · 확정 {confirmedCount}개 · 편성 캐릭터 {AutoPartyMembers.Count}개{vacancySummary} · 미편성 {UnassignedCharacters.Count}개";
    }

    // 캐릭터 하나하나가 왜 빠졌는지 실제 사유를 찾아 표시합니다.
    private string BuildUnassignedCharacterReason(
        GameCharacter character,
        Person owner,
        RaidContent raid,
        IReadOnlyCollection<RaidTeamSchedule> schedules,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById,
        IReadOnlyDictionary<(string RaidName, string Difficulty), string> issueReasons)
    {
        var raidSchedules = schedules
            .Where(schedule => schedule.RaidContentId == raid.Id)
            .ToArray();

        // 1) 이 캐릭터가 이미 어느 공대에 들어간 경우
        var placed = raidSchedules.FirstOrDefault(schedule =>
            schedule.Members.Any(member => member.CharacterId == character.Id));
        if (placed is not null)
        {
            return $"{character.Name}은(는) 이미 {placed.TeamNumber}공대에 편성되어 있습니다.";
        }

        // 2) 공대는 있지만 공석이 없는 경우
        if (raidSchedules.Length > 0 && raidSchedules.All(schedule => schedule.Members.Count >= raid.PlayerCount))
        {
            return "이 레이드의 공대가 모두 정원을 채웠습니다.";
        }

        // 3) 공석이 남아 있는 경우
        var openSchedule = raidSchedules
            .FirstOrDefault(schedule => schedule.Members.Count < raid.PlayerCount);
        if (openSchedule is not null)
        {
            return $"{openSchedule.TeamNumber}공대에 공석이 있습니다. '공석에 투입'으로 넣을 수 있습니다.";
        }

        return issueReasons.GetValueOrDefault(
            (raid.Name, raid.Difficulty),
            "이 레이드의 공대가 아직 만들어지지 않았습니다. 자동 편성을 실행하세요.");
    }

    private static string BuildVacancyText(int supporterVacancies, int dealerVacancies)
    {
        if (supporterVacancies + dealerVacancies == 0)
        {
            return "정원 충족";
        }

        var parts = new List<string>();
        if (supporterVacancies > 0)
        {
            parts.Add($"서포터 {supporterVacancies}");
        }

        if (dealerVacancies > 0)
        {
            parts.Add($"딜러 {dealerVacancies}");
        }

        return $"모집 필요: {string.Join(" · ", parts)}";
    }

    private static string GetRaidCardAccentColor(string difficulty, bool isConfirmed)
    {
        if (isConfirmed)
        {
            return "#2E8B68";
        }

        return difficulty switch
        {
            "나이트메어" => "#64258A",
            "하드" => "#9B651D",
            "노말" => "#405E86",
            _ => "#6656C9"
        };
    }

    private void RefreshUnassignedCharacterRows(
        IReadOnlyCollection<RaidTeamSchedule> schedules,
        IReadOnlyDictionary<Guid, Person> peopleById,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById,
        IReadOnlyDictionary<Guid, RaidContent> raidsById)
    {
        UnassignedCharacters.Clear();
        var assignedCharacterRaids = schedules
            .SelectMany(schedule => schedule.Members.Select(member => (member.CharacterId, schedule.RaidContentId)))
            .ToHashSet();
        var issueReasons = AutoPartyIssues
            .GroupBy(issue => (issue.RaidName, issue.Difficulty))
            .ToDictionary(group => group.Key, group => group.First().Reason);

        foreach (var selection in _state.WeeklyRaidSelections
                     .Where(selection => selection.WeekStartLocal == _currentWeekStart && selection.IsConfirmed))
        {
            if (assignedCharacterRaids.Contains((selection.CharacterId, selection.RaidContentId))
                || !charactersById.TryGetValue(selection.CharacterId, out var character)
                || !character.IsActive
                || !peopleById.TryGetValue(character.OwnerId, out var owner)
                || !owner.IsActive
                || !raidsById.TryGetValue(selection.RaidContentId, out var raid)
                || !raid.IsActive
                || selection.CompletedGateCount >= raid.GateCount)
            {
                continue;
            }

            var reason = BuildUnassignedCharacterReason(
                character,
                owner,
                raid,
                schedules,
                charactersById,
                issueReasons);
            UnassignedCharacters.Add(new UnassignedCharacterItem(
                character.Id,
                raid.Id,
                raid.Name,
                raid.Difficulty,
                owner.Name,
                character.Name,
                character.Role == CharacterRole.Supporter ? "서포터" : "딜러",
                $"{character.CombatPower:N0}",
                _replacementCharacterId == character.Id && _replacementRaidContentId == raid.Id
                    ? "투입 선택됨"
                    : "투입 선택",
                reason));
        }
    }

    private bool TryGetWeeklySelection(
        Guid selectionId,
        out WeeklyRaidSelection? selection,
        out RaidContent? raid)
    {
        selection = _state.WeeklyRaidSelections
            .FirstOrDefault(candidate => candidate.Id == selectionId);
        var raidContentId = selection?.RaidContentId;
        raid = raidContentId.HasValue
            ? _state.Raids.FirstOrDefault(candidate => candidate.Id == raidContentId.Value)
            : null;
        return selection is not null && raid is not null;
    }

    private bool HasConfirmedScheduleForCharacters(IReadOnlyCollection<Guid> characterIds) =>
        _scheduleReferenceService.HasConfirmedCharacterReference(_state.RaidTeamSchedules, characterIds);

    private bool HasConfirmedScheduleForRaid(Guid raidContentId) =>
        _scheduleReferenceService.HasConfirmedRaidReference(_state.RaidTeamSchedules, raidContentId);

    private void RemoveDraftSchedulesContainingCharacters(IReadOnlyCollection<Guid> characterIds)
    {
        if (characterIds.Count == 0)
        {
            return;
        }

        _scheduleReferenceService.RemoveDraftsContainingCharacters(_state.RaidTeamSchedules, characterIds);
        ClearManualSelections();
    }

    private void RemoveDraftSchedulesForRaid(Guid raidContentId)
    {
        _scheduleReferenceService.RemoveDraftsForRaid(_state.RaidTeamSchedules, raidContentId);
        ClearManualSelections();
    }

    private void ClearManualSelections()
    {
        _selectedSwapMemberIds.Clear();
        _replacementMemberId = null;
        _replacementCharacterId = null;
        _replacementRaidContentId = null;
        SwapSelectedPartyMembersCommand.RaiseCanExecuteChanged();
        ReplacePartyMemberCommand.RaiseCanExecuteChanged();
        FillVacancyCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveAndReportAsync(string message)
    {
        try
        {
            await _store.SaveAsync(_state);
            StatusMessage = message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"저장하지 못했습니다: {exception.Message}";
        }
    }

    private void RefreshCollections()
    {
        People.Clear();
        foreach (var person in _state.People.OrderBy(person => person.Name))
        {
            People.Add(person);
        }

        Raids.Clear();
        foreach (var raid in _state.Raids
                     .OrderByDescending(raid => raid.MinimumItemLevel)
                     .ThenBy(raid => raid.Name))
        {
            Raids.Add(raid);
        }

        NewCharacterOwnerId = People.FirstOrDefault()?.Id;
        NewAvailabilityPersonId = People.FirstOrDefault()?.Id;
        RefreshCharacterOwnerFilters();
        RefreshRaidChoices();
        RefreshCharacterRows();
        RefreshAvailabilityRows();
        RefreshSynergyRows();
        RefreshRaidRecommendations();
        RefreshWeeklyPlanRows();
        RefreshAutoPartyRows();
        NotifyCounters();
        OnPropertyChanged(nameof(CurrentWeekLabel));
        OnPropertyChanged(nameof(SynergyWeight));
        OnPropertyChanged(nameof(CombatPowerBalanceWeight));
        OnPropertyChanged(nameof(AvailabilityWeight));
        OnPropertyChanged(nameof(CharacterSwitchPenalty));
        OnPropertyChanged(nameof(MinimumCommonMinutes));
    }

    // 목록을 한 참여자로 좁혀서 볼 수 있게 합니다. null이면 전체를 보여줍니다.
    public Guid? CharacterOwnerFilterId
    {
        get => _characterOwnerFilterId;
        set
        {
            if (SetProperty(ref _characterOwnerFilterId, value))
            {
                RefreshCharacterRows();
                RefreshRaidRecommendations();
                RefreshWeeklyPlanRows();
                OnPropertyChanged(nameof(CharacterFilterSummary));
            }
        }
    }

    public string CharacterFilterSummary
    {
        get
        {
            var total = _state.Characters.Count;
            if (!_characterOwnerFilterId.HasValue)
            {
                return $"전체 {total}개 표시 중";
            }

            var ownerName = _state.People
                .FirstOrDefault(person => person.Id == _characterOwnerFilterId.Value)?.Name ?? "알 수 없음";
            return $"{ownerName}님의 캐릭터 {Characters.Count}개 / 전체 {total}개";
        }
    }

    private void RefreshCharacterOwnerFilters()
    {
        var previous = _characterOwnerFilterId;
        CharacterOwnerFilters.Clear();
        CharacterOwnerFilters.Add(new CharacterOwnerFilterOption(null, "전체 보기"));
        foreach (var person in _state.People.OrderBy(person => person.Name))
        {
            var count = _state.Characters.Count(character => character.OwnerId == person.Id);
            CharacterOwnerFilters.Add(new CharacterOwnerFilterOption(person.Id, $"{person.Name} ({count})"));
        }

        // 삭제된 참여자가 선택되어 있었다면 전체 보기로 되돌립니다.
        if (previous.HasValue && _state.People.All(person => person.Id != previous.Value))
        {
            CharacterOwnerFilterId = null;
        }
    }

    private void RefreshCharacterRows()
    {
        Characters.Clear();
        var peopleById = _state.People.ToDictionary(person => person.Id);

        // 사람별로 모아서 보여주고, 선택된 참여자가 있으면 그 사람만 봅니다.
        foreach (var character in _state.Characters
                     .Where(character => !CharacterOwnerFilterId.HasValue
                                         || character.OwnerId == CharacterOwnerFilterId.Value)
                     .OrderBy(character => peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "힐")
                     .ThenByDescending(character => character.ItemLevel)
                     .ThenBy(character => character.Name))
        {
            var ownerName = peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "알 수 없음";
            Characters.Add(new CharacterListItem(
                character.Id,
                ownerName,
                character.Name,
                character.ClassName,
                string.IsNullOrWhiteSpace(character.BuildName) ? "전체" : character.BuildName,
                character.Role == CharacterRole.Supporter ? "서포터" : "딜러",
                character.ItemLevel,
                character.CombatPower,
                character.IsActive));
        }

        OnPropertyChanged(nameof(CharacterFilterSummary));
    }

    private void RefreshAvailabilityRows()
    {
        AvailabilityWindows.Clear();
        var peopleById = _state.People.ToDictionary(person => person.Id);
        var dayOrder = WeekDays
            .Select((day, index) => (day.Value, index))
            .ToDictionary(pair => pair.Value, pair => pair.index);

        foreach (var window in _state.AvailabilityWindows
                     .OrderBy(window => dayOrder.GetValueOrDefault(window.Day, int.MaxValue))
                     .ThenBy(window => window.StartMinute)
                     .ThenBy(window => peopleById.GetValueOrDefault(window.PersonId)?.Name))
        {
            AvailabilityWindows.Add(new AvailabilityListItem(
                window.Id,
                peopleById.GetValueOrDefault(window.PersonId)?.Name ?? "알 수 없음",
                KoreanDayNames.GetValueOrDefault(window.Day, window.Day.ToString()),
                FormatMinute(window.StartMinute),
                FormatMinute(window.EndMinute)));
        }
    }

    private void RefreshSynergyRows()
    {
        Synergies.Clear();
        foreach (var synergy in _state.Synergies
                     .OrderBy(synergy => synergy.ClassName)
                     .ThenBy(synergy => synergy.RequiredBuildName)
                     .ThenBy(synergy => synergy.Kind))
        {
            var conditionText = synergy.IsConditional
                ? synergy.ConditionDescription ?? "조건부"
                : "상시";
            Synergies.Add(new SynergyListItem(
                synergy.Id,
                synergy.ClassName,
                string.IsNullOrWhiteSpace(synergy.RequiredBuildName) ? "전체" : synergy.RequiredBuildName,
                KoreanSynergyKindNames[synergy.Kind],
                $"{synergy.Value:0.##}%",
                string.IsNullOrWhiteSpace(synergy.StackingGroup) ? "독립" : synergy.StackingGroup,
                conditionText));
        }

        SynergyExclusions.Clear();
        foreach (var exclusion in _state.SynergyExclusions
                     .OrderBy(exclusion => exclusion.ReceiverClassName)
                     .ThenBy(exclusion => exclusion.ReceiverBuildName)
                     .ThenBy(exclusion => exclusion.Kind))
        {
            SynergyExclusions.Add(new SynergyExclusionListItem(
                exclusion.Id,
                exclusion.ReceiverClassName,
                string.IsNullOrWhiteSpace(exclusion.ReceiverBuildName) ? "전체" : exclusion.ReceiverBuildName,
                KoreanSynergyKindNames[exclusion.Kind],
                exclusion.Reason ?? "-"));
        }
    }

    private void RefreshRaidRecommendations()
    {
        RaidRecommendations.Clear();
        var peopleById = _state.People.ToDictionary(person => person.Id);

        foreach (var character in _state.Characters
                     .Where(character => character.IsActive)
                     .Where(character => !CharacterOwnerFilterId.HasValue
                                         || character.OwnerId == CharacterOwnerFilterId.Value)
                     .OrderBy(character => peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "힐")
                     .ThenByDescending(character => character.ItemLevel)
                     .ThenBy(character => character.Name))
        {
            var recommendations = _raidRecommendationService.RecommendTopRaids(character, _state.Raids);
            var existingRaidIds = _state.WeeklyRaidSelections
                .Where(selection => selection.WeekStartLocal == _currentWeekStart
                                    && selection.CharacterId == character.Id)
                .Select(selection => selection.RaidContentId)
                .ToHashSet();
            var selectAllByDefault = existingRaidIds.Count == 0;
            var choices = recommendations
                .Select(raid => new RaidRecommendationChoice(
                    raid.Id,
                    FormatRaidRecommendation(raid),
                    selectAllByDefault || existingRaidIds.Contains(raid.Id)))
                .Concat(Enumerable.Range(0, 3)
                    .Select(_ => new RaidRecommendationChoice(null, "-", false)))
                .Take(3)
                .ToArray();

            RaidRecommendations.Add(new RaidRecommendationItem(
                character.Id,
                peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "알 수 없음",
                character.Name,
                character.ItemLevel,
                choices[0],
                choices[1],
                choices[2]));
        }

        if (RaidRecommendations.Count == 0)
        {
            StatusMessage = CharacterOwnerFilterId.HasValue
                ? "선택한 참여자의 활성 캐릭터가 없습니다."
                : "추천할 캐릭터가 없습니다. 캐릭터를 먼저 등록하세요.";
        }
        else
        {
            StatusMessage = $"{RaidRecommendations.Count}개 캐릭터의 상위 레이드 추천을 계산했습니다.";
        }
    }

    // 주간 편성에서 캐릭터의 레이드를 직접 바꾸거나 추가할 때 쓰는 상태입니다.
    private Guid? _weeklyRaidTargetCharacterId;

    public Guid? WeeklyRaidTargetCharacterId
    {
        get => _weeklyRaidTargetCharacterId;
        set
        {
            if (SetProperty(ref _weeklyRaidTargetCharacterId, value))
            {
                AddWeeklyRaidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Guid? ReplacementRaidChoiceId
    {
        get => _replacementRaidChoiceId;
        set
        {
            if (SetProperty(ref _replacementRaidChoiceId, value))
            {
                ApplyWeeklyRaidChangeCommand.RaiseCanExecuteChanged();
                AddWeeklyRaidCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WeeklyRaidEditSummary => _editingSelectionId.HasValue
        ? "변경할 레이드를 고르고 [변경 적용]을 누르세요."
        : "목록의 [레이드 변경]을 누르면 그 줄의 레이드를 바꿀 수 있습니다.";

    private void RefreshRaidChoices()
    {
        var previous = _replacementRaidChoiceId;
        RaidChoices.Clear();
        foreach (var raid in _state.Raids
                     .Where(raid => raid.IsActive)
                     .OrderByDescending(raid => raid.Priority)
                     .ThenByDescending(raid => raid.MinimumItemLevel)
                     .ThenBy(raid => raid.Name))
        {
            RaidChoices.Add(new RaidChoiceOption(raid.Id, FormatRaidRecommendation(raid)));
        }

        if (previous.HasValue && RaidChoices.All(choice => choice.Value != previous.Value))
        {
            ReplacementRaidChoiceId = null;
        }
    }

    private void BeginEditWeeklyRaid(WeeklyRaidPlanItem item)
    {
        // 같은 줄을 다시 누르면 선택이 해제됩니다.
        _editingSelectionId = _editingSelectionId == item.SelectionId ? null : item.SelectionId;
        if (_editingSelectionId.HasValue)
        {
            var selection = _state.WeeklyRaidSelections
                .FirstOrDefault(candidate => candidate.Id == _editingSelectionId.Value);
            ReplacementRaidChoiceId = selection?.RaidContentId;
            WeeklyRaidTargetCharacterId = item.CharacterId;
        }

        RefreshWeeklyPlanRows();
        ApplyWeeklyRaidChangeCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(WeeklyRaidEditSummary));
    }

    private void CancelWeeklyRaidEdit()
    {
        _editingSelectionId = null;
        RefreshWeeklyPlanRows();
        ApplyWeeklyRaidChangeCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(WeeklyRaidEditSummary));
        StatusMessage = "레이드 변경을 취소했습니다.";
    }

    private async void ApplyWeeklyRaidChange()
    {
        if (!_editingSelectionId.HasValue || !_replacementRaidChoiceId.HasValue)
        {
            return;
        }

        var selection = _state.WeeklyRaidSelections
            .FirstOrDefault(candidate => candidate.Id == _editingSelectionId.Value);
        var raid = _state.Raids.FirstOrDefault(candidate => candidate.Id == _replacementRaidChoiceId.Value);
        var character = selection is null
            ? null
            : _state.Characters.FirstOrDefault(candidate => candidate.Id == selection.CharacterId);
        if (selection is null || raid is null || character is null)
        {
            StatusMessage = "변경할 대상을 찾지 못했습니다.";
            return;
        }

        if (selection.RaidContentId == raid.Id)
        {
            StatusMessage = "이미 같은 레이드입니다.";
            return;
        }

        if (!TryEnsureWeeklyRaidChangeAllowed(character, selection.RaidContentId, raid))
        {
            return;
        }

        selection.RaidContentId = raid.Id;
        selection.CompletedGateCount = 0;
        _editingSelectionId = null;

        RefreshWeeklyPlanRows();
        ClearManualSelections();
        RefreshAutoPartyRows();
        OnPropertyChanged(nameof(WeeklyRaidEditSummary));
        await SaveAndReportAsync($"{character.Name}의 레이드를 {raid.Name} {raid.Difficulty}(으)로 변경했습니다.");
    }

    private async void AddWeeklyRaid()
    {
        if (!WeeklyRaidTargetCharacterId.HasValue || !_replacementRaidChoiceId.HasValue)
        {
            return;
        }

        var character = _state.Characters
            .FirstOrDefault(candidate => candidate.Id == WeeklyRaidTargetCharacterId.Value);
        var raid = _state.Raids.FirstOrDefault(candidate => candidate.Id == _replacementRaidChoiceId.Value);
        if (character is null || raid is null)
        {
            StatusMessage = "추가할 캐릭터 또는 레이드를 찾지 못했습니다.";
            return;
        }

        if (!TryEnsureWeeklyRaidChangeAllowed(character, null, raid))
        {
            return;
        }

        _state.WeeklyRaidSelections.Add(new WeeklyRaidSelection
        {
            WeekStartLocal = _currentWeekStart,
            CharacterId = character.Id,
            RaidContentId = raid.Id
        });

        RefreshWeeklyPlanRows();
        RefreshAutoPartyRows();
        await SaveAndReportAsync($"{character.Name}에 {raid.Name} {raid.Difficulty}를 추가했습니다.");
    }

    // 확정 편성 보호, 중복 방지, 입장 레벨 제한을 한꺼번에 검사합니다.
    private bool TryEnsureWeeklyRaidChangeAllowed(
        GameCharacter character,
        Guid? currentRaidContentId,
        RaidContent raid)
    {
        if (_state.RaidTeamSchedules.Any(schedule =>
                schedule.IsConfirmed
                && schedule.WeekStartLocal == _currentWeekStart
                && schedule.Members.Any(member => member.CharacterId == character.Id)))
        {
            StatusMessage = "확정 공격대에 포함된 캐릭터입니다. 편성 확정을 먼저 해제하세요.";
            return false;
        }

        if (_state.WeeklyRaidSelections.Any(candidate =>
                candidate.WeekStartLocal == _currentWeekStart
                && candidate.CharacterId == character.Id
                && candidate.RaidContentId == raid.Id))
        {
            StatusMessage = $"{character.Name}는 이미 {raid.Name} {raid.Difficulty}를 선택했습니다.";
            return false;
        }

        if (raid.MinimumItemLevel > character.ItemLevel)
        {
            StatusMessage =
                $"{character.Name}의 레벨({character.ItemLevel:N2})이 {raid.Name} {raid.Difficulty} 입장 레벨({raid.MinimumItemLevel:N0})보다 낮습니다.";
            return false;
        }

        // 바꾸기 전 레이드로 잡혀 있던 초안은 정리합니다.
        if (currentRaidContentId.HasValue)
        {
            var linked = _state.RaidTeamSchedules
                .Where(schedule => schedule.WeekStartLocal == _currentWeekStart
                                   && schedule.RaidContentId == currentRaidContentId.Value
                                   && schedule.Members.Any(member => member.CharacterId == character.Id))
                .ToArray();
            foreach (var schedule in linked)
            {
                _state.RaidTeamSchedules.Remove(schedule);
            }
        }

        return true;
    }

    private void RefreshWeeklyPlanRows()
    {
        WeeklyRaidPlan.Clear();
        var peopleById = _state.People.ToDictionary(person => person.Id);
        var charactersById = _state.Characters.ToDictionary(character => character.Id);
        var raidsById = _state.Raids.ToDictionary(raid => raid.Id);

        var rows = _state.WeeklyRaidSelections
            .Where(selection => selection.WeekStartLocal == _currentWeekStart)
            .Select(selection =>
            {
                charactersById.TryGetValue(selection.CharacterId, out var character);
                raidsById.TryGetValue(selection.RaidContentId, out var raid);
                var ownerName = character is null
                    ? "알 수 없음"
                    : peopleById.GetValueOrDefault(character.OwnerId)?.Name ?? "알 수 없음";
                var gateCount = raid?.GateCount ?? 0;
                var completed = raid is not null && _weeklyRaidProgressService.IsCompleted(selection, raid);

                return new
                {
                    Selection = selection,
                    Character = character,
                    Raid = raid,
                    OwnerName = ownerName,
                    Progress = $"{Math.Min(selection.CompletedGateCount, gateCount)} / {gateCount}",
                    Status = completed ? "완료" : selection.CompletedGateCount > 0 ? "진행 중" : "미진행"
                };
            })
            .Where(row => row.Character is not null && row.Raid is not null)
            .Where(row => !CharacterOwnerFilterId.HasValue
                          || row.Character!.OwnerId == CharacterOwnerFilterId.Value)
            // 같은 캐릭터의 레이드가 한데 모이도록 캐릭터명을 먼저 정렬합니다.
            .OrderBy(row => row.OwnerName)
            .ThenBy(row => row.Character!.Name)
            .ThenByDescending(row => row.Raid!.MinimumItemLevel)
            .ThenBy(row => row.Raid!.Name);

        foreach (var row in rows)
        {
            WeeklyRaidPlan.Add(new WeeklyRaidPlanItem(
                row.Selection.Id,
                row.Character!.Id,
                row.OwnerName,
                row.Character.Name,
                row.Raid!.Name,
                row.Raid.Difficulty,
                row.Progress,
                row.Status,
                _editingSelectionId == row.Selection.Id ? "변경 중" : "레이드 변경"));
        }
    }

    private void NotifyCounters()
    {
        OnPropertyChanged(nameof(ActivePeopleCount));
        OnPropertyChanged(nameof(ActiveCharacterCount));
        OnPropertyChanged(nameof(ActiveRaidCount));
    }

    private static IReadOnlyList<TimeOption> BuildTimeOptions()
    {
        var options = new List<TimeOption>();
        for (var minute = 12 * 60; minute <= 27 * 60; minute += 30)
        {
            options.Add(new TimeOption(minute, FormatMinute(minute)));
        }

        return options;
    }

    private static string FormatMinute(int minute)
    {
        var isNextDay = minute >= 24 * 60;
        var normalized = minute % (24 * 60);
        var prefix = isNextDay ? "익일 " : string.Empty;
        return $"{prefix}{normalized / 60:00}:{normalized % 60:00}";
    }

    private static string FormatRaidRecommendation(RaidContent raid) =>
        $"{raid.Name} {raid.Difficulty} ({raid.MinimumItemLevel:N0})";

    private static string FormatAvailability(DayOfWeek day, int startMinute, int endMinute) =>
        $"{KoreanDayNames.GetValueOrDefault(day, day.ToString())} {FormatMinute(startMinute)}~{FormatMinute(endMinute)}";

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TextEquals(string? left, string? right) =>
        string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool PartyContainsSameClass(
        RaidTeamSchedule schedule,
        int partyNumber,
        GameCharacter character,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById,
        Guid? excludedMemberId = null) =>
        schedule.Members
            .Where(member => member.PartyNumber == partyNumber && member.Id != excludedMemberId)
            .Select(member => charactersById.GetValueOrDefault(member.CharacterId))
            .OfType<GameCharacter>()
            .Any(existing => SameCharacterClass(existing, character));

    private static bool ScheduleHasDuplicatePartyClasses(
        RaidTeamSchedule schedule,
        IReadOnlyDictionary<Guid, GameCharacter> charactersById) =>
        schedule.Members
            .GroupBy(member => member.PartyNumber)
            .Any(party => party
                .Select(member => charactersById.GetValueOrDefault(member.CharacterId)?.ClassName?.Trim())
                .Where(className => !string.IsNullOrWhiteSpace(className))
                .GroupBy(className => className!, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1));

    private static bool SameCharacterClass(GameCharacter left, GameCharacter right) =>
        !string.IsNullOrWhiteSpace(left.ClassName)
        && !string.IsNullOrWhiteSpace(right.ClassName)
        && TextEquals(left.ClassName, right.ClassName);
}
