namespace RunnerTray;

/// <summary>
/// "Sign in with GitHub": shows a one-time code, opens github.com/login/device, and waits.
/// Uses system colours and standard controls, so it works with high contrast, Narrator and the keyboard.
/// </summary>
internal sealed class SignInForm : Form
{
    private readonly Func<CancellationToken, Task<DeviceCode>> _start;
    private readonly Func<DeviceCode, CancellationToken, Task<TokenSet>> _wait;
    private readonly Label _code;
    private readonly Label _status;
    private readonly Button _copyOpen;
    private readonly Button _retry;
    private readonly CancellationTokenSource _cts = new();
    private DeviceCode? _deviceCode;

    public SignInForm(Func<CancellationToken, Task<DeviceCode>> start, Func<DeviceCode, CancellationToken, Task<TokenSet>> wait)
    {
        _start = start;
        _wait = wait;

        Text = "Sign in with GitHub - RunnerTray";
        AccessibleName = "Sign in with GitHub";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 10f * Theme.TextScale);
        Padding = new Padding(20);
        Icon = Icons.Get(TrayState.Busy);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(Theme.Scaled(440), 0),
            Text = "RunnerTray asks GitHub for read-only access to your Actions runs and runners. "
                   + "It never sees your password.",
        };
        var step1 = new Label { AutoSize = true, Text = "1. Copy this code:", Margin = new Padding(0, 14, 0, 4) };
        _code = new Label
        {
            AutoSize = true, Text = "…", Font = new Font("Consolas", 24f * Theme.TextScale, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8), AccessibleRole = AccessibleRole.StaticText,
        };
        var step2 = new Label { AutoSize = true, Text = "2. Paste it on the GitHub page and select Authorize.", Margin = new Padding(0, 4, 0, 10) };
        _copyOpen = new Button { AutoSize = true, Text = "&Copy code and open GitHub", Enabled = false, Padding = new Padding(8, 4, 8, 4) };
        _copyOpen.Click += (_, _) => CopyAndOpen();
        _status = new Label
        {
            AutoSize = true, MaximumSize = new Size(Theme.Scaled(440), 0), Margin = new Padding(0, 14, 0, 8),
            Text = "Getting a code from GitHub…", AccessibleRole = AccessibleRole.StatusBar,
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0) };
        _retry = new Button { AutoSize = true, Text = "&Try again", Visible = false };
        _retry.Click += async (_, _) => await BeginAsync();
        var cancel = new Button { AutoSize = true, Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { _retry, cancel });
        CancelButton = cancel;

        layout.Controls.AddRange(new Control[] { intro, step1, _code, step2, _copyOpen, _status, buttons });
        Controls.Add(layout);
    }

    /// <summary>The tokens after a successful sign-in.</summary>
    public TokenSet? Result { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await BeginAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnFormClosed(e);
    }

    private async Task BeginAsync()
    {
        _retry.Visible = false;
        _copyOpen.Enabled = false;
        _code.Text = "…";
        SetStatus("Getting a code from GitHub…");
        try
        {
            _deviceCode = await _start(_cts.Token);
            _code.Text = _deviceCode.UserCode;
            _code.AccessibleName = "Code: " + string.Join(" ", _deviceCode.UserCode.ToCharArray());
            _copyOpen.Enabled = true;
            _copyOpen.Focus();
            SetStatus($"Waiting for you to approve on GitHub. The code works for {(int)_deviceCode.ExpiresIn.TotalMinutes} minutes.");

            Result = await _wait(_deviceCode, _cts.Token);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            // Dialog closed.
        }
        catch (DeviceFlowException ex)
        {
            Log.Warn($"Sign-in failed: {ex.Error}");
            SetStatus(ex.Message);
            _retry.Visible = true;
            _retry.Focus();
        }
    }

    private void CopyAndOpen()
    {
        if (_deviceCode is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_deviceCode.UserCode);
            SetStatus("Code copied. Paste it on the GitHub page, then select Authorize.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("Could not copy the code. Type it on the GitHub page.");
        }

        Proc.Open(_deviceCode.VerificationUri);
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        // Screen readers announce the new status (WCAG 4.1.3 Status messages).
        _status.AccessibilityObject.RaiseAutomationNotification(
            System.Windows.Forms.Automation.AutomationNotificationKind.Other,
            System.Windows.Forms.Automation.AutomationNotificationProcessing.ImportantMostRecent, text);
    }
}
