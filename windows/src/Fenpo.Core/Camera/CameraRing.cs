using System.Runtime.InteropServices;

namespace Fenpo.Core.Camera;

/// <summary>
/// The shared memory between Fenpo.exe and Fenpo Kamera's media source (in Windows' Frame Server): Fenpo.exe writes
/// the phone's H.264 frames into a ring of slots, the media source reads and decodes them, and tells Fenpo.exe while
/// an app has the camera open. The media source creates it (only services may create Global names) and lets the
/// signed-in user write. The file is linked into Fenpo.VirtualCamera too, so it uses nothing else from Core.
/// </summary>
/// <remarks>
/// Layout, little-endian: a 4 KiB header (magic, version, the reader's heartbeat and "in use" flag, the writer's next
/// sequence number and heartbeat), then <see cref="SlotCount"/> slots of <see cref="SlotSize"/> bytes: sequence number,
/// presentation time in microseconds, flags, length, data. A slot is published by writing its sequence number last;
/// a reader checks it before and after copying.
/// </remarks>
public static unsafe class CameraRing
{
    /// <summary>The mapping's name.</summary>
    public const string Name = @"Global\FenpoCamera";

    /// <summary>Header bytes before the first slot.</summary>
    public const int HeaderSize = 4096;

    /// <summary>Number of slots.</summary>
    public const int SlotCount = 64;

    /// <summary>Bytes per slot, header included.</summary>
    public const int SlotSize = 512 * 1024;

    /// <summary>Bytes of frame data a slot holds.</summary>
    public const int MaxFrame = SlotSize - SlotHeaderSize;

    /// <summary>The whole mapping.</summary>
    public const long Size = HeaderSize + ((long)SlotCount * SlotSize);

    /// <summary>"FNCM".</summary>
    public const uint Magic = 0x4D434E46;

    /// <summary>Layout version.</summary>
    public const uint Version = 1;

    /// <summary>Frame flag: a key frame, with the codec config in front.</summary>
    public const int KeyFrameFlag = 1;

    private const int SlotHeaderSize = 24;

    /// <summary>
    /// The header at the start of the mapping.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Header
    {
        /// <summary><see cref="Magic"/> once set up.</summary>
        public uint Magic;

        /// <summary><see cref="Version"/>.</summary>
        public uint Version;

        /// <summary>UTC ticks, refreshed by the media source while an app reads frames.</summary>
        public long ReaderHeartbeat;

        /// <summary>1 while an app has the camera open.</summary>
        public int ReaderActive;

        /// <summary>Unused.</summary>
        public int Reserved;

        /// <summary>The sequence number the writer gives its next frame; the newest frame is one less.</summary>
        public long NextSequence;

        /// <summary>UTC ticks, refreshed by Fenpo.exe while the phone sends frames.</summary>
        public long WriterHeartbeat;
    }

    /// <summary>
    /// Sets up a fresh mapping (the media source does this once it created it).
    /// </summary>
    /// <param name="memory">Start of the mapping.</param>
    public static void Initialize(byte* memory)
    {
        var header = (Header*)memory;

        if (header->Magic == Magic && header->Version == Version)
        {
            return;
        }

        new Span<byte>(memory, HeaderSize).Clear();
        header->Version = Version;
        header->NextSequence = 1;
        Volatile.Write(ref header->Magic, Magic);
    }

    /// <summary>
    /// Whether the mapping is set up with this layout.
    /// </summary>
    /// <param name="memory">Start of the mapping.</param>
    /// <returns>True when it can be used.</returns>
    public static bool IsReady(byte* memory) => Volatile.Read(ref ((Header*)memory)->Magic) == Magic && ((Header*)memory)->Version == Version;

    /// <summary>
    /// Writes one frame (writer side).
    /// </summary>
    /// <param name="memory">Start of the mapping.</param>
    /// <param name="frame">Annex-B access unit; a key frame carries the codec config in front.</param>
    /// <param name="ptsMicroseconds">The phone's presentation time.</param>
    /// <param name="keyFrame">Whether it is a key frame.</param>
    /// <returns>False when the frame is too big for a slot.</returns>
    public static bool Write(byte* memory, ReadOnlySpan<byte> frame, long ptsMicroseconds, bool keyFrame)
    {
        if (frame.Length > MaxFrame)
        {
            return false;
        }

        var header = (Header*)memory;
        var sequence = header->NextSequence;
        var slot = Slot(memory, sequence);

        // Unpublish first, so a reader never takes a half-written slot for the old frame.
        Volatile.Write(ref *(long*)slot, 0);
        *(long*)(slot + 8) = ptsMicroseconds;
        *(int*)(slot + 16) = keyFrame ? KeyFrameFlag : 0;
        *(int*)(slot + 20) = frame.Length;
        frame.CopyTo(new Span<byte>(slot + SlotHeaderSize, frame.Length));
        Volatile.Write(ref *(long*)slot, sequence);
        Volatile.Write(ref header->NextSequence, sequence + 1);
        Volatile.Write(ref header->WriterHeartbeat, DateTime.UtcNow.Ticks);
        return true;
    }

