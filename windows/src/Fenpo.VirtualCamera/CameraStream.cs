namespace Fenpo.VirtualCamera;

/// <summary>
/// The virtual camera's video stream: NV12 1920x1080 at 30 fps. Every requested sample gets the newest decoded frame
/// from the phone (<see cref="PhoneFrames"/>), or a placeholder while the phone sends nothing.
/// </summary>
[GeneratedComClass]
public partial class CameraStream : IMFAttributes, IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator, IKsControl, IDisposable
{
    /// <summary>Frame width.</summary>
    public const int Width = 1920;

    /// <summary>Frame height.</summary>
    public const int Height = 1080;

    private const uint _allocatorSamples = 10;

    private readonly Lock _lock = new();
    private readonly CameraSource _source;
    private readonly PhoneFrames _frames = new(Width, Height);
    private MFAttributes _attributes = new(nameof(CameraStream));
    private ComObject<IMFMediaEventQueue>? _queue;
    private ComObject<IMFStreamDescriptor>? _descriptor;
    private IComObject<IMFVideoSampleAllocatorEx>? _allocator;
    private MF_STREAM_STATE _state = MF_STREAM_STATE.MF_STREAM_STATE_STOPPED;
    private ulong _delivered;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraStream"/> class.
    /// </summary>
    /// <param name="source">The source it belongs to.</param>
    /// <param name="index">Stream id.</param>
    public CameraStream(CameraSource source, uint index)
    {
        _source = source;
        _attributes.Set(Constants.MF_DEVICESTREAM_STREAM_CATEGORY, Constants.PINNAME_VIDEO_CAPTURE);
        _attributes.Set(Constants.MF_DEVICESTREAM_STREAM_ID, index);
        _attributes.Set(Constants.MF_DEVICESTREAM_FRAMESERVER_SHARED, 1u);
        _attributes.Set(Constants.MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES, (uint)MFFrameSourceTypes.MFFrameSourceTypes_Color);

        Functions.MFCreateEventQueue(out var queue).ThrowOnError();
        _queue = new ComObject<IMFMediaEventQueue>(queue);

        Functions.MFCreateMediaType(out var typeObject).ThrowOnError();
        using var type = new ComObject<IMFMediaType>(typeObject);
        type.Set(Constants.MF_MT_MAJOR_TYPE, Constants.MFMediaType_Video);
        type.Set(Constants.MF_MT_SUBTYPE, Constants.MFVideoFormat_NV12);
        type.SetSize(Constants.MF_MT_FRAME_SIZE, Width, Height);
        type.Set(Constants.MF_MT_DEFAULT_STRIDE, (uint)Width);
        type.Set(Constants.MF_MT_INTERLACE_MODE, (uint)MFVideoInterlaceMode.MFVideoInterlace_Progressive);
        type.Set(Constants.MF_MT_ALL_SAMPLES_INDEPENDENT, 1u);
        type.SetRatio(Constants.MF_MT_FRAME_RATE, 30, 1);
        type.Set(Constants.MF_MT_AVG_BITRATE, (uint)(Width * Height * 12 * 30));
        type.SetRatio(Constants.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);

        var types = new[] { type.Object };
        Functions.MFCreateStreamDescriptor(index, 1, types, out var descriptor).ThrowOnError();
        descriptor.GetMediaTypeHandler(out var handlerObject).ThrowOnError();
        using var handler = new ComObject<IMFMediaTypeHandler>(handlerObject);
        handler.Object.SetCurrentMediaType(type.Object).ThrowOnError();
        _descriptor = new ComObject<IMFStreamDescriptor>(descriptor);
    }

    /// <summary>
    /// Gets the stream's attributes, for the source.
    /// </summary>
    public IMFAttributes Attributes => _attributes;

    /// <summary>
    /// Starts delivering samples.
    /// </summary>
    /// <param name="type">The media type picked; null to resume.</param>
    /// <returns>The result.</returns>
    public HRESULT Start(IMFMediaType? type)
    {
        if (_queue is not { } queue || _allocator is not { } allocator)
        {
            return Constants.MF_E_SHUTDOWN;
        }

        if (type is not null)
        {
            allocator.Object.InitializeSampleAllocator(_allocatorSamples, type).ThrowOnError();
        }

        _frames.Start();
        queue.Object.QueueEventParamVar((uint)MF_EVENT_TYPE.MEStreamStarted, Guid.Empty, Constants.S_OK, 0).ThrowOnError();
        _state = MF_STREAM_STATE.MF_STREAM_STATE_RUNNING;
        return Constants.S_OK;
    }

