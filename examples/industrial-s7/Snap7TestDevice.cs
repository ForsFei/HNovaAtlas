using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace IndustrialS7;

// Local S7 protocol peer only: no PLC scan cycle, I/O hardware or security simulation.
public sealed class Snap7TestDevice : IDisposable
{
    private const string ExpectedHash = "17336585C8F5EBE55D6DB85152BFD64D5456F6C30B26AC862980AF85F725072D";
    private const int DbArea = 5;
    private IntPtr _module, _server;
    private GCHandle _pin;
    private readonly StopServer _stop;
    private readonly DestroyServer _destroy;
    public int Port { get; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateServer();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DestroyServer(ref IntPtr server);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int StopServer(IntPtr server);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RegisterArea(IntPtr server, int area, int index, IntPtr data, int size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetParameter(IntPtr server, int number, ref ushort value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int StartServer(IntPtr server, [MarshalAs(UnmanagedType.LPStr)] string address);

    public Snap7TestDevice()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("本机网络示例当前只准备 Windows x64；可运行 --offline-check。");
        string path = Path.Combine(AppContext.BaseDirectory, "snap7.dll");
        if (!File.Exists(path)) throw new FileNotFoundException("先运行 examples/industrial-s7/prepare-native.ps1，再重新构建。", path);
        using (FileStream source = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(source)) != ExpectedHash)
                throw new InvalidDataException("测试 DLL 哈希不匹配，拒绝加载。");
        _module = NativeLibrary.Load(path);
        _stop = Bind<StopServer>("Srv_Stop");
        _destroy = Bind<DestroyServer>("Srv_Destroy");
        try
        {
            _server = Bind<CreateServer>("Srv_Create")();
            if (_server == IntPtr.Zero) throw new IOException("Snap7 测试服务创建失败。");
            byte[] db = CreateDb();
            _pin = GCHandle.Alloc(db, GCHandleType.Pinned); // Keep registered memory alive until native destruction.
            Check(Bind<RegisterArea>("Srv_RegisterArea")(_server, DbArea, 1, _pin.AddrOfPinnedObject(), db.Length));
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            Port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop(); // Native API requires a port number; binding can still fail, never scan/retry ports.
            ushort localPort = (ushort)Port;
            Check(Bind<SetParameter>("Srv_SetParam")(_server, 1, ref localPort));
            Check(Bind<StartServer>("Srv_StartTo")(_server, "127.0.0.1"));
        }
        catch { Dispose(); throw; }
    }

    public static byte[] CreateDb()
    {
        byte[] db = new byte[64];
        db[0] = 1; db[1] = 2;
        BinaryPrimitives.WriteInt16BigEndian(db.AsSpan(2), -123);
        BinaryPrimitives.WriteInt32BigEndian(db.AsSpan(4), 123456);
        BinaryPrimitives.WriteInt32BigEndian(db.AsSpan(8), BitConverter.SingleToInt32Bits(25.5f));
        BinaryPrimitives.WriteInt16BigEndian(db.AsSpan(12), 250);
        db[14] = 2; // An unrelated flag at bit1 must survive a bit0 write.
        db[16] = 8; db[17] = 7; Encoding.ASCII.GetBytes("ATLAS01").CopyTo(db, 18);
        return db;
    }

    private T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, name));
    private static void Check(int code) { if (code != 0) throw new IOException($"Snap7 test server error 0x{code:X8}。"); }

    public void Dispose()
    {
        int stopCode = 0;
        if (_server != IntPtr.Zero)
        {
            stopCode = _stop(_server);
            _destroy(ref _server); // Stop/destroy native workers before releasing their registered memory.
        }
        if (_pin.IsAllocated) _pin.Free();
        if (_module != IntPtr.Zero) { NativeLibrary.Free(_module); _module = IntPtr.Zero; }
        Check(stopCode);
    }
}
