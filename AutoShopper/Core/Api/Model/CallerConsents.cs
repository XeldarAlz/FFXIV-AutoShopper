namespace AutoShopper.Core.Api.Model;

[Serializable]
public sealed class CallerConsent
{
    public string Caller { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Allowed { get; set; }
}

internal enum ConsentState : byte
{
    Unknown,
    Allowed,
    Denied,
}

internal readonly record struct ConsentRequest(string Caller, string DisplayName, long MaxSpend);

internal sealed class CallerConsents
{
    private readonly List<CallerConsent> answers;
    private readonly List<ConsentRequest> pending = [];

    public CallerConsents(List<CallerConsent> answers)
    {
        this.answers = answers;
    }

    public IReadOnlyList<CallerConsent> Answers => answers;

    public IReadOnlyList<ConsentRequest> Pending => pending;

    public int Revision { get; private set; }

    public ConsentState StateOf(string caller)
    {
        var index = IndexOfAnswer(caller);
        if (index < 0)
        {
            return ConsentState.Unknown;
        }

        return answers[index].Allowed ? ConsentState.Allowed : ConsentState.Denied;
    }

    public void Ask(string caller, string displayName, long maxSpend)
    {
        if (IndexOfAnswer(caller) >= 0)
        {
            return;
        }

        var request = new ConsentRequest(caller, displayName, maxSpend);
        var index = IndexOfPending(caller);
        if (index >= 0 && pending[index] == request)
        {
            return;
        }

        if (index >= 0)
        {
            pending[index] = request;
        }
        else
        {
            if (pending.Count >= ApiContract.PendingConsentCapacity)
            {
                pending.RemoveAt(0);
            }

            pending.Add(request);
        }

        Revision++;
    }

    public void Allow(string caller, string displayName) => Answer(caller, displayName, allowed: true);

    public void Deny(string caller, string displayName) => Answer(caller, displayName, allowed: false);

    public bool Revoke(string caller)
    {
        var index = IndexOfAnswer(caller);
        if (index < 0)
        {
            return false;
        }

        answers.RemoveAt(index);
        Revision++;
        return true;
    }

    public static bool Repair(List<CallerConsent> answers)
    {
        var changed = false;
        for (var index = answers.Count - 1; index >= 0; index--)
        {
            var answer = answers[index];
            if (answer is null || !ApiRequests.IsCallerName(answer.Caller) || IndexOf(answers, answer.Caller) < index)
            {
                answers.RemoveAt(index);
                changed = true;
                continue;
            }

            var displayName = ApiRequests.DisplayName(answer.Caller, answer.DisplayName);
            if (!string.Equals(displayName, answer.DisplayName, StringComparison.Ordinal))
            {
                answer.DisplayName = displayName;
                changed = true;
            }
        }

        return changed;
    }

    private void Answer(string caller, string displayName, bool allowed)
    {
        var pendingIndex = IndexOfPending(caller);
        if (pendingIndex >= 0)
        {
            pending.RemoveAt(pendingIndex);
        }

        var index = IndexOfAnswer(caller);
        if (index < 0)
        {
            answers.Add(new CallerConsent { Caller = caller, DisplayName = displayName, Allowed = allowed });
        }
        else
        {
            answers[index].DisplayName = displayName;
            answers[index].Allowed = allowed;
        }

        Revision++;
    }

    private int IndexOfAnswer(string caller) => IndexOf(answers, caller);

    private int IndexOfPending(string caller)
    {
        for (var index = 0; index < pending.Count; index++)
        {
            if (string.Equals(pending[index].Caller, caller, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOf(List<CallerConsent> answers, string caller)
    {
        for (var index = 0; index < answers.Count; index++)
        {
            if (answers[index] is { } answer && string.Equals(answer.Caller, caller, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
