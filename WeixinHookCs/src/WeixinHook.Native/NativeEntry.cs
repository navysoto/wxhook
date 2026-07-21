using System.Runtime.InteropServices;

namespace WeixinHook.Native;

internal static class NativeEntry
{
    private static volatile bool _initialized;

    [UnmanagedCallersOnly(EntryPoint = "DllMain")]
    public static int DllMain(IntPtr hModule, uint reason, IntPtr reserved)
    {
        if (reason == NativeApi.DllProcessAttach)
        {
            unsafe
            {
                delegate* unmanaged<IntPtr, uint> start = &InitThread;
                NativeApi.CreateThread(IntPtr.Zero, UIntPtr.Zero, (IntPtr)start, IntPtr.Zero, 0, out _);
            }
        }
        return 1;
    }

    [UnmanagedCallersOnly]
    private static uint InitThread(IntPtr _)
    {
        if (_initialized) return 0;
        _initialized = true;

        var baseAddr = WaitWeixinModule();
        if (baseAddr == 0)
            return 1;

        MsgRecvHook.Install(baseAddr);
        MsgSendHook.Initialize(baseAddr);
        ShmServer.Start();

        var recvWorker = new Thread(RecvWorkerLoop)
        {
            IsBackground = true,
            Name = "MsgRecvWorker"
        };
        recvWorker.Start();
        return 0;
    }

    private static void RecvWorkerLoop()
    {
        while (true)
        {
            MsgRecvHook.ProcessPending();
            Thread.Sleep(100);
        }
    }

    private static ulong WaitWeixinModule()
    {
        for (var i = 0; i < 200; i++)
        {
            var h = NativeApi.GetModuleHandleA(WeixinOffsets.ModuleName);
            if (h != IntPtr.Zero)
                return (ulong)h;
            NativeApi.Sleep(50);
        }
        return 0;
    }
}
