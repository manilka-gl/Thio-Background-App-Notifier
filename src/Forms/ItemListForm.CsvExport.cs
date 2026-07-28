using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;
using System.Windows.Forms;
using System.Xml;

#nullable enable

namespace Thio_Background_App_Notifier;

public partial class ItemListForm
{
    private const int JsonExportSchemaVersion = 1;
    private const int ExportButtonWidth = 120;
    private const int ExportButtonSpacing = 10;
    private static readonly char[] CsvCharactersRequiringQuotes = { ',', '"', '\r', '\n' };

    private Button? _buttonExportCsv;
    private Button? _buttonExportJson;

    private enum ExportFormat
    {
        Csv,
        Json
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EnsureExportButtons();
    }

    private void EnsureExportButtons()
    {
        if (_buttonExportCsv != null && _buttonExportJson != null)
            return;

        if (_buttonExportCsv == null)
        {
            _buttonExportCsv = CreateExportButton(
                name: "buttonExportCsv",
                text: "Export CSV",
                left: buttonClearFilter.Right + ExportButtonSpacing,
                tabIndex: buttonClearFilter.TabIndex + 1,
                clickHandler: buttonExportCsv_Click,
                toolTip: "Export every unfiltered row and column to an Excel-compatible CSV file. Exported data may include local system paths.");
        }

        if (_buttonExportJson == null)
        {
            _buttonExportJson = CreateExportButton(
                name: "buttonExportJson",
                text: "Export JSON",
                left: _buttonExportCsv.Right + ExportButtonSpacing,
                tabIndex: _buttonExportCsv.TabIndex + 1,
                clickHandler: buttonExportJson_Click,
                toolTip: "Export every unfiltered row and column to structured UTF-8 JSON. Exported data may include local system paths.");
        }
    }

    private Button CreateExportButton(
        string name,
        string text,
        int left,
        int tabIndex,
        EventHandler clickHandler,
        string toolTip)
    {
        Button button = new Button
        {
            AccessibleDescription = toolTip,
            AccessibleName = text,
            Anchor = buttonClearFilter.Anchor,
            BackColor = buttonClearFilter.BackColor,
            Font = buttonClearFilter.Font,
            Location = new Point(left, buttonClearFilter.Top),
            Name = name,
            Size = new Size(ExportButtonWidth, buttonClearFilter.Height),
            TabIndex = tabIndex,
            Text = text,
            UseCompatibleTextRendering = true,
            UseVisualStyleBackColor = false
        };

        button.Click += clickHandler;
        toolTip1.SetToolTip(button, toolTip);
        Controls.Add(button);
        button.BringToFront();
        return button;
    }

    private void buttonExportCsv_Click(object? sender, EventArgs e)
    {
        ExportWithSaveDialog(ExportFormat.Csv);
    }

    private void buttonExportJson_Click(object? sender, EventArgs e)
    {
        ExportWithSaveDialog(ExportFormat.Json);
    }

    private void ExportWithSaveDialog(ExportFormat format)
    {
        string extension = format == ExportFormat.Csv ? "csv" : "json";
        string formatName = format == ExportFormat.Csv ? "CSV" : "JSON";
        string filter = format == ExportFormat.Csv
            ? "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            : "JSON files (*.json)|*.json|All files (*.*)|*.*";

        using SaveFileDialog saveDialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = extension,
            FileName = GetDefaultExportFileName(extension),
            Filter = filter,
            FilterIndex = 1,
            OverwritePrompt = true,
            RestoreDirectory = true,
            SupportMultiDottedExtensions = true,
            Title = $"Export All Startup Items as {formatName}",
            ValidateNames = true
        };

        if (saveDialog.ShowDialog(this) != DialogResult.OK)
            return;

        ExportSnapshot snapshot = CaptureExportSnapshot();
        SetExportButtonsEnabled(false);

