using PADLOck.Services;

namespace PADLOck.UI;

// Окно скачивания обновления: что ставится, что нового, прогресс в процентах и мегабайтах
public sealed class UpdateDialog : DarkDialog
{
    private readonly UpdateService _service;
    private readonly UpdateAsset _asset;
    private readonly string _destination;
    private readonly CancellationTokenSource _cancel = new();
    private readonly ProgressLine _bar = new() { Dock = DockStyle.Top, Height = 14, Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0) };
    private readonly Label _percent = new() { AutoSize = true, ForeColor = Color.White, Font = Theme.SectionFont, Margin = new Padding(0, 10, 0, 0) };
    private readonly Button _button = Theme.CreateButton("Отмена", ButtonKind.Secondary);
    private readonly DateTime _started = DateTime.UtcNow;
    private bool _finished;

    public UpdateDialog(string title, string subtitle, string notes, UpdateAsset asset, string destination, UpdateService service)
    {
        _service = service;
        _asset = asset;
        _destination = destination;
        Forms.StyleDialog(this, title, new Size(600, notes.Trim().Length > 0 ? 470 : 250));
        ControlBox = false;

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(22, 18, 22, 8),
            BackColor = Theme.BaseBottom
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control c, SizeType type = SizeType.AutoSize, float size = 0)
        {
            stack.RowStyles.Add(new RowStyle(type, size));
            c.Dock = DockStyle.Fill;
            stack.Controls.Add(c);
        }

        Add(new Label { Text = title, AutoSize = true, Font = Theme.SectionFont, ForeColor = Color.White });
        Add(new Label { Text = subtitle, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 10) });
        if (notes.Trim().Length > 0)
        {
            Add(new Label { Text = "Что нового", AutoSize = true, Font = Theme.BoldFont, ForeColor = Theme.TextColor });
            var box = new TextBox
            {
                Text = notes.Replace("\r\n", "\n").Replace("\n", "\r\n").Trim(),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.BaseTop,
                ForeColor = Theme.TextColor,
                Margin = new Padding(0, 4, 0, 8)
            };
            Add(box, SizeType.Percent, 100);
        }
        Add(_percent);
        Add(_bar, SizeType.Absolute, 22);
        Add(_status);

        _button.Click += (_, _) =>
        {
            if (_finished)
                Close();
            else
                _cancel.Cancel();
        };
        Controls.Add(stack);
        Controls.Add(Forms.ButtonBar(_button));
        _percent.Text = "0%";
        _status.Text = $"{_asset.Name} · {Mb(_asset.Size)}";
    }

    public string? Error { get; private set; }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            var fraction = p.Total > 0 ? Math.Min(1.0, p.Done / (double)p.Total) : 0;
            _bar.Value = fraction;
            _percent.Text = $"{fraction * 100:0}%";
            var seconds = Math.Max(0.5, (DateTime.UtcNow - _started).TotalSeconds);
            _status.Text = $"Скачано {Mb(p.Done)} из {Mb(p.Total)} · {p.Done / seconds / 1048576:0.0} МБ/с";
        });

        try
        {
            await _service.DownloadAsync(_asset, _destination, progress, _cancel.Token);
            _bar.Value = 1;
            _percent.Text = "100%";
            _status.Text = "Скачано и проверено (SHA-256)";
            _finished = true;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            _finished = true;
            DialogResult = DialogResult.Cancel;
        }
        catch (Exception ex)
        {
            Error = ex is UpdateException ? ex.Message : "не удалось скачать: " + ex.Message;
            _finished = true;
            _percent.Text = "Ошибка";
            _percent.ForeColor = Theme.DangerText;
            _status.Text = Error;
            _status.MaximumSize = new Size(ClientSize.Width - 50, 0);
            _button.Text = "Закрыть";
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Пока идёт скачивание, закрыть окно можно только кнопкой «Отмена»
        if (!_finished && e.CloseReason == CloseReason.UserClosing)
        {
            _cancel.Cancel();
            e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cancel.Dispose();
        base.Dispose(disposing);
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} МБ";

    private sealed class ProgressLine : Control
    {
        private double _value;

        public ProgressLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.BaseBottom;
        }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, 0, 1);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 2, Width - 1, 10);
            Theme.FillRounded(e.Graphics, r, 5, Theme.TrackBack);
            var width = (int)(r.Width * _value);
            if (width >= 10)
                Theme.FillRounded(e.Graphics, new Rectangle(r.X, r.Y, width, r.Height), 5, Theme.Accent);
        }
    }
}