    /// <summary>
    /// Stops delivering samples.
    /// </summary>
    /// <returns>The result.</returns>
    public HRESULT Stop()
    {
        if (_queue is not { } queue || _allocator is not { } allocator)
        {
            return Constants.MF_E_SHUTDOWN;
        }

        _frames.Stop();
        allocator.Object.UninitializeSampleAllocator();
        queue.Object.QueueEventParamVar((uint)MF_EVENT_TYPE.MEStreamStopped, Guid.Empty, Constants.S_OK, 0).ThrowOnError();
        _state = MF_STREAM_STATE.MF_STREAM_STATE_STOPPED;
        return Constants.S_OK;
    }

    /// <summary>
    /// Ends the stream for good.
    /// </summary>
    public void Shutdown()
    {
        _frames.Stop();
        _queue?.Object.Shutdown();
    }

    /// <summary>
    /// Takes Frame Server's sample allocator.
    /// </summary>
    /// <param name="allocator">IMFVideoSampleAllocatorEx.</param>
    /// <returns>The result.</returns>
    public HRESULT SetAllocator(nint allocator)
    {
        if (allocator == 0)
        {
            return Constants.E_POINTER;
        }

        _allocator = ComObject.FromPointer<IMFVideoSampleAllocatorEx>(allocator);
        return _allocator is null ? Constants.E_NOINTERFACE : Constants.S_OK;
    }

    /// <summary>
    /// Frames are made in system memory: the D3D manager is not given to the allocator.
    /// </summary>
    /// <param name="manager">IMFDXGIDeviceManager.</param>
    /// <returns>S_OK.</returns>
    public HRESULT SetD3DManager(nint manager) => Constants.S_OK;

    /// <inheritdoc/>
    public HRESULT RequestSample(nint token)
    {
        try
        {
            lock (_lock)
            {
                if (_allocator is not { } allocator || _queue is not { } queue)
                {
                    return Constants.MF_E_SHUTDOWN;
                }

                allocator.Object.AllocateSample(out var sampleObject).ThrowOnError();
                using var sample = new ComObject<IMFSample>(sampleObject);
                sample.Object.SetSampleTime(Functions.MFGetSystemTime()).ThrowOnError();
                sample.Object.SetSampleDuration(333333).ThrowOnError();
                sample.Object.GetBufferByIndex(0, out var bufferObject).ThrowOnError();
                using var buffer = new ComObject<IMFMediaBuffer>(bufferObject);
                buffer.WithLock((data, length, _) => _frames.Fill(data, (int)length));
                buffer.Object.SetCurrentLength((uint)(Width * Height * 3 / 2)).ThrowOnError();

                if (token != 0)
                {
                    sample.Object.SetUnknown(Constants.MFSampleExtension_Token, token).ThrowOnError();
                }

                ComObject.WithComInstance(sample, unknown =>
                {
                    queue.Object.QueueEventParamUnk((uint)MF_EVENT_TYPE.MEMediaSample, Guid.Empty, Constants.S_OK, unknown).ThrowOnError();
                });

                // Samples go back to the allocator only when their wrappers are collected.
                if (++_delivered % (_allocatorSamples / 2) == 0)
                {
                    GC.Collect();
                }

                return Constants.S_OK;
            }
        }
        catch (Exception exception)
        {
            ComServer.Log($"Delivering a frame failed: {exception}");
            return exception.HResult;
        }
    }

    /// <inheritdoc/>
    public HRESULT SetStreamState(MF_STREAM_STATE value)
    {
        if (_state == value)
        {
            return Constants.S_OK;
        }

        switch (value)
        {
            case MF_STREAM_STATE.MF_STREAM_STATE_STOPPED:
                return Stop();
            case MF_STREAM_STATE.MF_STREAM_STATE_RUNNING:
                return Start(null);
            case MF_STREAM_STATE.MF_STREAM_STATE_PAUSED when _state == MF_STREAM_STATE.MF_STREAM_STATE_RUNNING:
                _state = value;
                return Constants.S_OK;
            default:
                return Constants.MF_E_INVALID_STATE_TRANSITION;
        }
    }

    /// <inheritdoc/>
    public HRESULT GetStreamState(out MF_STREAM_STATE value)
    {
        value = _state;
        return Constants.S_OK;
    }

