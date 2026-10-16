using Fenpo.Core.Channel;

namespace Fenpo.Core.Transfers;

/// <summary>
/// Takes the phone's file transfer data connections: TLS connections to the control server port whose first frame was
/// a <c>TransferStreamHeader</c> (protocol/README.md, File transfer).
/// </summary>
public interface ITransferDataConnections
{
    /// <summary>
    /// Moves the bytes of one accepted transfer, in whichever direction it goes. The connection closes when this returns.
    /// </summary>
    /// <param name="device">Phone whose pinned certificate opened the connection.</param>
    /// <param name="transferId">TransferStreamHeader.transfer_id.</param>
    /// <param name="stream">The TLS stream, past the header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the transfer ended.</returns>
    Task HandleAsync(PairedAppDevice device, string transferId, Stream stream, CancellationToken cancellationToken);
}
