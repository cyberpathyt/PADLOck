using PADLOck.Services;

namespace PADLOck.UI;

// Выбор шагов прошивки галочками: полностью или частично
public sealed class StepsDialog : DarkDialog
{
    private readonly List<(StepKey Key, CheckBox Box)> _boxes = new();

    public StepsDialog(string title, string summary, string actionText,
                       IReadOnlyList<ProvisionStep> steps, IReadOnlySet<StepKey> preselected)
    {
        Forms.StyleDialog(this, title, new Size(620, 640));

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(20, 14, 20, 10),
            BackColor = Theme.BaseBottom
        };
        list.Controls.Add(new Label
        {
            Text = summary,
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, 10)
        });

        foreach (var step in steps)
        {
            var box = Forms.Check(step.Title);
            box.Font = Theme.BoldFont;
            box.Checked = preselected.Contains(step.Key);
            box.Margin = new Padding(0, 6, 0, 0);
            var hint = new Label
            {
                Text = step.Hint,
                AutoSize = true,
                MaximumSize = new Size(540, 0),
                ForeColor = Theme.Dimmed,
                Font = Theme.SmallFont,
                Margin = new Padding(20, 0, 0, 2)
            };
            hint.Click += (_, _) => box.Checked = !box.Checked;
            list.Controls.Add(box);
            list.Controls.Add(hint);
            _boxes.Add((step.Key, box));
        }

        var ok = Theme.CreateButton(actionText, ButtonKind.Primary);
        ok.Click += (_, _) =>
        {
            if (Selected.Count == 0) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = Theme.CreateButton("Отмена", ButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;
        var all = Theme.CreateButton("Все", ButtonKind.Secondary);
        all.Click += (_, _) => _boxes.ForEach(b => b.Box.Checked = true);
        var none = Theme.CreateButton("Ничего", ButtonKind.Secondary);
        none.Click += (_, _) => _boxes.ForEach(b => b.Box.Checked = false);

        var right = Forms.ButtonBar(ok, cancel);
        right.Dock = DockStyle.Fill;
        right.Padding = Padding.Empty;
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Theme.BaseTop };
        all.Margin = Padding.Empty;
        left.Controls.Add(all);
        left.Controls.Add(none);
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12), BackColor = Theme.BaseTop };
        bar.Controls.Add(right);
        bar.Controls.Add(left);

        Controls.Add(list);
        Controls.Add(bar);
        CancelButton = cancel;
        AcceptButton = ok;
    }

    public HashSet<StepKey> Selected => _boxes.Where(b => b.Box.Checked).Select(b => b.Key).ToHashSet();
}
