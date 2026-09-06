namespace LoaSchedule.Domain.Services;

public sealed class GameWeekService
{
    private const DayOfWeek ResetDay = DayOfWeek.Wednesday;
    private static readonly TimeSpan ResetTime = TimeSpan.FromHours(6);

    public DateTime GetWeekStart(DateTime localDateTime)
    {
        var daysSinceResetDay = ((int)localDateTime.DayOfWeek - (int)ResetDay + 7) % 7;
        var candidate = localDateTime.Date.AddDays(-daysSinceResetDay).Add(ResetTime);

        return localDateTime < candidate ? candidate.AddDays(-7) : candidate;
    }
}
