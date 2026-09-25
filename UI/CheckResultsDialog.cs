using PADLOck.Services;

namespace PADLOck.UI;

public sealed class CheckResultsDialog : DarkDialog
{
    public CheckResultsDialog(IEnumerable<(string Device, List<CheckItem> Items)> results)
    {
        Forms.StyleDialog(this, "Проверка планшетов", new Size(900, 560));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Theme.BaseBottom,
            GridColor = Theme.GridLines,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeight = 34
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.BaseTop, ForeColor = Theme.Muted, Font = Theme.BoldFont,
            SelectionBackColor = Theme.BaseTop, SelectionForeColor = Theme.Muted
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.BaseBottom, ForeColor = Theme.TextColor, Font = Theme.UiFont,
            SelectionBackColor = Theme.InputBack, SelectionForeColor = Theme.TextColor,
            Padding = new Padding(4, 0, 4, 0)
        };
        grid.RowTemplate.Height = 30;
        grid.Columns.Add("Device", "Планшет");
        grid.Columns.Add("Check", "Проверка");
        grid.Columns.Add("Result", "Результат");
        grid.Columns.Add("Details", "Подробности");
        grid.Columns[0].FillWeight = 22;
        grid.Columns[1].FillWeight = 30;
        grid.Columns[2].FillWeight = 12;
        grid.Columns[3].FillWeight = 36;

        var okStyle = new DataGridViewCellStyle { ForeColor = Theme.SuccessText, Font = Theme.BoldFont };
        var errorStyle = new DataGridViewCellStyle { ForeColor = Theme.DangerText, Font = Theme.BoldFont };
        var rows = new List<DataGridViewRow>();
        foreach (var (device, items) in results)
        {
            foreach (var item in items)
            {
                var row = new DataGridViewRow();
                row.CreateCells(grid, device, item.Title, item.Ok ? "OK" : "Ошибка", item.Details);
                row.Cells[2].Style = item.Ok ? okStyle : errorStyle;
                rows.Add(row);
            }
        }
        grid.Rows.AddRange(rows.ToArray());

        var close = Theme.CreateButton("Закрыть", ButtonKind.Secondary);
        close.DialogResult = DialogResult.Cancel;
        Controls.Add(grid);
        Controls.Add(Forms.ButtonBar(close));
        CancelButton = close;
    }
}
