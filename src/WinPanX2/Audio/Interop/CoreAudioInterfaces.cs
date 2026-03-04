using System;
using System.Runtime.InteropServices;

namespace WinPanX2.Audio.Interop;

internal enum EDataFlow
{
    eRender,
    eCapture,
    eAll,
    EDataFlow_enum_count
}

internal enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications,
    ERole_enum_count
}

internal enum DEVICE_STATE
{
    ACTIVE = 0x00000001,
    DISABLED = 0x00000002,
    NOTPRESENT = 0x00000004,
    UNPLUGGED = 0x00000008,
    MASK_ALL = 0x0000000F
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    // Slot 3 (after IUnknown)
    int NotImpl1();

    // Slot 4
    [PreserveSig]
    int GetDefaultAudioEndpoint(
        EDataFlow dataFlow,
        ERole role,
        out IMMDevice ppDevice);

    // Slot 5
    [PreserveSig]
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
        out IMMDevice ppDevice);

    // Slot 6
    [PreserveSig]
    int EnumAudioEndpoints(
        EDataFlow dataFlow,
        int dwStateMask,
        out IMMDeviceCollection ppDevices);

    // Slot 7
    [PreserveSig]
    int RegisterEndpointNotificationCallback(
        IMMNotificationClient pClient);

    // Slot 8
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(
        IMMNotificationClient pClient);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    [PreserveSig]
    int GetState(out int pdwState);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-C0A9B8F6D7C3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint pcDevices);

    [PreserveSig]
    int Item(uint nDevice, out IMMDevice ppDevice);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid fmtid;
    public int pid;
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, DEVICE_STATE dwNewState);
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig]
    int GetAudioSessionControl(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);

    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

    [PreserveSig]
    int RegisterSessionNotification(IntPtr SessionNotification);

    [PreserveSig]
    int UnregisterSessionNotification(IntPtr SessionNotification);

    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int SessionCount);

    [PreserveSig]
    int GetSession(int SessionCount, out IAudioSessionControl Session);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid pRetVal);

    [PreserveSig]
    int SetGroupingParam(ref Guid Override, ref Guid EventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr NewNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr NewNotifications);
}

[ComImport]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl methods (must repeat in order)
    int GetState(out AudioSessionState state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
    int GetGroupingParam(out Guid pRetVal);
    int SetGroupingParam(ref Guid Override, ref Guid EventContext);
    int RegisterAudioSessionNotification(IntPtr NewNotifications);
    int UnregisterAudioSessionNotification(IntPtr NewNotifications);

    // IAudioSessionControl2 methods
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetProcessId(out uint pRetVal);

    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference(bool optOut);
}

[ComImport]
[Guid("1C158861-B533-4B30-B1CF-E853E51C59B8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioChannelVolume
{
    [PreserveSig]
    int GetChannelCount(out uint pdwCount);

    [PreserveSig]
    int SetChannelVolume(uint dwIndex, float fLevel, ref Guid EventContext);

    [PreserveSig]
    int GetChannelVolume(uint dwIndex, out float pfLevel);
}
