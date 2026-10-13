using AutoShopper.Core.Shopping.Import;

namespace AutoShopper.Core.Exports;

internal interface IExportNames : IItemNames
{
    bool TryGetWorldName(uint worldId, out string name);

    bool TryGetDataCenterName(uint dataCenterId, out string name);
}
