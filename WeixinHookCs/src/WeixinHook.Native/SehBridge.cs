using System.Runtime.InteropServices;

namespace WeixinHook.Native;

internal static class SehBridge
{
    [DllImport("*", EntryPoint = "Seh_Init")]
    public static extern int Init(
        IntPtr pfnGetCoro, IntPtr pfnGetSvc, IntPtr pfnGetCtx, IntPtr pfnDoSend, IntPtr pfnMsgCtor,
        uint offWxid, uint offContent, uint offMsgType, uint offMsgTypeAlt,
        uint objSize,
        IntPtr sendHookAddr, uint sendOrigByte);

    [DllImport("*", EntryPoint = "Seh_Enqueue")]
    public static extern int Enqueue(byte[] wxid, int wxidLen, byte[] content, int contentLen);

    [DllImport("*", EntryPoint = "Seh_Wait")]
    public static extern int Wait(uint timeoutMs);

    [DllImport("*", EntryPoint = "Seh_HasPending")]
    public static extern int HasPending();

    [DllImport("*", EntryPoint = "Seh_IsOurCall")]
    public static extern int IsOurCall();

    [DllImport("*", EntryPoint = "Seh_LastStep")]
    public static extern int LastStep();

    [DllImport("*", EntryPoint = "Seh_SetDrainResume")]
    public static extern void SetDrainResume(ulong resume);

    [DllImport("*", EntryPoint = "Seh_GetDrainResumePtr")]
    public static extern IntPtr GetDrainResumePtr();

    [DllImport("*", EntryPoint = "Seh_SetDrainStub")]
    public static extern void SetDrainStub(IntPtr stub);

    [DllImport("*", EntryPoint = "Seh_GetDrainThunk")]
    public static extern IntPtr GetDrainThunk();

    [DllImport("*", EntryPoint = "Seh_KickCoro")]
    public static extern int KickCoro(uint tid);

    [DllImport("*", EntryPoint = "Seh_ArmHwBp")]
    public static extern int ArmHwBp(uint tid, ulong bpAddr);
}
