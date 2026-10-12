using AutoShopper.Core.Shopping.Model;

namespace AutoShopper;

public sealed partial class Configuration
{
    public List<ShoppingList> Lists { get; set; } = [];

    public Guid SelectedListId { get; set; }
}