    /// <inheritdoc/>
    public HRESULT BeginGetEvent(IMFAsyncCallback callback, nint state)
    {
        lock (_lock)
        {
            return _queue is { } queue ? queue.Object.BeginGetEvent(callback, state) : Constants.MF_E_SHUTDOWN;
        }
    }

    /// <inheritdoc/>
    public HRESULT EndGetEvent(IMFAsyncResult result, out IMFMediaEvent evt)
    {
        lock (_lock)
        {
            if (_queue is not { } queue)
            {
                evt = null!;
                return Constants.MF_E_SHUTDOWN;
            }

            return queue.Object.EndGetEvent(result, out evt);
        }
    }

    /// <inheritdoc/>
    public HRESULT GetEvent(MEDIA_EVENT_GENERATOR_GET_EVENT_FLAGS flags, out IMFMediaEvent evt)
    {
        lock (_lock)
        {
            if (_queue is not { } queue)
            {
                evt = null!;
                return Constants.MF_E_SHUTDOWN;
            }

            return queue.Object.GetEvent((uint)flags, out evt);
        }
    }

    /// <inheritdoc/>
    public HRESULT QueueEvent(uint type, in Guid extendedType, HRESULT hrStatus, nint value)
    {
        lock (_lock)
        {
            return _queue is { } queue ? queue.Object.QueueEventParamVar(type, extendedType, hrStatus, value) : Constants.MF_E_SHUTDOWN;
        }
    }

    /// <inheritdoc/>
    public HRESULT GetMediaSource(out IMFMediaSource mediaSource)
    {
        mediaSource = _source;
        return Constants.S_OK;
    }

    /// <inheritdoc/>
    public HRESULT GetStreamDescriptor(out IMFStreamDescriptor streamDescriptor)
    {
        if (_descriptor is not { } descriptor)
        {
            streamDescriptor = null!;
            return Constants.MF_E_SHUTDOWN;
        }

        streamDescriptor = descriptor.Object;
        return Constants.S_OK;
    }

    /// <inheritdoc/>
    public HRESULT KsEvent(in KSIDENTIFIER Event, uint EventLength, nint EventData, uint DataLength, out uint BytesReturned)
    {
        BytesReturned = 0;
        return HRESULT.FromWin32(WIN32_ERROR.ERROR_SET_NOT_FOUND);
    }

    /// <inheritdoc/>
    public HRESULT KsMethod(in KSIDENTIFIER Method, uint MethodLength, nint MethodData, uint DataLength, out uint BytesReturned)
    {
        BytesReturned = 0;
        return HRESULT.FromWin32(WIN32_ERROR.ERROR_SET_NOT_FOUND);
    }

