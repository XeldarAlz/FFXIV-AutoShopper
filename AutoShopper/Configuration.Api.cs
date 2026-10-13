using AutoShopper.Core.Api.Model;

namespace AutoShopper;

public sealed partial class Configuration
{
    public List<CallerConsent> ApiCallers { get; set; } = [];

    private bool RepairApiSettings()
    {
        if (ApiCallers is null)
        {
            ApiCallers = [];
            return true;
        }

        return CallerConsents.Repair(ApiCallers);
    }
}
