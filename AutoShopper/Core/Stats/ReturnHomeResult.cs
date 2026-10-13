namespace AutoShopper.Core.Stats;

// How the way home after a run went, kept apart from how the shopping itself ended.
public enum ReturnHomeResult : byte
{
    NotAsked = 0,
    AlreadyHome = 1,
    // Started but not seen to the end, for example when the plugin unloaded on the way.
    Underway = 2,
    Arrived = 3,
    Failed = 4,
    // The settings keep the route home off.
    Skipped = 5,
}
