#include <windows.h>

/*
 * Pure-native WeChat send path (mirrors Dll1 msg_send.cpp).
 * MUST NOT call into managed/NativeAOT from the coroutine thread —
 * that deadlocks the GC ("死机").
 */

typedef void (*fn_v_p)(void* a);
typedef void (*fn_v_uq_p)(unsigned __int64 a, void* b);
typedef __int64 (*fn_i_uq_ppi)(unsigned __int64 a, void* b, void* c, int d);
typedef __int64 (*fn_i_i)(__int64 a);

static void MemSet(void* dst, int v, SIZE_T n)
{
    unsigned char* p = (unsigned char*)dst;
    while (n--) *p++ = (unsigned char)v;
}

static void MemCpy(void* dst, const void* src, SIZE_T n)
{
    unsigned char* d = (unsigned char*)dst;
    const unsigned char* s = (const unsigned char*)src;
    while (n--) *d++ = *s++;
}

typedef struct {
    volatile LONG pending;
    volatile LONG done;
    volatile LONG result;
    volatile LONG our_call;
    volatile LONG inited;
    volatile LONG last_step; /* diagnostic: where CallSendOnce stopped */

    HANDLE done_event;

    void* pfn_get_coro;
    void* pfn_get_svc;
    void* pfn_get_ctx;
    void* pfn_dosend;
    void* pfn_msgctor;

    unsigned int off_wxid;
    unsigned int off_content;
    unsigned int off_msgtype;
    unsigned int off_msgtype_alt;
    unsigned int obj_size;

    BYTE* send_hook_addr;
    BYTE send_orig_byte;
    volatile UINT64 drain_resume;
    void* drain_stub; /* set from C# after BuildDrainStub */

    void* fake_vtable[2];

    char wxid[256];
    char content[8192];
    int wxid_len;
    int content_len;
} NativeSendState;

static NativeSendState g;
static void Seh_NoOp(void* p) { (void)p; }

