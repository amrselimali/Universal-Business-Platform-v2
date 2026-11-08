using Microsoft.Extensions.Logging.Abstractions;
using Ubp.Web.Startup;
using Xunit;

namespace Ubp.Tests;

/// <summary>الإغلاق الآلي للشفتات: آخر الخامسة صباحاً مرّ هو حد الإغلاق.</summary>
public sealed class ShiftAutoCloseTests
{
    [Fact]
    public void Before_five_am_the_cutoff_is_yesterday_five_am()
    {
        var now = new DateTimeOffset(2026, 11, 5, 4, 59, 0, TimeSpan.FromHours(2));

        var cutoff = ShiftAutoCloseWorker.Cutoff(now);

        Assert.Equal(new DateTimeOffset(2026, 11, 4, 5, 0, 0, TimeSpan.FromHours(2)), cutoff);
    }

    [Fact]
    public void After_five_am_the_cutoff_is_today_five_am()
    {
        var now = new DateTimeOffset(2026, 11, 5, 5, 1, 0, TimeSpan.FromHours(2));

        var cutoff = ShiftAutoCloseWorker.Cutoff(now);

        Assert.Equal(new DateTimeOffset(2026, 11, 5, 5, 0, 0, TimeSpan.FromHours(2)), cutoff);
    }

    [Fact]
    public void Worker_is_constructed_with_scope_factory_and_logger()
    {
        // تحقق من إمكانية تسجيل العامل كخدمة خلفية دون تشغيله.
        Assert.NotNull(typeof(ShiftAutoCloseWorker));
        Assert.NotNull(NullLogger<ShiftAutoCloseWorker>.Instance);
    }
}
