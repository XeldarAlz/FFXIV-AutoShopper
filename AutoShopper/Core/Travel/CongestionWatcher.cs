using System.Threading;
using Dalamud.Game.Chat;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Travel;

// Notes when the game says a world is congested, the LogMessage rows 9419, 9426 and 9427 of game data 2026.09.15, so a
// world change under way can tell that its target turned it away.
internal sealed class CongestionWatcher : IDisposable
{
    private static readonly uint[] congestionMessageIds = [9419, 9426, 9427];

    private long lastSeenAtMs;

    public CongestionWatcher()
    {
        Svc.Chat.LogMessage += OnLogMessage;
    }

    public void Dispose()
    {
        Svc.Chat.LogMessage -= OnLogMessage;
    }

    public bool SeenSince(long sinceMs)
    {
        var seenAt = Interlocked.Read(ref lastSeenAtMs);
        return seenAt != 0 && seenAt >= sinceMs;
    }

    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            var logMessageId = message.LogMessageId;
            if (Array.IndexOf(congestionMessageIds, logMessageId) < 0)
            {
                return;
            }

            Interlocked.Exchange(ref lastSeenAtMs, Environment.TickCount64);
            RunLog.Info($"Travel: the game reported a congested world (log message {logMessageId}).");
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Travel: handling a log message failed");
        }
    }
}
