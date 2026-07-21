using WeixinHook.Shared;

namespace WeixinHook.UI;

internal sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(245, 246, 248);
    private static readonly Color PanelBg = Color.White;
    private static readonly Color Line = Color.FromArgb(226, 228, 233);
    private static readonly Color TextMuted = Color.FromArgb(110, 118, 130);
    private static readonly Color TextMain = Color.FromArgb(28, 32, 40);
    private static readonly Color Accent = Color.FromArgb(22, 119, 255);

    private readonly WxHookSession _session = new();

    private readonly TextBox _txtProcess = new();
    private readonly Button _btnInject = new();
    private readonly Button _btnConnect = new();
    private readonly Label _lblStatus = new();

    private readonly TextBox _txtWxid = new();
    private readonly TextBox _txtSend = new();
    private readonly Button _btnSend = new();
    private readonly Label _lblHint = new();

    private readonly TextBox _txtRecv = new();
    private readonly Button _btnClear = new();

    public MainForm()
    {
        Text = "WeixinHook Demo";
        Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = Bg;
        MinimumSize = new Size(860, 660);
        Size = new Size(960, 720);
        StartPosition = FormStartPosition.CenterScreen;

        StyleControls();
        BuildLayout();

        _session.MessageReceived += OnMessageReceived;
        _session.Log += s => Ui(() => AppendRecv($"[系统] {s}"));

        _btnInject.Click += async (_, _) => await InjectAsync();
        _btnConnect.Click += async (_, _) => await ConnectAsync();
        _btnSend.Click += async (_, _) => await SendAsync();
        _btnClear.Click += (_, _) => _txtRecv.Clear();

        UpdateStatus("未连接 — 先注入，再连接");
    }

    private void StyleControls()
    {
        _txtProcess.Text = "WeiXin";
        _txtProcess.Width = 160;
        _txtProcess.Height = 32;

        MakePrimary(_btnInject, "注入", 100);
        MakeSecondary(_btnConnect, "连接", 100);
        MakePrimary(_btnSend, "发送消息", 120);
        MakeSecondary(_btnClear, "清空", 88);

        _lblStatus.AutoSize = false;
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = TextMuted;

        _txtWxid.Text = "filehelper";
        _txtWxid.PlaceholderText = "好友 wxid，或群 xxx@chatroom";
        _txtWxid.Dock = DockStyle.Fill;
        _txtWxid.Height = 30;

        _txtSend.Multiline = true;
        _txtSend.ScrollBars = ScrollBars.Vertical;
        _txtSend.Dock = DockStyle.Fill;
        _txtSend.PlaceholderText = "要发送的文本…";

        _lblHint.Text = "注入后稍等，协程线程会自动捕获；状态栏显示协程 ID 后即可发送。";
        _lblHint.AutoSize = true;
        _lblHint.ForeColor = TextMuted;
        _lblHint.Margin = new Padding(14, 8, 0, 0);

        _txtRecv.Multiline = true;
        _txtRecv.ReadOnly = true;
        _txtRecv.ScrollBars = ScrollBars.Vertical;
        _txtRecv.Dock = DockStyle.Fill;
        _txtRecv.BorderStyle = BorderStyle.FixedSingle;
        _txtRecv.BackColor = Color.FromArgb(252, 252, 253);
        _txtRecv.ForeColor = TextMain;
        _txtRecv.Font = new Font("Consolas", 10f);
    }

    private static void MakePrimary(Button btn, string text, int width)
    {
        btn.Text = text;
        btn.Size = new Size(width, 34);
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.BackColor = Accent;
        btn.ForeColor = Color.White;
        btn.Cursor = Cursors.Hand;
        btn.Margin = new Padding(0, 0, 10, 0);
    }

    private static void MakeSecondary(Button btn, string text, int width)
    {
        btn.Text = text;
        btn.Size = new Size(width, 34);
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderColor = Line;
        btn.FlatAppearance.BorderSize = 1;
        btn.BackColor = PanelBg;
        btn.ForeColor = TextMain;
        btn.Cursor = Cursors.Hand;
        btn.Margin = new Padding(0, 0, 10, 0);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Bg,
            Padding = new Padding(20),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        root.Controls.Add(WrapCard(BuildConnectBody()), 0, 0);
        root.Controls.Add(WrapCard(BuildSendBody()), 0, 1);
        root.Controls.Add(WrapCard(BuildRecvBody()), 0, 2);

        Controls.Add(root);
    }

    private static Panel WrapCard(Control body)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelBg,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(18, 14, 18, 14),
        };
        body.Dock = DockStyle.Fill;
        card.Controls.Add(body);
        return card;
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 28,
        Dock = DockStyle.Top,
        Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
        ForeColor = TextMain,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = 56,
        Height = 32,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TextMuted,
        Margin = new Padding(0, 0, 10, 0),
    };

    private Control BuildConnectBody()
    {
        // 三行：标题 / 进程+按钮 / 状态  —— 绝不互相 Dock 叠压
        var box = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
        };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var title = SectionTitle("连接");
        title.Dock = DockStyle.Fill;

        var actionRow = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Dock = DockStyle.Fill,
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        var lblProc = FieldLabel("进程");
        lblProc.Dock = DockStyle.Fill;
        _txtProcess.Dock = DockStyle.Fill;
        _txtProcess.Margin = new Padding(0, 2, 12, 2);
        _btnInject.Dock = DockStyle.Left;
        _btnInject.Margin = new Padding(0, 2, 10, 2);
        _btnConnect.Dock = DockStyle.Left;
        _btnConnect.Margin = new Padding(0, 2, 0, 2);

        actionRow.Controls.Add(lblProc, 0, 0);
        actionRow.Controls.Add(_txtProcess, 1, 0);
        actionRow.Controls.Add(_btnInject, 2, 0);
        actionRow.Controls.Add(_btnConnect, 3, 0);

        var statusRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var lblSt = FieldLabel("状态");
        lblSt.Dock = DockStyle.Fill;
        statusRow.Controls.Add(lblSt, 0, 0);
        statusRow.Controls.Add(_lblStatus, 1, 0);

        box.Controls.Add(title, 0, 0);
        box.Controls.Add(actionRow, 0, 1);
        box.Controls.Add(statusRow, 0, 2);
        return box;
    }

    private Control BuildSendBody()
    {
        var box = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4,
            Dock = DockStyle.Fill,
        };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var title = SectionTitle("发送文本");
        title.Dock = DockStyle.Fill;

        var wxidRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
        };
        wxidRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        wxidRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var lblTo = FieldLabel("接收方");
        lblTo.Dock = DockStyle.Fill;
        wxidRow.Controls.Add(lblTo, 0, 0);
        wxidRow.Controls.Add(_txtWxid, 1, 0);

        var contentRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 0),
        };
        contentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        contentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var lblContent = new Label
        {
            Text = "内容",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(0, 4, 0, 0),
        };
        contentRow.Controls.Add(lblContent, 0, 0);
        contentRow.Controls.Add(_txtSend, 1, 0);

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(56, 4, 0, 0),
        };
        actionRow.Controls.Add(_btnSend);
        actionRow.Controls.Add(_lblHint);

        box.Controls.Add(title, 0, 0);
        box.Controls.Add(wxidRow, 0, 1);
        box.Controls.Add(contentRow, 0, 2);
        box.Controls.Add(actionRow, 0, 3);
        return box;
    }

    private Control BuildRecvBody()
    {
        var box = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
        };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var header = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

        var title = SectionTitle("接收消息");
        title.Dock = DockStyle.Fill;
        _btnClear.Dock = DockStyle.Right;
        _btnClear.Margin = new Padding(0, 0, 0, 2);

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_btnClear, 1, 0);

        box.Controls.Add(header, 0, 0);
        box.Controls.Add(_txtRecv, 0, 1);
        return box;
    }

    private async Task InjectAsync()
    {
        SetBusy(true);
        try
        {
            var dll = Injector.ResolveNativeDll();
            AppendRecv($"[系统] 注入 DLL: {dll}");
            var (ok, msg) = await Task.Run(() => Injector.Inject(_txtProcess.Text.Trim(), dll));
            UpdateStatus(msg);
            if (!ok) return;

            AppendRecv("[系统] 注入成功，等待 DLL 初始化共享内存…");
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(300);
                if (await TryConnectOnceAsync())
                    return;
            }
            AppendRecv("[系统] 自动连接超时，请手动点「连接」");
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message);
            AppendRecv($"[系统] {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConnectAsync()
    {
        SetBusy(true);
        try
        {
            if (!await TryConnectOnceAsync())
                AppendRecv("[系统] 连接失败：确认已注入 WeixinHook.Native.dll");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> TryConnectOnceAsync()
    {
        string error = "";
        var ok = await Task.Run(() => _session.Connect(out error));
        if (!ok)
        {
            UpdateStatus($"未连接: {error}");
            return false;
        }

        _session.StartReceiveLoop(400);
        try
        {
            var st = await _session.GetStatusAsync();
            UpdateStatus(st.CoroTid > 0
                ? $"内存已连  ·  协程={st.CoroTid}  ·  已收={st.RecvTotal}"
                : "内存已连  ·  协程捕获中（使用微信即可自动就绪）");
            AppendRecv("[系统] 收消息轮询已启动");
            return true;
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message);
            return false;
        }
    }

    private async Task SendAsync()
    {
        var wxid = _txtWxid.Text.Trim();
        var text = _txtSend.Text;
        if (string.IsNullOrWhiteSpace(wxid) || string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "请填写接收方和内容", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_session.IsConnected)
        {
            MessageBox.Show(this, "请先注入并连接", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnSend.Enabled = false;
        try
        {
            var (ok, err) = await _session.SendTextAsync(wxid, text);
            AppendRecv(ok
                ? $"[发送成功] -> {wxid}: {text}"
                : $"[发送失败] -> {wxid} ({err})");
            if (ok) _txtSend.Clear();
        }
        catch (Exception ex)
        {
            AppendRecv($"[发送异常] {ex.Message}");
        }
        finally
        {
            _btnSend.Enabled = true;
        }
    }

    private void OnMessageReceived(ReceivedMessage msg) => Ui(() => AppendRecv(msg.DisplayLine()));

    private void AppendRecv(string line)
    {
        if (_txtRecv.TextLength > 0) _txtRecv.AppendText(Environment.NewLine);
        _txtRecv.AppendText(line);
    }

    private void UpdateStatus(string text) => Ui(() => _lblStatus.Text = text);

    private void SetBusy(bool busy) => Ui(() =>
    {
        _btnInject.Enabled = !busy;
        _btnConnect.Enabled = !busy;
    });

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _session.Dispose();
        base.OnFormClosing(e);
    }
}
