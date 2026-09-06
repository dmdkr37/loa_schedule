using LoaSchedule.Domain.Models;

namespace LoaSchedule.Domain.Services;

public sealed class WeeklyRaidProgressService
{
    public void Advance(WeeklyRaidSelection selection, RaidContent raid)
    {
        selection.CompletedGateCount = Math.Clamp(
            selection.CompletedGateCount + 1,
            0,
            raid.GateCount);
    }

    public void Reset(WeeklyRaidSelection selection) => selection.CompletedGateCount = 0;

    public bool IsCompleted(WeeklyRaidSelection selection, RaidContent raid) =>
        raid.GateCount > 0 && selection.CompletedGateCount >= raid.GateCount;
}