static int Call1(void* fn, void* a1)
{
    __try { ((fn_v_p)fn)(a1); return 1; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
}

static int Call2(void* fn, unsigned __int64 a1, void* a2)
{
    __try { ((fn_v_uq_p)fn)(a1, a2); return 1; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
}

static int CallDoSend(void* fn, unsigned __int64 ctx, void* v8, void* vec, int flag)
{
    __try { ((fn_i_uq_ppi)fn)(ctx, v8, vec, flag); return 1; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
}

static int CallMsgCtor(void* fn, __int64 obj)
{
    __try { ((fn_i_i)fn)(obj); return 1; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
}

static void ReleaseSP(unsigned __int64 ctrl)
{
    if (!ctrl) return;
    __try {
        volatile long* uses = (volatile long*)(ctrl + 8);
        volatile long* weaks = (volatile long*)(ctrl + 12);
        if (_InterlockedDecrement(uses) == 0) {
            void** vptr = *(void***)ctrl;
            ((void(*)(void*))vptr[0])((void*)ctrl);
            if (_InterlockedDecrement(weaks) == 0)
                ((void(*)(void*))vptr[1])((void*)ctrl);
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {}
}

static void AssignString(char* field, const char* utf8, int len)
{
    if (len < 0) len = 0;
    if (len <= 15) {
        if (len > 0) MemCpy(field, utf8, (SIZE_T)len);
        field[len] = 0;
        *(UINT64*)(field + 16) = (UINT64)len;
        *(UINT64*)(field + 24) = 15;
        return;
    }
    {
        SIZE_T cap = (SIZE_T)len | 15;
        char* buf = (char*)HeapAlloc(GetProcessHeap(), 0, cap + 1);
        if (!buf) return;
        MemCpy(buf, utf8, (SIZE_T)len);
        buf[len] = 0;
        *(void**)field = buf;
        *(UINT64*)(field + 16) = (UINT64)len;
        *(UINT64*)(field + 24) = (UINT64)cap;
    }
}

static void PatchByte(BYTE* addr, BYTE val)
{
    DWORD old = 0, tmp = 0;
    if (!VirtualProtect(addr, 1, PAGE_EXECUTE_READWRITE, &old)) return;
    *addr = val;
    VirtualProtect(addr, 1, old, &tmp);
}

static int DirectSendCore(void* vecData, UINT64 outSP[6])
{
    DECLSPEC_ALIGN(16) BYTE sp16[16];
    DECLSPEC_ALIGN(16) BYTE v8[80];

    MemSet(outSP, 0, 6 * sizeof(UINT64));
    InterlockedExchange(&g.our_call, 1);

    InterlockedExchange(&g.last_step, 1);
    MemSet(sp16, 0, sizeof(sp16));
    if (!Call1(g.pfn_get_coro, sp16)) { InterlockedExchange(&g.our_call, 0); return 0; }
    outSP[0] = *(UINT64*)sp16;
    outSP[1] = *(UINT64*)(sp16 + 8);
    if (!outSP[0]) { InterlockedExchange(&g.last_step, 11); InterlockedExchange(&g.our_call, 0); return 0; }

    InterlockedExchange(&g.last_step, 2);
    MemSet(sp16, 0, sizeof(sp16));
    if (!Call2(g.pfn_get_svc, outSP[0], sp16)) { InterlockedExchange(&g.our_call, 0); return 0; }
    outSP[2] = *(UINT64*)sp16;
    outSP[3] = *(UINT64*)(sp16 + 8);
    if (!outSP[2]) { InterlockedExchange(&g.last_step, 21); InterlockedExchange(&g.our_call, 0); return 0; }

    InterlockedExchange(&g.last_step, 3);
    MemSet(sp16, 0, sizeof(sp16));
    if (!Call2(g.pfn_get_ctx, outSP[2], sp16)) { InterlockedExchange(&g.our_call, 0); return 0; }
    outSP[4] = *(UINT64*)sp16;
    outSP[5] = *(UINT64*)(sp16 + 8);
    if (!outSP[4]) { InterlockedExchange(&g.last_step, 31); InterlockedExchange(&g.our_call, 0); return 0; }

    InterlockedExchange(&g.last_step, 4);
    MemSet(v8, 0xAA, 72);
    MemSet(v8 + 72, 0, 8);
    if (!CallDoSend(g.pfn_dosend, outSP[4], v8, vecData, 1)) {
        InterlockedExchange(&g.last_step, 41);
        InterlockedExchange(&g.our_call, 0);
        return 0;
    }

    InterlockedExchange(&g.last_step, 5);
    InterlockedExchange(&g.our_call, 0);
    return 1;
}

static int CallSendOnce(void)
{
    unsigned int objBytes = g.obj_size < 0x1000 ? 0x1000u : g.obj_size;
    SIZE_T fullSize = 0x10 + objBytes + 64;
    BYTE* fullBuf;
    BYTE* ctrl;
    BYTE* obj;
    BYTE* spElem;
    BYTE* vecData;
    UINT64 sps[6];
    int ok;

    fullBuf = (BYTE*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, fullSize);
    if (!fullBuf) return 0;

    ctrl = fullBuf;
    obj = fullBuf + 0x10;
    spElem = fullBuf + 0x10 + objBytes;
    vecData = spElem + 16;

    *(void***)ctrl = g.fake_vtable;
    *(long*)(ctrl + 8) = 10;
    *(long*)(ctrl + 12) = 1;

    InterlockedExchange(&g.last_step, 10);
    if (!CallMsgCtor(g.pfn_msgctor, (__int64)obj)) {
        InterlockedExchange(&g.last_step, 101);
        HeapFree(GetProcessHeap(), 0, fullBuf);
        return 0;
    }

    *(UINT64*)(obj + 0x08) = (UINT64)(uintptr_t)obj;
    *(UINT64*)(obj + 0x10) = (UINT64)(uintptr_t)ctrl;
    *(UINT64*)(obj + g.off_msgtype) = 1;
    *(UINT32*)(obj + g.off_msgtype_alt) = 1;

    AssignString((char*)(obj + g.off_wxid), g.wxid, g.wxid_len);
    AssignString((char*)(obj + g.off_content), g.content, g.content_len);

    *(UINT64*)spElem = (UINT64)(uintptr_t)obj;
    *(UINT64*)(spElem + 8) = (UINT64)(uintptr_t)ctrl;
    *(UINT64*)vecData = (UINT64)(uintptr_t)spElem;
    *(UINT64*)(vecData + 8) = (UINT64)(uintptr_t)spElem + 16;
    *(UINT64*)(vecData + 16) = (UINT64)(uintptr_t)spElem + 16;

    MemSet(sps, 0, sizeof(sps));
    ok = DirectSendCore(vecData, sps);
    ReleaseSP(sps[5]);
    ReleaseSP(sps[3]);
    ReleaseSP(sps[1]);
    return ok;
}

int Seh_Init(
    void* pfnGetCoro, void* pfnGetSvc, void* pfnGetCtx, void* pfnDoSend, void* pfnMsgCtor,
    unsigned int offWxid, unsigned int offContent, unsigned int offMsgType, unsigned int offMsgTypeAlt,
    unsigned int objSize,
    void* sendHookAddr, unsigned int sendOrigByte)
{
    MemSet(&g, 0, sizeof(g));
    g.pfn_get_coro = pfnGetCoro;
    g.pfn_get_svc = pfnGetSvc;
    g.pfn_get_ctx = pfnGetCtx;
    g.pfn_dosend = pfnDoSend;
    g.pfn_msgctor = pfnMsgCtor;
    g.off_wxid = offWxid;
    g.off_content = offContent;
    g.off_msgtype = offMsgType;
    g.off_msgtype_alt = offMsgTypeAlt;
    g.obj_size = objSize;
    g.send_hook_addr = (BYTE*)sendHookAddr;
    g.send_orig_byte = (BYTE)sendOrigByte;
    g.fake_vtable[0] = (void*)&Seh_NoOp;
    g.fake_vtable[1] = (void*)&Seh_NoOp;
    g.done_event = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!g.done_event) return 0;
    InterlockedExchange(&g.inited, 1);
    return 1;
}

int Seh_Enqueue(const char* wxid, int wxidLen, const char* content, int contentLen)
{
    if (!g.inited || !wxid || !content) return 0;
    if (wxidLen < 0) wxidLen = 0;
    if (contentLen < 0) contentLen = 0;
    if (wxidLen >= (int)sizeof(g.wxid)) wxidLen = (int)sizeof(g.wxid) - 1;
    if (contentLen >= (int)sizeof(g.content)) contentLen = (int)sizeof(g.content) - 1;

    MemCpy(g.wxid, wxid, (SIZE_T)wxidLen);
    g.wxid[wxidLen] = 0;
    g.wxid_len = wxidLen;
    MemCpy(g.content, content, (SIZE_T)contentLen);
    g.content[contentLen] = 0;
    g.content_len = contentLen;

    InterlockedExchange(&g.result, 0);
    InterlockedExchange(&g.done, 0);
    ResetEvent(g.done_event);
    InterlockedExchange(&g.pending, 1);
    return 1;
}

int Seh_Wait(unsigned int timeoutMs)
{
    DWORD w;
    if (!g.done_event) return 0;
    w = WaitForSingleObject(g.done_event, timeoutMs);
    if (w != WAIT_OBJECT_0) {
        InterlockedExchange(&g.pending, 0);
        return 0;
    }
    return g.result ? 1 : 0;
}

int Seh_HasPending(void)
{
    return g.pending ? 1 : 0;
}

int Seh_IsOurCall(void)
{
    return g.our_call ? 1 : 0;
}

int Seh_LastStep(void)
{
    return (int)g.last_step;
}

void Seh_SetDrainResume(unsigned __int64 resume)
{
    g.drain_resume = resume;
}

volatile unsigned __int64* Seh_GetDrainResumePtr(void)
{
    return &g.drain_resume;
}

void Seh_SetDrainStub(void* stub)
{
    g.drain_stub = stub;
}

/*
 * x64 CONTEXT must be 16-byte aligned or Get/SetThreadContext silently fails.
 * That was likely why send queued forever with "no reaction".
 */
int Seh_KickCoro(unsigned int tid)
{
    HANDLE h;
    DWORD sus;
    DECLSPEC_ALIGN(16) CONTEXT ctx;
    UINT64 rip;
    void* stub = g.drain_stub;

    if (!tid || !stub) {
        InterlockedExchange(&g.last_step, 200);
        return 0;
    }

    h = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_SET_CONTEXT, FALSE, tid);
    if (!h) {
        InterlockedExchange(&g.last_step, 201);
        return 0;
    }

    sus = SuspendThread(h);
    if (sus == (DWORD)-1) {
        InterlockedExchange(&g.last_step, 202);
        CloseHandle(h);
        return 0;
    }

    MemSet(&ctx, 0, sizeof(ctx));
    ctx.ContextFlags = CONTEXT_CONTROL;
    if (!GetThreadContext(h, &ctx)) {
        InterlockedExchange(&g.last_step, 203);
        ResumeThread(h);
        CloseHandle(h);
        return 0;
    }

    rip = ctx.Rip;
    if (stub && rip >= (UINT64)(uintptr_t)stub && rip < (UINT64)(uintptr_t)stub + 0x1000) {
        ResumeThread(h);
        CloseHandle(h);
        return 1; /* already in stub */
    }

    g.drain_resume = rip;
    if (g.send_hook_addr)
        PatchByte(g.send_hook_addr, g.send_orig_byte);

    ctx.Rip = (DWORD64)(uintptr_t)stub;
    ctx.EFlags &= ~0x100u;
    if (!SetThreadContext(h, &ctx)) {
        InterlockedExchange(&g.last_step, 204);
        ResumeThread(h);
        CloseHandle(h);
        return 0;
    }

    ResumeThread(h);
    CloseHandle(h);
    InterlockedExchange(&g.last_step, 205); /* kick ok, waiting drain */
    return 1;
}

int Seh_ArmHwBp(unsigned int tid, unsigned __int64 bpAddr)
{
    HANDLE h;
    DWORD sus;
    DECLSPEC_ALIGN(16) CONTEXT ctx;

    if (!tid || !bpAddr) return 0;
    h = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_SET_CONTEXT, FALSE, tid);
    if (!h) return 0;

    sus = SuspendThread(h);
    if (sus == (DWORD)-1) { CloseHandle(h); return 0; }

    MemSet(&ctx, 0, sizeof(ctx));
    ctx.ContextFlags = CONTEXT_DEBUG_REGISTERS;
    if (!GetThreadContext(h, &ctx)) {
        ResumeThread(h);
        CloseHandle(h);
        return 0;
    }

    ctx.Dr0 = bpAddr;
    ctx.Dr7 = (ctx.Dr7 & ~0x000F0003ULL) | 0x1;
    if (!SetThreadContext(h, &ctx)) {
        ResumeThread(h);
        CloseHandle(h);
        return 0;
    }

    ResumeThread(h);
    CloseHandle(h);
    InterlockedExchange(&g.last_step, 205); /* armed, waiting GetCoroCtx */
    return 1;
}

void Seh_DrainThunk(void)
{
    int ok = 0;

    if (InterlockedCompareExchange(&g.pending, 0, 1) == 1) {
        __try { ok = CallSendOnce(); }
        __except (EXCEPTION_EXECUTE_HANDLER) { ok = 0; }
        InterlockedExchange(&g.result, ok ? 1 : 0);
        InterlockedExchange(&g.done, 1);
        if (g.done_event) SetEvent(g.done_event);
    }

    if (g.send_hook_addr && g.send_orig_byte &&
        g.drain_resume != (UINT64)(uintptr_t)g.send_hook_addr)
    {
        PatchByte(g.send_hook_addr, 0xCC);
    }
}

void* Seh_GetDrainThunk(void)
{
    return (void*)&Seh_DrainThunk;
}

int Seh_Call1(void* fn, void* a1) { return Call1(fn, a1); }
int Seh_Call2(void* fn, unsigned __int64 a1, void* a2) { return Call2(fn, a1, a2); }
int Seh_CallDoSend(void* fn, unsigned __int64 ctx, void* v8, void* vec, int flag)
{ return CallDoSend(fn, ctx, v8, vec, flag); }
int Seh_CallMsgCtor(void* fn, __int64 obj) { return CallMsgCtor(fn, obj); }
void Seh_ReleaseSP(unsigned __int64 ctrl) { ReleaseSP(ctrl); }
