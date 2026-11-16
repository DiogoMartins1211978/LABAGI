#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;

namespace Tools.Georeferencing
{
    // Call WinRT directly so Unity players need no SDK projection or native plugin.
    internal sealed class WindowsLocationProvider : IDisposable
    {
        const string RuntimeClass = "Windows.Devices.Geolocation.Geolocator";
        IntPtr locator;
        AsyncOperation pending;
        bool requestingAccess;
        bool ownsRuntime;
        public string Status { get; private set; } = "Stopped";
        public LocationOrigin Origin { get; private set; } = LocationOrigin.Unknown;

        public void Start()
        {
            int initialized = RoInitialize(0);
            // Unity may already own an apartment with a different threading model.
            if (initialized < 0 && initialized != unchecked((int)0x80010106))
                Check(initialized, "Initialize Windows Runtime");
            ownsRuntime = initialized >= 0;

            IntPtr name = IntPtr.Zero, factory = IntPtr.Zero;
            try
            {
                Check(WindowsCreateString(RuntimeClass, RuntimeClass.Length, out name), "Create runtime class name");
                var factoryId = new Guid("9A8E7571-2DF5-4591-9F87-EB5FD894E9B7");
                Check(RoGetActivationFactory(name, ref factoryId, out factory), "Create Geolocator access factory");
                Check(Method<GetObject>(factory, 6)(factory, out var operation), "Request Windows location access");
                pending = new AsyncOperation(operation);
                requestingAccess = true;
                Status = "Waiting For Windows Location Permission";
            }
            finally
            {
                Release(factory);
                if (name != IntPtr.Zero) WindowsDeleteString(name);
            }
        }

        public bool TryRead(out GeoPoint point, out double accuracy, out double utcSeconds)
        {
            point = default;
            accuracy = utcSeconds = 0;
            if (pending == null) BeginPositionRequest();
            if (!pending.IsCompleted)
            {
                if (!requestingAccess) Status = ReadLocationStatus();
                return false;
            }

            if (requestingAccess)
            {
                Check(Method<GetInteger>(pending.Value, 8)(pending.Value, out int access), "Read Windows location permission");
                pending.Dispose();
                pending = null;
                if (access == 2) Check(unchecked((int)0x80070005), "Windows location permission");
                if (access != 1)
                    throw new InvalidOperationException($"Windows returned an unspecified location permission result ({access}). "
                        + "Start acquisition on the main thread while the application is in the foreground.");
                requestingAccess = false;
                CreateLocator();
                BeginPositionRequest();
                return false;
            }

            IntPtr position = IntPtr.Zero, coordinate = IntPtr.Zero, withPoint = IntPtr.Zero, geoPoint = IntPtr.Zero;
            try
            {
                Check(Method<GetObject>(pending.Value, 8)(pending.Value, out position), "Read Windows position result");
                Check(Method<GetObject>(position, 6)(position, out coordinate), "Read Windows coordinate");
                withPoint = Query(coordinate, "FEEA0525-D22C-4D46-B527-0B96066FC7DB");
                Check(Method<GetObject>(withPoint, 6)(withPoint, out geoPoint), "Read geographic point");
                Check(Method<GetPosition>(geoPoint, 6)(geoPoint, out BasicPosition value), "Read geographic position");
                Check(Method<GetNumber>(coordinate, 9)(coordinate, out accuracy), "Read location accuracy");
                Check(Method<GetTimestamp>(coordinate, 13)(coordinate, out long timestamp), "Read location timestamp");
                point = new GeoPoint(value.latitude, value.longitude);
                utcSeconds = new DateTimeOffset(DateTime.FromFileTimeUtc(timestamp)).ToUnixTimeMilliseconds() / 1000.0;
                Origin = ReadPositionSource(coordinate);
                Status = "Windows Geolocator; Source: " + Origin;
                return true;
            }
            finally
            {
                Release(geoPoint);
                Release(withPoint);
                Release(coordinate);
                Release(position);
                pending.Dispose();
                pending = null;
            }
        }

        static LocationOrigin ReadPositionSource(IntPtr coordinate)
        {
            // Source metadata is optional and must not prevent a valid position being used.
            var id = new Guid("95E634BE-DBD6-40AC-B8F2-A65C0340D9A6");
            int result = Method<QueryInterface>(coordinate, 0)(coordinate, ref id, out var metadata);
            if (result < 0) return LocationOrigin.Unknown;
            try
            {
                if (Method<GetInteger>(metadata, 6)(metadata, out int source) < 0) return LocationOrigin.Unknown;
                switch (source)
                {
                    case 0: return LocationOrigin.Cellular;
                    case 1: return LocationOrigin.Satellite;
                    case 2: return LocationOrigin.WiFi;
                    case 3: return LocationOrigin.IPAddress;
                    case 5: return LocationOrigin.Default;
                    case 6: return LocationOrigin.Obfuscated;
                    default: return LocationOrigin.Unknown;
                }
            }
            finally { Release(metadata); }
        }