        try
        {
            if (format == ExportFormat.Csv)
                ExportSnapshotToCsv(snapshot, saveDialog.FileName);
            else
                ExportSnapshotToJson(snapshot, saveDialog.FileName);
        }
        catch (Exception ex) when (IsExpectedExportException(ex))
        {
            MessageBox.Show(
                this,
                $"The {formatName} file could not be saved.\n\n{ex.Message}",
                $"{formatName} Export Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        finally
        {
            SetExportButtonsEnabled(true);
        }

        MessageBox.Show(
            this,
            $"Exported {snapshot.Rows.Count:N0} rows to:\n{saveDialog.FileName}",
            $"{formatName} Export Complete",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void SetExportButtonsEnabled(bool enabled)
    {
        if (_buttonExportCsv != null)
            _buttonExportCsv.Enabled = enabled;
        if (_buttonExportJson != null)
            _buttonExportJson.Enabled = enabled;
    }

    private ExportSnapshot CaptureExportSnapshot()
    {
        int columnCount = listView.Columns.Count;
        string[] headers = new string[columnCount];

        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            string header = columnIndex < _baseHeaderText.Length
                ? _baseHeaderText[columnIndex]
                : listView.Columns[columnIndex].Text;
            headers[columnIndex] = MakeValidUnicode(header);
        }

        List<string[]> rows = new List<string[]>(_unfilteredItemList.Count);
        foreach (ListViewItem row in _unfilteredItemList)
        {
            string[] cells = new string[columnCount];
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                string value = columnIndex < row.SubItems.Count
                    ? row.SubItems[columnIndex].Text
                    : string.Empty;
                cells[columnIndex] = MakeValidUnicode(value);
            }
            rows.Add(cells);
        }

        string title = string.IsNullOrWhiteSpace(Text) ? "Startup Items" : Text.Trim();
        return new ExportSnapshot(
            title: MakeValidUnicode(title),
            exportedAtUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            headers: headers,
            rows: rows);
    }

    private static void ExportSnapshotToCsv(ExportSnapshot snapshot, string filePath)
    {
        WriteFileAtomically(filePath, stream =>
        {
            using StreamWriter writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                bufferSize: 4096,
                leaveOpen: true);

            WriteCsvRow(writer, snapshot.Headers);
            foreach (string[] row in snapshot.Rows)
                WriteCsvRow(writer, row);
        });
    }

    private static void ExportSnapshotToJson(ExportSnapshot snapshot, string filePath)
    {
        JsonExportDocument document = CreateJsonDocument(snapshot);

        WriteFileAtomically(filePath, stream =>
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(
                typeof(JsonExportDocument),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });

            using XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                ownsStream: false,
                indent: true,
                indentChars: "  ");

