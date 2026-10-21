using clib.TaskSystem;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using System.Threading.Tasks;
using AutoTask = clib.Services.AutoTask;

namespace AutoShopper.Core.Tasks;

// One movement or teleport run as its own task, so it owns its cancellation source. The parent run can cancel exactly
// this operation, and cancelling fires its registered cleanups instead of leaving it running. It builds on the plain
// task base rather than the library's TaskBase, whose every instance installs two native movement hooks.
internal sealed partial class MoveOp(Func<MoveOp, Task> body) : AutoTask
{
    // The task runner swallows exceptions, so a failed move would otherwise look like a clean arrival.
    public Exception? Fault { get; private set; }

    protected override async Task Execute()
    {
        try
        {
            await body(this);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Fault = exception;
        }
    }

    public Task MoveInZone(Vector3 destination, MovementConfig config, Func<bool>? stopCondition)
        => Navigate(destination, config, stopCondition);

    public Task Teleport(uint territoryId, Vector3 destination, bool allowSameZoneTeleport)
        => TeleportToTerritory(territoryId, destination, allowSameZoneTeleport);

    public Task Aethernet(uint territoryId, Vector3 destination)
        => UseAethernet(territoryId, destination);

    // A straight step with no path search, so it crosses floor the mesh does not have.
    public Task StepTo(Vector3 destination, Func<bool> stopCondition)
        => StepStraight(destination, stopCondition);

    public Task Interact(IGameObject gameObject, Func<bool>? waitUntil, UiSkipOptions skip)
        => InteractWith(gameObject, waitUntil, skip);

    public Task DismountNow() => Dismount();
}