        void CreateLocator()
        {
            IntPtr name = IntPtr.Zero, instance = IntPtr.Zero;
            try
            {
                Check(WindowsCreateString(RuntimeClass, RuntimeClass.Length, out name), "Create runtime class name");
                Check(RoActivateInstance(name, out instance), "Activate Windows Geolocator");
                locator = Query(instance, "A9C3BF62-4524-4989-8AA9-DE019D2E551F");
                // Request the highest available accuracy; the provider reports the actual result.
                Check(Method<SetInteger>(locator, 7)(locator, 1), "Request high location accuracy");
            }
            finally
            {
                Release(instance);
                if (name != IntPtr.Zero) WindowsDeleteString(name);
            }
        }

        void BeginPositionRequest()
        {
            Check(Method<GetObject>(locator, 13)(locator, out var operation), "Start Windows position request");
            pending = new AsyncOperation(operation);
            Status = "Waiting For Windows Position";
        }

        string ReadLocationStatus()
        {
            Check(Method<GetInteger>(locator, 12)(locator, out int state), "Read Windows location status");
            switch (state)
            {
                case 0: return "Windows Location Ready; Waiting For Position";
                case 1: return "Initializing Windows Location";
                case 2: return "Windows Location Has No Data";
                case 3: return "Windows Location Disabled";
                case 4: return "Windows Location Not Initialized";
                case 5: return "Windows Location Not Available On This Device";
                default: return "Unknown Windows Location State: " + state;
            }
        }

        public void Dispose()
        {
            pending?.Dispose();
            pending = null;
            Release(locator);
            locator = IntPtr.Zero;
            if (ownsRuntime) RoUninitialize();
            ownsRuntime = false;
            Status = "Stopped";
        }

        // Poll IAsyncInfo without callbacks, keeping native calls on Unity's main thread.
        sealed class AsyncOperation : IDisposable
        {
            public IntPtr Value { get; private set; }
            IntPtr info;

            public AsyncOperation(IntPtr value)
            {
                Value = value;
                try { info = Query(value, "00000036-0000-0000-C000-000000000046"); }
                catch { Release(value); Value = IntPtr.Zero; throw; }
            }

            public bool IsCompleted
            {
                get
                {
                    Check(Method<GetInteger>(info, 7)(info, out int state), "Read location operation state");
                    if (state == 3)
                    {
                        Check(Method<GetInteger>(info, 8)(info, out int error), "Read location operation error");
                        Check(error, "Windows location operation");
                    }
                    if (state == 2) throw new OperationCanceledException("Windows location operation was cancelled.");
                    return state == 1;
                }
            }

            public void Dispose()
            {
                if (info != IntPtr.Zero)
                {
                    // Cancellation and Close also release an unfinished startup request.
                    Method<OperationAction>(info, 9)(info);
                    Method<OperationAction>(info, 10)(info);
                    Release(info);
                    info = IntPtr.Zero;
                }
                Release(Value);
                Value = IntPtr.Zero;
            }
        }

        static void Check(int result, string operation)
        {
            if (result >= 0) return;
            string detail;
            switch (unchecked((uint)result))
            {
                case 0x80070005:
                    detail = "Access denied. Check Windows Settings > Privacy > Location and desktop app access. A system policy may also block access.";
                    break;
                case 0x800705B4:
                    detail = "Windows could not obtain a position before its location request timed out.";
                    break;
                case 0x80040154:
                case 0x80004001:
                    detail = "Windows Geolocator is not available on this system.";
                    break;
                default:
                    detail = "Windows could not complete the location operation.";
                    break;
            }
            throw new COMException($"{operation} failed (HRESULT 0x{result:X8}). {detail}", result);
        }

        // Interface slots follow the Windows SDK Geolocation ABI.
        static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

        static IntPtr Query(IntPtr instance, string interfaceId)
        {
            var id = new Guid(interfaceId);
            Check(Method<QueryInterface>(instance, 0)(instance, ref id, out var result), "Query Windows location interface");
            return result;
        }

        static void Release(IntPtr instance)
        {
            if (instance != IntPtr.Zero) Method<ReleaseReference>(instance, 2)(instance);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BasicPosition { public double latitude, longitude, altitude; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterface(IntPtr self, ref Guid id, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseReference(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetObject(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetInteger(IntPtr self, out int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetInteger(IntPtr self, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetNumber(IntPtr self, out double value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetTimestamp(IntPtr self, out long value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetPosition(IntPtr self, out BasicPosition value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OperationAction(IntPtr self);

        [DllImport("combase.dll")] static extern int RoInitialize(uint apartment);
        [DllImport("combase.dll")] static extern void RoUninitialize();
        [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int WindowsCreateString(string text, int length, out IntPtr value);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid interfaceId, out IntPtr factory);
        [DllImport("combase.dll")] static extern int RoActivateInstance(IntPtr name, out IntPtr instance);
    }
}
#endif
