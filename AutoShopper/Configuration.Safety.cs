namespace AutoShopper;

public sealed partial class Configuration
{
    public bool GmAlertStopRun { get; set; } = true;
    public bool GmAlertToast { get; set; } = false;
    public bool GmAlertChat { get; set; } = false;
    public bool GmAlertSound { get; set; } = false;
    public int GmAlertBeepCount { get; set; } = 3;
    public int GmAlertBeepDurationMs { get; set; } = 250;
    public int GmAlertBeepFrequencyHz { get; set; } = 900;
    public bool GmAlertKillGame { get; set; } = false;
    public List<string> GmAlertCommands { get; set; } = [];

    // A hand-edited file can carry a null command list or empty commands; both are put right once on load.
    private bool RepairSafetySettings()
    {
        if (GmAlertCommands is null)
        {
            GmAlertCommands = [];
            return true;
        }

        return GmAlertCommands.RemoveAll(string.IsNullOrWhiteSpace) > 0;
    }
}
