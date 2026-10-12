namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Safety
    {
        public static readonly LocString Add = new("safety.add", "Add");
        public static readonly LocString Remove = new("safety.remove", "Remove");
        public static readonly LocString Test = new("safety.test", "Test");
        public static readonly LocString Preview = new("safety.preview", "Preview");

        public static readonly LocString Tab = new("safety.tab", "Safety");
        public static readonly LocString TabSub = new("safety.tabSub", "Detects nearby Game Masters and reacts: stop the run, ping you, or take more drastic action.");

        public static readonly LocString GmAlerts = new("safety.gm.alerts", "Alerts");
        public static readonly LocString GmStopRun = new("safety.gm.stopRun", "Stop the run");
        public static readonly LocString GmStopRunHelp = new("safety.gm.stopRunHelp", "End the run at once when a GM appears nearby. It is recorded as stopped, and nothing runs after it: no way home and no after-run action. Strongly recommended; the other alerts are little use if the run keeps going.");
        public static readonly LocString GmToast = new("safety.gm.toast", "Toast notification");
        public static readonly LocString GmToastHelp = new("safety.gm.toastHelp", "Pop a Dalamud toast: \"GM <name> is nearby!\"");
        public static readonly LocString GmChat = new("safety.gm.chat", "Chat alert");
        public static readonly LocString GmChatHelp = new("safety.gm.chatHelp", "Print a red chat warning into your local log.");
        public static readonly LocString GmSound = new("safety.gm.sound", "Sound beeps");
        public static readonly LocString GmSoundHelp = new("safety.gm.soundHelp", "Plays a series of system beeps through your speakers. Loud enough to grab your attention if you're tabbed away.");
        public static readonly LocString BeepCount = new("safety.gm.beepCount", "Beep count");
        public static readonly LocString BeepCountHelp = new("safety.gm.beepCountHelp", "How many beeps to play in the burst.");
        public static readonly LocString BeepCountFormat = new("safety.gm.beepCountFormat", "%d beeps");
        public static readonly LocString BeepLength = new("safety.gm.beepLength", "Beep length");
        public static readonly LocString BeepLengthHelp = new("safety.gm.beepLengthHelp", "How long each beep lasts.");
        public static readonly LocString BeepLengthFormat = new("safety.gm.beepLengthFormat", "%d ms each");
        public static readonly LocString BeepPitch = new("safety.gm.beepPitch", "Beep pitch");
        public static readonly LocString BeepPitchHelp = new("safety.gm.beepPitchHelp", "Tone frequency of each beep.");
        public static readonly LocString BeepPitchFormat = new("safety.gm.beepPitchFormat", "%d Hz");
        public static readonly LocString GmActions = new("safety.gm.actions", "Actions");
        public static readonly LocString GmCommands = new("safety.gm.commands", "Custom commands");
        public static readonly LocString GmCommandsHelp = new("safety.gm.commandsHelp", "Chat commands to run when a GM is spotted. Useful for things like /logout, /sh stay calm, or a macro.");
        public static readonly LocString GmKill = new("safety.gm.kill", "Kill the game");
        public static readonly LocString GmKillHelp = new("safety.gm.killHelp", "Hard-terminate the game process via /xlkill. The last-resort option; no goodbyes, no cutscene, no logout. You'll get a disconnect.");
        public static readonly LocString NoCommands = new("safety.gm.noCommands", "No commands queued.");
    }
}
