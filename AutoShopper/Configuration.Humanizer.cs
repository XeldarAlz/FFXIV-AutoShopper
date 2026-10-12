namespace AutoShopper;

public enum HumanAction : byte
{
    MoveOff,
    Teleport,
}

public readonly record struct DelayRange(float MinSeconds, float MaxSeconds);

public sealed partial class Configuration
{
    public bool HumanizerEnabled { get; set; } = true;

    public float MoveOffDelayMinSeconds { get; set; } = 1.0f;

    public float MoveOffDelayMaxSeconds { get; set; } = 3.5f;

    public float TeleportDelayMinSeconds { get; set; } = 0.3f;

    public float TeleportDelayMaxSeconds { get; set; } = 1.5f;

    public DelayRange DelayFor(HumanAction action) => action switch
    {
        HumanAction.MoveOff => new DelayRange(MoveOffDelayMinSeconds, MoveOffDelayMaxSeconds),
        _ => new DelayRange(TeleportDelayMinSeconds, TeleportDelayMaxSeconds),
    };

    public void SetDelay(HumanAction action, DelayRange range)
    {
        switch (action)
        {
            case HumanAction.MoveOff:
                (MoveOffDelayMinSeconds, MoveOffDelayMaxSeconds) = range;
                break;
            default:
                (TeleportDelayMinSeconds, TeleportDelayMaxSeconds) = range;
                break;
        }
    }
}
