namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Humanizer
    {
        public static readonly LocString Tab = new("humanizer.tab", "Humanizer");
        public static readonly LocString TabSub = new("humanizer.tabSub", "Short random pauses before the run acts, the way a player reacts.");
        public static readonly LocString Group = new("humanizer.group", "Reaction delays");
        public static readonly LocString Enabled = new("humanizer.enabled", "Humanize reactions");
        public static readonly LocString EnabledHelp = new("humanizer.enabledHelp", "Wait a random moment before each action below, drawn anew each time between the shortest and the longest value. A shortest value above the longest is used as a fixed wait. Off acts at once.");
        public static readonly LocString RangeTo = new("humanizer.rangeTo", "to");
        public static readonly LocString SecondsFormat = new("humanizer.secondsFormat", "%.1f s");
        public static readonly LocString MoveOff = new("humanizer.moveOff", "Moving off");
        public static readonly LocString MoveOffHelp = new("humanizer.moveOffHelp", "Before the run sets off toward its next stop, ahead of any teleport or move.");
        public static readonly LocString Teleport = new("humanizer.teleport", "Casting a teleport");
        public static readonly LocString TeleportHelp = new("humanizer.teleportHelp", "Before each teleport the run casts.");
    }
}
