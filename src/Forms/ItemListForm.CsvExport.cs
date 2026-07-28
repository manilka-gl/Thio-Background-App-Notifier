using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

#nullable enable

namespace Thio_Background_App_Notifier;

public partial class ItemListForm
{
    private static readonly char[] CsvCharactersRequiringQuotes = { ',', '"', '\r', '\n' };

    private Button? _buttonExportCsv;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EnsureExportCsvButton();
    }

    private void EnsureExportCsvButton()
    {
        if (_buttonExportCsv != null)
            return;

        _buttonExportCsv = new Button
        {
            Anchor = buttonClearFilter.Anchor,
            BackColor = buttonClearFilter.BackColor,
            Font = buttonClearFilter.Font,
            Location = new Point(buttonClearFilter.Right + 10, buttonClearFilter.Top),
            Name = "buttonExportCsv",
            Size = new Size(120, buttonClearFilter.Height),
            TabIndex = buttonClearFilter.TabIndex + 1,
            Text = "Export CSV",
            UseCompatibleTextRendering = true,
            UseVisualStyleBackColor = false
        };

        _buttonExportCsv.Click += buttonExportCsv_Click;
        toolTip1.SetToolTip(_buttonExportCsv, "Export all rows and columns to a CSV file. The current filter is ignored.");
        Controls.Add(_buttonExportCsv);
        _buttonExportCsv.BringToFront();
    }

    private void buttonExportCsv_Click(object? sender, EventArgs e)
    {
        using SaveFileDialog saveDialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "csv",
            FileName = GetDefaultCsvFileName(),
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            OverwritePrompt = true,
            RestoreDirectory = true,
            Title = "Export Startup Items"
        };

        if (saveDialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            ExportAllRowsToCsv(saveDialog.FileName);
            MessageBox.Show(
                this,
                $"Exported {_unfilteredItemList.Count:N0} rows to:\n{saveDialog.FileName}",
                "CSV Export Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"The CSV file could not be saved.\n\n{ex.Message}",
                "CSV Export Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExportAllRowsToCsv(string filePath)
    {
        using StreamWriter writer = new StreamWriter(
            filePath,
            append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        string[] headers = new string[listView.Columns.Count];
        for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
        {
            headers[columnIndex] = columnIndex < _baseHeaderText.Length
                ? _baseHeaderText[columnIndex]
                : listView.Columns[columnIndex].Text;
        }
        WriteCsvRow(writer, headers);

        foreach (ListViewItem row in _unfilteredItemList)
        {
            string[] cells = new string[listView.Columns.Count];
            for (int columnIndex = 0; columnIndex < cells.Length; columnIndex++)
            {
                cells[columnIndex] = columnIndex < row.SubItems.Count
                    ? row.SubItems[columnIndex].Text
                    : string.Empty;
            }
            WriteCsvRow(writer, cells);
        }
    }

    private static void WriteCsvRow(TextWriter writer, IReadOnlyList<string> cells)
    {
        for (int index = 0; index < cells.Count; index++)
        {
            if (index > 0)
                writer.Write(',');

            writer.Write(EscapeCsvCell(cells[index]));
        }

        writer.WriteLine();
    }

    private static string EscapeCsvCell(string value)
    {
        value = NeutralizeSpreadsheetFormula(value);

        bool requiresQuotes = value.IndexOfAny(CsvCharactersRequiringQuotes) >= 0;
        if (value.IndexOf('"') >= 0)
            value = value.Replace("\"", "\"\"");

        return requiresQuotes ? $"\"{value}\"" : value;
    }

    private static string NeutralizeSpreadsheetFormula(string value)
    {
        int firstNonWhitespaceIndex = 0;
        while (firstNonWhitespaceIndex < value.Length && char.IsWhiteSpace(value[firstNonWhitespaceIndex]))
            firstNonWhitespaceIndex++;

        if (firstNonWhitespaceIndex < value.Length)
        {
            char firstCharacter = value[firstNonWhitespaceIndex];
            if (firstCharacter == '=' || firstCharacter == '+' || firstCharacter == '-' || firstCharacter == '@')
                return "'" + value;
        }

        return value;
    }

    private string GetDefaultCsvFileName()
    {
        string baseName = string.IsNullOrWhiteSpace(Text) ? "startup-items" : Text.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalidCharacter, '_');

        return $"{baseName}_{DateTime.Now:yyyy-MM-dd_HHmmss}.csv";
    }
}
