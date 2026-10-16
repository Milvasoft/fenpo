namespace Fenpo.VirtualCamera;

/// <summary>
/// The virtual camera's media source: one video stream (<see cref="CameraStream"/>), live, started and stopped by
/// Frame Server for the app that opened "Fenpo Kamera".
/// </summary>
[GeneratedComClass]
public partial class CameraSource :
    IMFAttributes,
    IMFMediaSourceEx,
    IMFMediaSource,
    IMFMediaEventGenerator,
    IMFSampleAllocatorControl,
    IMFGetService,
    IKsControl,
    IDisposable
{
    private readonly Lock _lock = new();
    private readonly MFAttributes _attributes = new(nameof(CameraSource));
    private readonly CameraStream _stream;
    private ComObject<IMFMediaEventQueue>? _queue;
    private ComObject<IMFPresentationDescriptor>? _presentation;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraSource"/> class.
    /// </summary>
    public CameraSource()
    {
        _stream = new CameraStream(this, 0);

        // Camera profiles: the legacy one (up to 30 fps) is what conferencing apps pick.
        Functions.MFCreateSensorProfile(Constants.KSCAMERAPROFILE_Legacy, 0, PWSTR.Null, out var legacy).ThrowOnError();
        legacy.AddProfileFilter(0, PWSTR.From("((RES==;FRT<=30,1;SUT==))")).ThrowOnError();
        Functions.MFCreateSensorProfileCollection(out var profiles).ThrowOnError();
        using var collection = new ComObject<IMFSensorProfileCollection>(profiles);
        collection.Object.AddProfile(legacy).ThrowOnError();
        _attributes.Set(Constants.MF_DEVICEMFT_SENSORPROFILE_COLLECTION, collection);

        _stream.GetStreamDescriptor(out var descriptor).ThrowOnError();
        var pointer = ComObject.GetOrCreateComInstance(descriptor, typeof(IMFStreamDescriptor).GUID);

        try
        {
            unsafe
            {
                var list = stackalloc nint[] { pointer };
                Functions.MFCreatePresentationDescriptor(1, (nint)list, out var presentation).ThrowOnError();
                _presentation = new ComObject<IMFPresentationDescriptor>(presentation);
            }
        }
        finally
        {
            Marshal.Release(pointer);
        }

        Functions.MFCreateEventQueue(out var queue).ThrowOnError();
        _queue = new ComObject<IMFMediaEventQueue>(queue);
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
    public HRESULT CreatePresentationDescriptor(out IMFPresentationDescriptor presentationDescriptor)
    {
        lock (_lock)
        {
            if (_presentation is not { } presentation)
            {
                presentationDescriptor = null!;
                return Constants.MF_E_SHUTDOWN;
            }

            return presentation.Object.Clone(out presentationDescriptor);
        }
    }

    /// <inheritdoc/>
    public HRESULT GetCharacteristics(out uint characteristics)
    {
        characteristics = (uint)MFMEDIASOURCE_CHARACTERISTICS.MFMEDIASOURCE_IS_LIVE;
        return Constants.S_OK;
    }

    /// <inheritdoc/>
    public HRESULT GetSourceAttributes(out IMFAttributes attributes)
    {
        attributes = _attributes;
        return Constants.S_OK;
    }

    /// <inheritdoc/>
    public HRESULT GetStreamAttributes(uint streamIdentifier, out IMFAttributes attributes)
    {
        if (streamIdentifier != 0)
        {
            attributes = null!;
            return Constants.E_FAIL;
        }

        attributes = _stream.Attributes;
        return Constants.S_OK;
    }

    /// <inheritdoc/>
    public HRESULT SetD3DManager(nint manager) => _stream.SetD3DManager(manager);

    /// <inheritdoc/>
    public HRESULT GetAllocatorUsage(uint outputStreamID, out uint inputStreamID, out MFSampleAllocatorUsage usage)
    {
        inputStreamID = outputStreamID;
        usage = MFSampleAllocatorUsage.MFSampleAllocatorUsage_UsesProvidedAllocator;
        return outputStreamID == 0 ? Constants.S_OK : Constants.E_FAIL;
    }

    /// <inheritdoc/>
    public HRESULT SetDefaultAllocator(uint outputStreamID, nint allocator) => outputStreamID == 0 ? _stream.SetAllocator(allocator) : Constants.E_FAIL;

    /// <inheritdoc/>
    public HRESULT Pause() => Constants.MF_E_INVALID_STATE_TRANSITION;

    /// <inheritdoc/>
    public unsafe HRESULT Start(IMFPresentationDescriptor? presentationDescriptor, nint guidTimeFormat, nint startPosition)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(presentationDescriptor);

            if (guidTimeFormat != 0 && *(Guid*)guidTimeFormat != Guid.Empty)
            {
                return Constants.E_INVALIDARG;
            }

            lock (_lock)
            {
                if (_queue is not { } queue || _presentation is not { } presentation)
                {
                    return Constants.MF_E_SHUTDOWN;
                }

                presentationDescriptor.GetStreamDescriptorCount(out var count).ThrowOnError();

                if (count != 1)
                {
                    return Constants.E_INVALIDARG;
                }

                presentationDescriptor.GetStreamDescriptorByIndex(0, out var selected, out var requested).ThrowOnError();
                using var descriptor = new ComObject<IMFStreamDescriptor>(requested);
                _stream.GetStreamState(out var state).ThrowOnError();
                var running = state != MF_STREAM_STATE.MF_STREAM_STATE_STOPPED;

                if (selected && !running)
                {
                    presentation.Object.SelectStream(0).ThrowOnError();
                    ComObject.WithComInstanceOfType<IMFMediaStream>(_stream, unknown =>
                    {
                        queue.Object.QueueEventParamUnk((uint)MF_EVENT_TYPE.MENewStream, Guid.Empty, Constants.S_OK, unknown).ThrowOnError();
                    }, createIfNeeded: true);

                    descriptor.Object.GetMediaTypeHandler(out var handlerObject).ThrowOnError();
                    using var handler = new ComObject<IMFMediaTypeHandler>(handlerObject);
                    handler.Object.GetCurrentMediaType(out var typeObject).ThrowOnError();
                    using var type = new ComObject<IMFMediaType>(typeObject);
                    _stream.Start(type.Object).ThrowOnError();
                }
                else if (!selected && running)
                {
                    presentation.Object.DeselectStream(0).ThrowOnError();
                    _stream.Stop().ThrowOnError();
                }

                using var time = new PropVariant(Functions.MFGetSystemTime());
                var detached = time.Detached;
                queue.Object.QueueEventParamVar((uint)MF_EVENT_TYPE.MESourceStarted, Guid.Empty, Constants.S_OK, (nint)(&detached)).ThrowOnError();
                return Constants.S_OK;
            }
        }
        catch (Exception exception)
        {
            ComServer.Log($"Starting failed: {exception}");
            return exception.HResult;
        }
    }

    /// <inheritdoc/>
    public unsafe HRESULT Stop()
    {
        try
        {
            lock (_lock)
            {
                if (_queue is not { } queue || _presentation is not { } presentation)
                {
                    return Constants.MF_E_SHUTDOWN;
                }

                _stream.Stop().ThrowOnError();
                presentation.Object.DeselectStream(0).ThrowOnError();
                using var time = new PropVariant(Functions.MFGetSystemTime());
                var detached = time.Detached;
                queue.Object.QueueEventParamVar((uint)MF_EVENT_TYPE.MESourceStopped, Guid.Empty, Constants.S_OK, (nint)(&detached)).ThrowOnError();
                return Constants.S_OK;
            }
        }
        catch (Exception exception)
        {
            ComServer.Log($"Stopping failed: {exception}");
            return exception.HResult;
        }
    }

    /// <inheritdoc/>
    public HRESULT Shutdown()
    {
        lock (_lock)
        {
            if (_queue is not { } queue)
            {
                return Constants.MF_E_SHUTDOWN;
            }

            queue.Object.Shutdown().ThrowOnError();
            _attributes.NativeObject.DeleteAllItems();
            _stream.Shutdown();
            return Constants.S_OK;
        }
    }

    /// <inheritdoc/>
    public HRESULT GetService(in Guid guidService, in Guid riid, out nint ppvObject)
    {
        ppvObject = 0;
        return Constants.E_NOINTERFACE;
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
        _stream.Dispose();
        _attributes.Dispose();
        Interlocked.Exchange(ref _queue, null)?.Dispose();
        Interlocked.Exchange(ref _presentation, null)?.Dispose();
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
