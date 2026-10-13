namespace AutoShopper;

public sealed partial class Configuration
{
    public const int MaximumWorldRevisits = 3;

    // How many times one run may go back to a world it already shopped when a replan finds the rest there. Zero, the
    // default, never goes back.
    public int WorldRevisits { get; set; }

    private bool RepairRunSettings()
        => Clamp(WorldRevisits, 0, MaximumWorldRevisits, value => WorldRevisits = (int)value);
}