    /// <inheritdoc/>
    public HRESULT KsProperty(in KSIDENTIFIER Property, uint PropertyLength, nint PropertyData, uint DataLength, out uint BytesReturned)
    {
        BytesReturned = 0;
        return HRESULT.FromWin32(WIN32_ERROR.ERROR_SET_NOT_FOUND);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _frames.Dispose();
        Interlocked.Exchange(ref _attributes!, null)?.Dispose();
        Interlocked.Exchange(ref _descriptor, null)?.Dispose();
        Interlocked.Exchange(ref _queue, null)?.Dispose();
        Interlocked.Exchange(ref _allocator, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    HRESULT IMFAttributes.Compare(IMFAttributes? pTheirs, MF_ATTRIBUTES_MATCH_TYPE MatchType, out BOOL pbResult) => ((IMFAttributes)_attributes).Compare(pTheirs, MatchType, out pbResult);
    HRESULT IMFAttributes.CompareItem(in Guid guidKey, in PROPVARIANT Value, out BOOL pbResult) => ((IMFAttributes)_attributes).CompareItem(guidKey, Value, out pbResult);
    HRESULT IMFAttributes.CopyAllItems(IMFAttributes? pDest) => ((IMFAttributes)_attributes).CopyAllItems(pDest);
    HRESULT IMFAttributes.DeleteAllItems() => ((IMFAttributes)_attributes).DeleteAllItems();
    HRESULT IMFAttributes.DeleteItem(in Guid guidKey) => ((IMFAttributes)_attributes).DeleteItem(guidKey);
    HRESULT IMFAttributes.GetAllocatedBlob(in Guid guidKey, out nint ppBuf, out uint pcbSize) => ((IMFAttributes)_attributes).GetAllocatedBlob(guidKey, out ppBuf, out pcbSize);
    HRESULT IMFAttributes.GetAllocatedString(in Guid guidKey, out PWSTR ppwszValue, out uint pcchLength) => ((IMFAttributes)_attributes).GetAllocatedString(guidKey, out ppwszValue, out pcchLength);
    HRESULT IMFAttributes.GetBlob(in Guid guidKey, nint pBuf, uint cbBufSize, nint pcbBlobSize) => ((IMFAttributes)_attributes).GetBlob(guidKey, pBuf, cbBufSize, pcbBlobSize);
    HRESULT IMFAttributes.GetBlobSize(in Guid guidKey, out uint pcbBlobSize) => ((IMFAttributes)_attributes).GetBlobSize(guidKey, out pcbBlobSize);
    HRESULT IMFAttributes.GetCount(out uint pcItems) => ((IMFAttributes)_attributes).GetCount(out pcItems);
    HRESULT IMFAttributes.GetDouble(in Guid guidKey, out double pfValue) => ((IMFAttributes)_attributes).GetDouble(guidKey, out pfValue);
    HRESULT IMFAttributes.GetGUID(in Guid guidKey, out Guid pguidValue) => ((IMFAttributes)_attributes).GetGUID(guidKey, out pguidValue);
    HRESULT IMFAttributes.GetItem(in Guid guidKey, nint pValue) => ((IMFAttributes)_attributes).GetItem(guidKey, pValue);
    HRESULT IMFAttributes.GetItemByIndex(uint unIndex, out Guid pguidKey, nint pValue) => ((IMFAttributes)_attributes).GetItemByIndex(unIndex, out pguidKey, pValue);
    HRESULT IMFAttributes.GetItemType(in Guid guidKey, out MF_ATTRIBUTE_TYPE pType) => ((IMFAttributes)_attributes).GetItemType(guidKey, out pType);
    HRESULT IMFAttributes.GetString(in Guid guidKey, PWSTR pwszValue, uint cchBufSize, nint pcchLength) => ((IMFAttributes)_attributes).GetString(guidKey, pwszValue, cchBufSize, pcchLength);
    HRESULT IMFAttributes.GetStringLength(in Guid guidKey, out uint pcchLength) => ((IMFAttributes)_attributes).GetStringLength(guidKey, out pcchLength);
    HRESULT IMFAttributes.GetUINT32(in Guid guidKey, out uint punValue) => ((IMFAttributes)_attributes).GetUINT32(guidKey, out punValue);
    HRESULT IMFAttributes.GetUINT64(in Guid guidKey, out ulong punValue) => ((IMFAttributes)_attributes).GetUINT64(guidKey, out punValue);
    HRESULT IMFAttributes.GetUnknown(in Guid guidKey, in Guid riid, out nint ppv) => ((IMFAttributes)_attributes).GetUnknown(guidKey, riid, out ppv);
    HRESULT IMFAttributes.LockStore() => ((IMFAttributes)_attributes).LockStore();
    HRESULT IMFAttributes.SetBlob(in Guid guidKey, nint pBuf, uint cbBufSize) => ((IMFAttributes)_attributes).SetBlob(guidKey, pBuf, cbBufSize);
    HRESULT IMFAttributes.SetDouble(in Guid guidKey, double fValue) => ((IMFAttributes)_attributes).SetDouble(guidKey, fValue);
    HRESULT IMFAttributes.SetGUID(in Guid guidKey, in Guid guidValue) => ((IMFAttributes)_attributes).SetGUID(guidKey, guidValue);
    HRESULT IMFAttributes.SetItem(in Guid guidKey, in PROPVARIANT Value) => ((IMFAttributes)_attributes).SetItem(guidKey, Value);
    HRESULT IMFAttributes.SetString(in Guid guidKey, PWSTR wszValue) => ((IMFAttributes)_attributes).SetString(guidKey, wszValue);
    HRESULT IMFAttributes.SetUINT32(in Guid guidKey, uint unValue) => ((IMFAttributes)_attributes).SetUINT32(guidKey, unValue);
    HRESULT IMFAttributes.SetUINT64(in Guid guidKey, ulong unValue) => ((IMFAttributes)_attributes).SetUINT64(guidKey, unValue);
    HRESULT IMFAttributes.SetUnknown(in Guid guidKey, nint pUnknown) => ((IMFAttributes)_attributes).SetUnknown(guidKey, pUnknown);
    HRESULT IMFAttributes.UnlockStore() => ((IMFAttributes)_attributes).UnlockStore();
}
