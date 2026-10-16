namespace Fenpo.Bluetooth.Link;

/// <summary>
/// Lets one RFCOMM connection to the phone be set up at a time. MAP and PBAP start together when the phone comes in
/// reach, and the iPhone dropped the second of two simultaneous connects ("destination host was down").
/// </summary>
internal static class ClassicConnectGate
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Runs a connect step alone.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="connect">The connect step.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The step's result.</returns>
    public static async Task<T> RunAsync<T>(Func<Task<T>> connect, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await connect();
        }
        finally
        {
            _gate.Release();
        }
    }
}
