using System.IO.MemoryMappedFiles;
using System.Text;

namespace WeixinHook.Demo;

/// <summary>控制台版：同样走共享内存，方便无 UI 调试。</summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("WeixinHook Demo (共享内存)");
        Console.WriteLine("  inject | send <wxid> <text> | pop | listen | status");
        Console.WriteLine();

        if (args.Length == 0)
        {
            Console.WriteLine("示例: WeixinHook.Demo inject");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "inject":
            {
                var dll = WeixinHook.Shared.Injector.ResolveNativeDll();
                var (ok, msg) = WeixinHook.Shared.Injector.Inject(
                    args.Length >= 2 ? args[1] : "WeiXin", dll);
                Console.WriteLine(msg);
                if (ok) Console.WriteLine("DLL=" + dll);
                break;
            }
            case "send" when args.Length >= 3:
            {
                using var c = Open();
                var ok = c.SendText(args[1], string.Join(' ', args.Skip(2)), TimeSpan.FromSeconds(35));
                Console.WriteLine(ok ? "OK" : "FAIL");
                break;
            }
            case "pop":
            {
                using var c = Open();
                var m = c.Pop(TimeSpan.FromSeconds(5));
                Console.WriteLine(m is null ? "(empty)" : m.DisplayLine());
                break;
            }
            case "listen":
            {
                using var c = Open();
                Console.WriteLine("listening...");
                while (true)
                {
                    var m = c.Pop(TimeSpan.FromSeconds(5));
                    if (m is not null) Console.WriteLine(m.DisplayLine());
                    await Task.Delay(300);
                }
            }
            case "status":
            {
                using var c = Open();
                var st = c.Status(TimeSpan.FromSeconds(5));
                Console.WriteLine($"ok={st.Ok} coro={st.CoroTid} recv={st.RecvTotal}");
                break;
            }
            default:
                Console.WriteLine("unknown command");
                break;
        }
    }

    private static WeixinHook.Shared.ShmClient Open()
    {
        if (!WeixinHook.Shared.ShmClient.TryConnect(out var c, out var err) || c is null)
            throw new InvalidOperationException("连接失败: " + err);
        return c;
    }
}
