namespace LoaSchedule.Domain.Models;

public sealed class AvailabilityWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonId { get; set; }
    public DayOfWeek Day { get; set; }

    // 당일 00:00은 0, 익일 03:00은 1620으로 표현할 수 있습니다.
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}
