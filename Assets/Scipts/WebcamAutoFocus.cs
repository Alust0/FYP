using System;
using System.Runtime.InteropServices;
using UnityEngine;

/* Switches PC webcams to continuous autofocus through the webcam driver (DirectShow).
 Vuforia's SetFocusMode only reaches phone cameras; on a PC the webcam stays in whatever
 focus mode its driver was left in, often manual.

 COM objects are called through raw interface pointers because Unity's Mono returns null
 for [ComImport]-wrapped device objects. */
public static class WebcamAutoFocus
{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    const int CameraControlFocus = 6;
    const int FlagAuto = 1;
    const int VariantTypeString = 8;

    // Slot numbers of the methods used, counted from the start of each COM interface
    const int SlotRelease = 2;
    const int SlotQueryInterface = 0;
    const int SlotCreateClassEnumerator = 3; // ICreateDevEnum
    const int SlotNext = 3;                  // IEnumMoniker
    const int SlotBindToObject = 8;          // IMoniker
    const int SlotBindToStorage = 9;         // IMoniker
    const int SlotRead = 3;                  // IPropertyBag
    const int SlotGetRange = 3;              // IAMCameraControl
    const int SlotSet = 4;                   // IAMCameraControl
    const int SlotGet = 5;                   // IAMCameraControl

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid classId, IntPtr outer, int context, ref Guid interfaceId, out IntPtr instance);

    [DllImport("oleaut32.dll")]
    static extern int VariantClear(IntPtr variant);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ReleaseFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int QueryInterfaceFn(IntPtr self, ref Guid interfaceId, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateClassEnumeratorFn(IntPtr self, ref Guid category, out IntPtr enumerator, int flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int NextFn(IntPtr self, int count, out IntPtr moniker, IntPtr fetched);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int BindFn(IntPtr self, IntPtr bindContext, IntPtr monikerToLeft, ref Guid interfaceId, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ReadFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr variant, IntPtr errorLog);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetRangeFn(IntPtr self, int property, out int min, out int max, out int step, out int def, out int caps);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetFn(IntPtr self, int property, int value, int flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetFn(IntPtr self, int property, out int value, out int flags);

    /* Applies to every webcam that reports autofocus support: DirectShow names don't match
     the device Unity picked (the Razer Kiyo X shows as "USB Video Device"), and fixed-focus
     or virtual cameras report no support anyway.

     onlyCamera limits it to the webcam with that name. Opening a camera's driver here disturbs
     another program streaming from it, e.g. the other window when two editor windows each use
     their own camera (CameraChoice); there it blacked out the other window's picture. */
    public static void Enable(string onlyCamera = null)
    {
        /* 0 or 1 means COM was started (or already running) on this thread and needs a matching
         CoUninitialize; anything else means it's running in another mode and still usable. */
        int comInit = CoInitializeEx(IntPtr.Zero, 2);
        IntPtr deviceEnum = IntPtr.Zero;
        IntPtr monikers = IntPtr.Zero;

        try
        {
            Guid systemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
            Guid createDevEnumId = new Guid("29840822-5B84-11D0-BD3B-00A0C911CE86");
            Guid videoInputCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");

            if (CoCreateInstance(ref systemDeviceEnum, IntPtr.Zero, 1, ref createDevEnumId, out deviceEnum) != 0)
            {
                Debug.LogWarning("[CameraDiag] Webcam autofocus unavailable: could not list webcams.");
                return;
            }

            if (Method<CreateClassEnumeratorFn>(deviceEnum, SlotCreateClassEnumerator)(deviceEnum, ref videoInputCategory, out monikers, 0) != 0 || monikers == IntPtr.Zero)
            {
                Debug.Log("[CameraDiag] Webcam autofocus: no webcams found.");
                return;
            }

            NextFn next = Method<NextFn>(monikers, SlotNext);
            while (next(monikers, 1, out IntPtr moniker, IntPtr.Zero) == 0 && moniker != IntPtr.Zero)
            {
                try
                {
                    string cameraName = ReadCameraName(moniker);
                    if (onlyCamera == null || cameraName == onlyCamera) EnableOn(moniker, cameraName);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CameraDiag] Webcam autofocus failed: " + e.Message);
                }
                finally
                {
                    Release(moniker);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CameraDiag] Webcam autofocus unavailable: " + e.Message);
        }
        finally
        {
            Release(monikers);
            Release(deviceEnum);
            if (comInit == 0 || comInit == 1)
                CoUninitialize();
        }
    }

    static void EnableOn(IntPtr moniker, string cameraName)
    {
        Guid baseFilterId = new Guid("56a86895-0ad4-11ce-b03a-0020af0ba770");
        Guid cameraControlId = new Guid("C6E13370-30AC-11d0-A18C-00A0C9118956");

        IntPtr filter = IntPtr.Zero;
        IntPtr control = IntPtr.Zero;

        try
        {
            bool hasControl =
                Method<BindFn>(moniker, SlotBindToObject)(moniker, IntPtr.Zero, IntPtr.Zero, ref baseFilterId, out filter) == 0 && filter != IntPtr.Zero &&
                Method<QueryInterfaceFn>(filter, SlotQueryInterface)(filter, ref cameraControlId, out control) == 0 && control != IntPtr.Zero;

            if (!hasControl || Method<GetRangeFn>(control, SlotGetRange)(control, CameraControlFocus, out _, out _, out _, out _, out int caps) != 0 || (caps & FlagAuto) == 0)
            {
                Debug.Log($"[CameraDiag] Webcam autofocus: '{cameraName}' has no autofocus (fixed-focus or virtual camera).");
                return;
            }

            Method<GetFn>(control, SlotGet)(control, CameraControlFocus, out int focusValue, out _);
            bool autoSet = Method<SetFn>(control, SlotSet)(control, CameraControlFocus, focusValue, FlagAuto) == 0;
            Debug.Log($"[CameraDiag] Webcam autofocus on '{cameraName}' returned: {autoSet}");
        }
        finally
        {
            Release(control);
            Release(filter);
        }
    }

    static string ReadCameraName(IntPtr moniker)
    {
        Guid propertyBagId = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
        string cameraName = "Unknown webcam";

        if (Method<BindFn>(moniker, SlotBindToStorage)(moniker, IntPtr.Zero, IntPtr.Zero, ref propertyBagId, out IntPtr bag) != 0 || bag == IntPtr.Zero)
            return cameraName;

        // A VARIANT is 24 bytes on 64-bit: the type tag at offset 0, the string pointer at offset 8
        IntPtr variant = Marshal.AllocCoTaskMem(24);
        try
        {
            for (int i = 0; i < 24; i++)
                Marshal.WriteByte(variant, i, 0);

            if (Method<ReadFn>(bag, SlotRead)(bag, "FriendlyName", variant, IntPtr.Zero) == 0 && Marshal.ReadInt16(variant) == VariantTypeString)
                cameraName = Marshal.PtrToStringBSTR(Marshal.ReadIntPtr(variant, 8));

            VariantClear(variant);
        }
        finally
        {
            Marshal.FreeCoTaskMem(variant);
            Release(bag);
        }

        return cameraName;
    }

    static T Method<T>(IntPtr comObject, int slot) where T : Delegate
    {
        IntPtr methodTable = Marshal.ReadIntPtr(comObject);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(methodTable, slot * IntPtr.Size));
    }

    static void Release(IntPtr comObject)
    {
        if (comObject != IntPtr.Zero)
            Method<ReleaseFn>(comObject, SlotRelease)(comObject);
    }
#else
    public static void Enable(string onlyCamera = null) { }
#endif
}
