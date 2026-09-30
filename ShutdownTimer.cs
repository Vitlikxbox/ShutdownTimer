using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class ShutdownTimer : Form
{
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    [DllImport("user32.dll")] static extern bool LockWorkStation();

    static readonly string[] Actions = { "Выключить компьютер", "Перезагрузить", "Спящий режим", "Гибернация", "Заблокировать", "Выйти из системы" };

    ComboBox actionBox;
    RadioButton afterRadio, atRadio;
    NumericUpDown hoursBox, minutesBox;
    DateTimePicker atPicker;
    Label countdownLabel, targetLabel;
    Button startButton;
    NotifyIcon tray;
    Timer timer;
    DateTime? target;
    bool warned;

    ShutdownTimer()
    {
        Text = "ShutdownTimer";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(340, 330);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(new Label { Text = "Что сделать:", Location = new Point(15, 17), AutoSize = true });
        actionBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(120, 14), Width = 205 };
        actionBox.Items.AddRange(Actions);
        actionBox.SelectedIndex = 0;
        Controls.Add(actionBox);

        afterRadio = new RadioButton { Text = "Через", Location = new Point(15, 58), AutoSize = true, Checked = true };
        Controls.Add(afterRadio);
        hoursBox = new NumericUpDown { Location = new Point(90, 56), Width = 55, Maximum = 99 };
        Controls.Add(hoursBox);
        Controls.Add(new Label { Text = "ч", Location = new Point(148, 59), AutoSize = true });
        minutesBox = new NumericUpDown { Location = new Point(170, 56), Width = 55, Maximum = 59, Value = 30 };
        Controls.Add(minutesBox);
        Controls.Add(new Label { Text = "мин", Location = new Point(228, 59), AutoSize = true });

        atRadio = new RadioButton { Text = "В", Location = new Point(15, 96), AutoSize = true };
        Controls.Add(atRadio);
        atPicker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(90, 94), Width = 80, Value = DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1) };
        Controls.Add(atPicker);
        EventHandler pickAt = (s, e) => atRadio.Checked = true, pickAfter = (s, e) => afterRadio.Checked = true;
        atPicker.ValueChanged += pickAt;
        hoursBox.ValueChanged += pickAfter;
        minutesBox.ValueChanged += pickAfter;

        var quick = new FlowLayoutPanel { Location = new Point(12, 132), Size = new Size(320, 36) };
        foreach (int m in new[] { 15, 30, 60, 90, 120 })
        {
            int minutes = m;
            var b = new Button { Text = m < 60 ? m + " мин" : (m / 60.0).ToString("0.#") + " ч", Size = new Size(58, 28), Font = new Font("Segoe UI", 9) };
            b.Click += (s, e) => { hoursBox.Value = minutes / 60; minutesBox.Value = minutes % 60; afterRadio.Checked = true; };
            quick.Controls.Add(b);
        }
        Controls.Add(quick);

        countdownLabel = new Label { Location = new Point(12, 176), Size = new Size(316, 50), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Color.Gray, Text = "--:--:--" };
        Controls.Add(countdownLabel);
        targetLabel = new Label { Location = new Point(12, 228), Size = new Size(316, 22), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray, Text = "Таймер не запущен" };
        Controls.Add(targetLabel);

        startButton = new Button { Text = "Запустить", Location = new Point(15, 266), Size = new Size(310, 44) };
        startButton.Click += (s, e) => { if (target.HasValue) Stop(); else Start(); };
        Controls.Add(startButton);
        AcceptButton = startButton;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Показать окно", null, (s, e) => ShowWindow());
        menu.Items.Add("Отменить таймер", null, (s, e) => Stop());
        menu.Items.Add("Выход", null, (s, e) => Close());
        tray = new NotifyIcon { Icon = SystemIcons.Information, Visible = true, ContextMenuStrip = menu, Text = "ShutdownTimer" };
        tray.DoubleClick += (s, e) => ShowWindow();

        timer = new Timer { Interval = 500 };
        timer.Tick += (s, e) => Tick();
        Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Hide(); };
    }

    void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void Start()
    {
        DateTime t;
        if (afterRadio.Checked)
        {
            int minutes = (int)hoursBox.Value * 60 + (int)minutesBox.Value;
            if (minutes == 0) { MessageBox.Show(this, "Укажи время больше нуля", Text); return; }
            t = DateTime.Now.AddMinutes(minutes);
        }
        else
        {
            t = DateTime.Now.Date.Add(atPicker.Value.TimeOfDay);
            t = t.AddSeconds(-t.Second);
            if (t <= DateTime.Now) t = t.AddDays(1);
        }
        target = t;
        warned = false;
        SetInputs(false);
        startButton.Text = "Отменить";
        targetLabel.Text = Actions[actionBox.SelectedIndex] + " в " + t.ToString(t.Date == DateTime.Today ? "HH:mm" : "HH:mm, d MMM");
        timer.Start();
        Tick();
    }

    void Stop()
    {
        target = null;
        timer.Stop();
        SetInputs(true);
        startButton.Text = "Запустить";
        countdownLabel.Text = "--:--:--";
        countdownLabel.ForeColor = Color.Gray;
        targetLabel.Text = "Таймер не запущен";
        tray.Text = "ShutdownTimer";
    }

    void SetInputs(bool enabled)
    {
        actionBox.Enabled = afterRadio.Enabled = atRadio.Enabled = hoursBox.Enabled = minutesBox.Enabled = atPicker.Enabled = enabled;
        foreach (Control c in Controls) if (c is FlowLayoutPanel) c.Enabled = enabled;
    }

    void Tick()
    {
        if (!target.HasValue) return;
        var left = target.Value - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            int action = actionBox.SelectedIndex;
            Stop();
            Execute(action);
            return;
        }
        countdownLabel.Text = ((int)left.TotalHours).ToString("00") + left.ToString(@"\:mm\:ss");
        countdownLabel.ForeColor = left.TotalSeconds <= 60 ? Color.Firebrick : Color.FromArgb(40, 90, 160);
        tray.Text = "ShutdownTimer: осталось " + countdownLabel.Text;
        if (!warned && left.TotalSeconds <= 60)
        {
            warned = true;
            tray.ShowBalloonTip(10000, "Меньше минуты", Actions[actionBox.SelectedIndex] + ". Открой окно, чтобы отменить", ToolTipIcon.Warning);
        }
    }

    static void Execute(int action)
    {
        switch (action)
        {
            case 0: Run("shutdown", "/s /t 0"); break;
            case 1: Run("shutdown", "/r /t 0"); break;
            case 2: SetSuspendState(false, false, false); break;
            case 3: SetSuspendState(true, false, false); break;
            case 4: LockWorkStation(); break;
            case 5: Run("shutdown", "/l"); break;
        }
    }

    static void Run(string file, string args)
    {
        Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (target.HasValue && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, "Таймер запущен. Закрыть программу и отменить его?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosing(e);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new ShutdownTimer());
    }
}