    /// <summary>
    /// Reads the frame after <paramref name="last"/> (reader side). When the reader fell more than half the ring
    /// behind, it jumps to the newest key frame, so it decodes from live again instead of from the past.
    /// </summary>
    /// <param name="memory">Start of the mapping.</param>
    /// <param name="last">Sequence number of the last frame read; 0 for none.</param>
    /// <param name="buffer">Receives the frame; at least <see cref="MaxFrame"/> bytes.</param>
    /// <param name="frame">The frame read: its sequence number, time, key flag and length in <paramref name="buffer"/>.</param>
    /// <returns>False when there is no newer frame (or it was overwritten while reading).</returns>
    public static bool TryRead(byte* memory, long last, Span<byte> buffer, out FrameInfo frame)
    {
        frame = default;
        var newest = Volatile.Read(ref ((Header*)memory)->NextSequence) - 1;

        if (newest <= last || newest <= 0)
        {
            return false;
        }

        var wanted = last + 1;

        if (last == 0 || newest - last > SlotCount / 2)
        {
            wanted = NewestKeyFrame(memory, newest);

            if (wanted <= last)
            {
                return false;
            }
        }

        var slot = Slot(memory, wanted);

        if (Volatile.Read(ref *(long*)slot) != wanted)
        {
            return false;
        }

        var pts = *(long*)(slot + 8);
        var flags = *(int*)(slot + 16);
        var length = *(int*)(slot + 20);

        if (length < 0 || length > MaxFrame || length > buffer.Length)
        {
            return false;
        }

        new ReadOnlySpan<byte>(slot + SlotHeaderSize, length).CopyTo(buffer);

        // Overwritten while copying: the writer lapped this slot.
        if (Volatile.Read(ref *(long*)slot) != wanted)
        {
            return false;
        }

        frame = new FrameInfo(wanted, pts, (flags & KeyFrameFlag) != 0, length);
        return true;
    }

    /// <summary>
    /// Marks the camera in use or not, with a fresh heartbeat (reader side).
    /// </summary>
    /// <param name="memory">Start of the mapping.</param>
    /// <param name="active">Whether an app reads frames.</param>
    public static void SetReader(byte* memory, bool active)
    {
        var header = (Header*)memory;
        Volatile.Write(ref header->ReaderHeartbeat, DateTime.UtcNow.Ticks);
        Volatile.Write(ref header->ReaderActive, active ? 1 : 0);
    }

    /// <summary>
    /// Whether an app has the camera open: the reader says so and its heartbeat is fresh (writer side).
    /// </summary>
    /// <param name="memory">Start of the mapping.</param>
    /// <param name="staleAfter">How old a heartbeat may be.</param>
    /// <returns>True while the camera is in use.</returns>
    public static bool ReaderActive(byte* memory, TimeSpan staleAfter)
    {
        var header = (Header*)memory;
        return Volatile.Read(ref header->ReaderActive) == 1 && DateTime.UtcNow.Ticks - Volatile.Read(ref header->ReaderHeartbeat) < staleAfter.Ticks;
    }

    /// <summary>
    /// Whether frames came lately (reader side, to show a placeholder when they stopped).
    /// </summary>
    /// <param name="memory">Start of the mapping.</param>
    /// <param name="staleAfter">How old the last frame may be.</param>
    /// <returns>True while the phone sends.</returns>
    public static bool WriterActive(byte* memory, TimeSpan staleAfter) => DateTime.UtcNow.Ticks - Volatile.Read(ref ((Header*)memory)->WriterHeartbeat) < staleAfter.Ticks;

    private static byte* Slot(byte* memory, long sequence) => memory + HeaderSize + ((sequence % SlotCount) * (long)SlotSize);

    private static long NewestKeyFrame(byte* memory, long newest)
    {
        for (var sequence = newest; sequence > Math.Max(0, newest - (SlotCount / 2)); sequence--)
        {
            var slot = Slot(memory, sequence);

            if (Volatile.Read(ref *(long*)slot) == sequence && (*(int*)(slot + 16) & KeyFrameFlag) != 0)
            {
                return sequence;
            }
        }

        return 0;
    }

    /// <summary>
    /// A frame read from the ring.
    /// </summary>
    /// <param name="Sequence">Its sequence number.</param>
    /// <param name="PtsMicroseconds">The phone's presentation time.</param>
    /// <param name="KeyFrame">Whether it is a key frame.</param>
    /// <param name="Length">Bytes.</param>
    public readonly record struct FrameInfo(long Sequence, long PtsMicroseconds, bool KeyFrame, int Length);
}
