using LoaSchedule.Domain.Models;

namespace LoaSchedule.Infrastructure.Persistence;

internal static class SeedDataFactory
{
    public static AppState CreateInitialState() => new()
    {
        Raids =
        [
            Raid("echidna-normal", "카제로스 레이드", "서막 : 에키드나", "노말", 1620, 8, 2, 10),
            Raid("echidna-hard", "카제로스 레이드", "서막 : 에키드나", "하드", 1630, 8, 2, 20),
            Raid("behemoth", "에픽 레이드", "베히모스", "노말", 1640, 16, 2, 30),
            Raid("aegir-normal", "카제로스 레이드", "1막 : 에기르", "노말", 1660, 8, 2, 40),
            Raid("aegir-hard", "카제로스 레이드", "1막 : 에기르", "하드", 1680, 8, 2, 50),
            Raid("brel2-normal", "카제로스 레이드", "2막 : 아브렐슈드", "노말", 1670, 8, 2, 60),
            Raid("brel2-hard", "카제로스 레이드", "2막 : 아브렐슈드", "하드", 1690, 8, 2, 70),
            Raid("mordum-normal", "카제로스 레이드", "3막 : 모르둠", "노말", 1680, 8, 3, 80),
            Raid("mordum-hard", "카제로스 레이드", "3막 : 모르둠", "하드", 1700, 8, 3, 90),
            Raid("act4-normal", "카제로스 레이드", "4막 : 파멸의 성채", "노말", 1700, 8, 2, 100),
            Raid("act4-hard", "카제로스 레이드", "4막 : 파멸의 성채", "하드", 1720, 8, 2, 110),
            Raid("final-normal", "카제로스 레이드", "종막 : 최후의 날", "노말", 1710, 8, 2, 120),
            Raid("final-hard", "카제로스 레이드", "종막 : 최후의 날", "하드", 1730, 8, 2, 130),
            Raid("cathedral-1", "어비스 던전", "지평의 성당", "1단계", 1700, 4, 2, 100),
            Raid("cathedral-2", "어비스 던전", "지평의 성당", "2단계", 1720, 4, 2, 120),
            Raid("cathedral-3", "어비스 던전", "지평의 성당", "3단계", 1750, 4, 2, 150),
            Raid("serka-normal", "그림자 레이드", "고통의 마녀, 세르카", "노말", 1710, 4, 2, 120),
            Raid("serka-hard", "그림자 레이드", "고통의 마녀, 세르카", "하드", 1730, 4, 2, 140),
            Raid("serka-nightmare", "그림자 레이드", "고통의 마녀, 세르카", "나이트메어", 1740, 4, 2, 150),
            Raid("belgardin-normal", "그림자 레이드", "죽음의 계율자, 벨가르딘", "노말", 1750, 8, 2, 160),
            Raid("belgardin-hard", "그림자 레이드", "죽음의 계율자, 벨가르딘", "하드", 1770, 8, 2, 170),
            Raid("belgardin-nightmare", "그림자 레이드", "죽음의 계율자, 벨가르딘", "나이트메어", 1780, 8, 2, 180)
        ]
    };

    private static RaidContent Raid(
        string key,
        string category,
        string name,
        string difficulty,
        decimal level,
        int players,
        int gates,
        int priority) => new()
        {
            GroupKey = key.Split('-')[0],
            Category = category,
            Name = name,
            Difficulty = difficulty,
            MinimumItemLevel = level,
            PlayerCount = players,
            PartySize = 4,
            GateCount = gates,
            Priority = priority
        };
}