            serializer.WriteObject(writer, document);
            writer.Flush();
        });
    }

    private static JsonExportDocument CreateJsonDocument(ExportSnapshot snapshot)
    {
        HashSet<string> usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<JsonExportColumn> columns = new List<JsonExportColumn>(snapshot.Headers.Length);

        for (int index = 0; index < snapshot.Headers.Length; index++)
        {
            string key = CreateUniqueJsonKey(snapshot.Headers[index], index, usedKeys);
            columns.Add(new JsonExportColumn
            {
                Index = index,
                Key = key,
                Label = snapshot.Headers[index]
            });
        }

        List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>(snapshot.Rows.Count);
        foreach (string[] sourceRow in snapshot.Rows)
        {
            Dictionary<string, string> row = new Dictionary<string, string>(columns.Count, StringComparer.Ordinal);
            for (int index = 0; index < columns.Count; index++)
            {
                string value = index < sourceRow.Length ? sourceRow[index] : string.Empty;
                row.Add(columns[index].Key, value);
            }
            rows.Add(row);
        }

        return new JsonExportDocument
        {
            SchemaVersion = JsonExportSchemaVersion,
            SourceTitle = snapshot.Title,
            Scope = "all-unfiltered-rows",
            ExportedAtUtc = snapshot.ExportedAtUtc,
            RowCount = rows.Count,
            ColumnCount = columns.Count,
            Columns = columns,
            Rows = rows
        };
    }

    private static string CreateUniqueJsonKey(string label, int columnIndex, HashSet<string> usedKeys)
    {
        string normalized = MakeValidUnicode(label).Normalize(NormalizationForm.FormKC);
        StringBuilder builder = new StringBuilder(normalized.Length);
        bool previousWasSeparator = false;

        foreach (char character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (builder.Length > 0 && !previousWasSeparator)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        string baseKey = builder.ToString().Trim('_');
        if (string.IsNullOrEmpty(baseKey))
            baseKey = $"column_{columnIndex + 1}";
        else if (char.IsDigit(baseKey[0]))
            baseKey = "column_" + baseKey;

        string candidate = baseKey;
        int suffix = 2;
        while (!usedKeys.Add(candidate))
            candidate = $"{baseKey}_{suffix++}";

        return candidate;
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
        value = NeutralizeSpreadsheetFormula(MakeValidUnicode(value));

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

    private static string MakeValidUnicode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder? sanitized = null;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    if (sanitized != null)
                    {
                        sanitized.Append(character);
                        sanitized.Append(value[++index]);
                    }
                    else
                    {
                        index++;
                    }
                    continue;
                }

                if (sanitized == null)
                {
                    sanitized = new StringBuilder(value.Length);
                    sanitized.Append(value, 0, index);
                }
                sanitized.Append('\uFFFD');
            }
            else if (char.IsLowSurrogate(character))
            {
                if (sanitized == null)
                {
                    sanitized = new StringBuilder(value.Length);
                    sanitized.Append(value, 0, index);
                }
                sanitized.Append('\uFFFD');
            }
            else if (sanitized != null)
            {
                sanitized.Append(character);
            }
        }

        return sanitized?.ToString() ?? value;
    }

    private static void WriteFileAtomically(string filePath, Action<Stream> writeAction)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A destination file path is required.", nameof(filePath));
        if (writeAction == null)
            throw new ArgumentNullException(nameof(writeAction));

        string destinationPath = Path.GetFullPath(filePath);
        string? directoryPath = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException("The selected destination folder does not exist.");

        string temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                writeAction(stream);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destinationPath))
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup only; never mask the original export error.
                }
            }
        }
    }

    private static bool IsExpectedExportException(Exception exception)
    {
        return exception is IOException
            || exception is UnauthorizedAccessException
            || exception is SecurityException
            || exception is SerializationException
            || exception is InvalidDataContractException
            || exception is XmlException
            || exception is ArgumentException
            || exception is NotSupportedException;
    }

    private string GetDefaultExportFileName(string extension)
    {
        string baseName = string.IsNullOrWhiteSpace(Text) ? "startup-items" : MakeValidUnicode(Text.Trim());
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalidCharacter, '_');

        baseName = baseName.Trim(' ', '.');
        if (string.IsNullOrEmpty(baseName))
            baseName = "startup-items";
        if (baseName.Length > 80)
            baseName = baseName.Substring(0, 80).TrimEnd(' ', '.');

        return $"{baseName}_{DateTime.Now:yyyy-MM-dd_HHmmss}.{extension}";
    }

    private sealed class ExportSnapshot
    {
        public ExportSnapshot(string title, string exportedAtUtc, string[] headers, List<string[]> rows)
        {
            Title = title;
            ExportedAtUtc = exportedAtUtc;
            Headers = headers;
            Rows = rows;
        }

        public string Title { get; }
        public string ExportedAtUtc { get; }
        public string[] Headers { get; }
        public List<string[]> Rows { get; }
    }

    [DataContract]
    private sealed class JsonExportDocument
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "sourceTitle", Order = 1)]
        public string SourceTitle { get; set; } = string.Empty;

        [DataMember(Name = "scope", Order = 2)]
        public string Scope { get; set; } = string.Empty;

        [DataMember(Name = "exportedAtUtc", Order = 3)]
        public string ExportedAtUtc { get; set; } = string.Empty;

        [DataMember(Name = "rowCount", Order = 4)]
        public int RowCount { get; set; }

        [DataMember(Name = "columnCount", Order = 5)]
        public int ColumnCount { get; set; }

        [DataMember(Name = "columns", Order = 6)]
        public List<JsonExportColumn> Columns { get; set; } = new List<JsonExportColumn>();

        [DataMember(Name = "rows", Order = 7)]
        public List<Dictionary<string, string>> Rows { get; set; } = new List<Dictionary<string, string>>();
    }

    [DataContract]
    private sealed class JsonExportColumn
    {
        [DataMember(Name = "index", Order = 0)]
        public int Index { get; set; }

        [DataMember(Name = "key", Order = 1)]
        public string Key { get; set; } = string.Empty;

        [DataMember(Name = "label", Order = 2)]
        public string Label { get; set; } = string.Empty;
    }
}
