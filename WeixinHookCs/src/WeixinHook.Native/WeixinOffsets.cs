namespace WeixinHook.Native;

/// <summary>Weixin.dll 4.1.11.52 offsets (from v4_1_11_52.h)</summary>
internal static class WeixinOffsets
{
    public const string ModuleName = "Weixin.dll";

    public static class Msg
    {
        public const ulong DoAddMsg = 0x179D6EF;
        public const ulong MsgId = 0x148;
        public const ulong Type = 0x0C;
        public const ulong From = 0x18;
        public const ulong Wxid = 0x38;
        public const ulong Content = 0x180;
        public const ulong Timestamp = 0x150;
        public const ulong Signature = 0x58;
    }

    public static class Send
    {
        public const uint GetCoroCtx = 0x00042010;
        public const uint GetMsgSvc = 0x00334780;
        public const uint GetMsgCtx = 0x006C7A80;
        public const uint DoSend = 0x01788AF0;
        public const uint SendEntry = 0x017419E0;
        public const uint MsgCtor = 0x0072E830;

        public const uint Wxid = 0x0B0;
        public const uint Content = 0x708;
        public const uint AtWxid = 0x728;
        public const uint MsgType = 0x0D8;
        public const uint MsgTypeAlt = 0x09C;
        public const uint ObjSize = 0x800;
    }
}
