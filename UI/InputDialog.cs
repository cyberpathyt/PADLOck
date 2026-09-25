namespace PADLOck.UI;

public sealed class InputDialog : DarkDialog
{
    private readonly TextBox _box;

    public InputDialog(string title, string prompt, string initial)
    {
        Text = title;
        Font = Theme.UiFont;
        BackColor = Theme.BaseBottom;
        ForeColor = Theme.TextColor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 150);

        var label = new Label { Text = prompt, AutoSize = true, Location = new Point(16, 16), ForeColor = Theme.TextColor };
        _box = new TextBox { Text = initial, Location = new Point(16, 44), Width = 388, MaxLength = 40 };
        Theme.StyleInput(_box);

        var ok = Theme.CreateButton("Сохранить", ButtonKind.Primary);
        ok.DialogResult = DialogResult.OK;
        ok.Location = new Point(ClientSize.Width - 16 - ok.Width, 100);

        var cancel = Theme.CreateButton("Отмена", ButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Location = new Point(ok.Left - 8 - cancel.Width, 100);

        Controls.AddRange(new Control[] { label, _box, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _box.Text.Trim();
}
