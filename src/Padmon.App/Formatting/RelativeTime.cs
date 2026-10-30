using System.Globalization;
using Padmon.Core.History;

namespace Padmon.Formatting;

public static class RelativeTime
{
    public static string Format(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var delta = DateTimeOffset.Now - local;

        if (delta < TimeSpan.Zero)
        {
            return "just now";
        }

        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)delta.TotalMinutes);
            return $"{minutes}m ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)delta.TotalHours);
            return $"{hours}h ago";
        }

        if (delta < TimeSpan.FromDays(7))
        {
            var days = Math.Max(1, (int)delta.TotalDays);
            return $"{days}d ago";
        }

        return local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    public static string Date(DateTimeOffset value) =>
        value.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public static string SessionDuration(ConnectionSession session)
    {
        var end = session.DisconnectedAt ?? DateTimeOffset.UtcNow;
        var duration = end - session.ConnectedAt;

        if (duration < TimeSpan.FromMinutes(1))
        {
            return "under a minute";
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)duration.TotalMinutes)} min";
        }

        if (duration < TimeSpan.FromDays(1))
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{(int)duration.TotalDays}d {duration.Hours}h";
    }
}
