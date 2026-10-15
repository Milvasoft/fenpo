namespace Fenpo.Core.Media;

/// <summary>
/// The phone's media player. iPhone: AMS; Android: the Fenpo channel.
/// </summary>
public interface IMediaSource
{
    /// <summary>
    /// Raised when anything about the current playback changed.
    /// </summary>
    event EventHandler<NowPlaying>? Changed;

    /// <summary>
    /// Gets the latest state.
    /// </summary>
    NowPlaying Current { get; }

    /// <summary>
    /// Sends a remote command.
    /// </summary>
    /// <param name="command">Command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the phone accepted it.</returns>
    Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken = default);
}
