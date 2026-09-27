namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>V0.4.1 end-turn layer counter. The bottom-card service owns exact pile timing and re-entry guards.</summary>
public sealed class FuelTheFirePower : ImplementedLibrarianPower
{
    private int? _endTurnLayerSnapshot;

    /// <summary>
    /// The shared end-turn coordinator calls this before any delayed card or
    /// bottom-card action. This prevents a bottom BurnTheRiver play from
    /// increasing the work scheduled for the current phase.
    /// </summary>
    public void SnapshotEndTurnLayers() => _endTurnLayerSnapshot = Amount;

}
