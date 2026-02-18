using System.Data;
using System.Management.Automation;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace PSForge.Services;

/// <summary>
/// Converts PowerShell output objects (PSObject graphs) into displayable formats.
/// Supports three output modes:
///   - Grid: DataTable with auto-detected columns for DataGrid binding
///   - Text: Format-List style string representation
///   - JSON: Serialized JSON for structured export
///
/// Also provides CSV export for clipboard operations.
/// </summary>
public sealed class OutputFormatterService
{
    private readonly ILogger<OutputFormatterService> _logger;

    public OutputFormatterService(ILogger<OutputFormatterService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Converts a list of output objects into a DataTable for DataGrid display.
    /// Auto-generates columns from the union of all property names across all objects.
    ///
    /// This approach handles heterogeneous output (where different objects may have
    /// different property sets) by creating the superset of all columns.
    /// </summary>
    public DataTable ConvertToDataTable(List<object> outputObjects)
    {
        var table = new DataTable();
        if (outputObjects.Count == 0) return table;

        _logger.LogDebug("Converting {Count} objects to DataTable", outputObjects.Count);

        // First pass: collect all unique property names across all objects.
        // Preserves insertion order for consistent column ordering.
        var propertyNames = new List<string>();
        var propertySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var obj in outputObjects)
        {
            if (obj is PSObject psObj)
            {
                foreach (var prop in psObj.Properties)
                {
                    if (propertySet.Add(prop.Name))
                    {
                        propertyNames.Add(prop.Name);
                    }
                }
            }
        }

        // Fallback: if no PSObject properties found, use a single "Value" column
        if (propertyNames.Count == 0)
        {
            table.Columns.Add("Value", typeof(string));
            foreach (var obj in outputObjects)
            {
                table.Rows.Add(obj?.ToString() ?? string.Empty);
            }
            return table;
        }

        // Create columns from discovered property names
        foreach (var name in propertyNames)
        {
            table.Columns.Add(name, typeof(object));
        }

        // Second pass: populate rows, handling missing properties gracefully
        foreach (var obj in outputObjects)
        {
            var row = table.NewRow();

            if (obj is PSObject psObj)
            {
                foreach (var name in propertyNames)
                {
                    try
                    {
                        var prop = psObj.Properties[name];
                        row[name] = prop?.Value ?? DBNull.Value;
                    }
                    catch
                    {
                        // Property doesn't exist on this object — expected for heterogeneous output
                        row[name] = DBNull.Value;
                    }
                }
            }
            else
            {
                // Non-PSObject: put string representation in first column
                row[propertyNames[0]] = obj?.ToString() ?? string.Empty;
            }

            table.Rows.Add(row);
        }

        _logger.LogDebug("DataTable created with {Cols} columns and {Rows} rows",
            table.Columns.Count, table.Rows.Count);

        return table;
    }

    /// <summary>
    /// Formats output objects as Format-List style text (one property per line).
    /// Each object is separated by a blank line for readability.
    /// </summary>
    public string FormatAsText(List<object> outputObjects)
    {
        if (outputObjects.Count == 0) return string.Empty;

        var lines = new List<string>();

        foreach (var obj in outputObjects)
        {
            if (obj is PSObject psObj)
            {
                foreach (var prop in psObj.Properties)
                {
                    try
                    {
                        lines.Add($"{prop.Name,-30} : {prop.Value}");
                    }
                    catch
                    {
                        lines.Add($"{prop.Name,-30} : <error reading value>");
                    }
                }
                lines.Add(string.Empty); // Blank line between objects
            }
            else
            {
                lines.Add(obj?.ToString() ?? string.Empty);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Serializes output objects to JSON format.
    /// Extracts PSObject properties into dictionaries for clean, portable JSON output.
    /// </summary>
    public string FormatAsJson(List<object> outputObjects)
    {
        if (outputObjects.Count == 0) return "[]";

        var serializable = new List<object>();

        foreach (var obj in outputObjects)
        {
            if (obj is PSObject psObj)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in psObj.Properties)
                {
                    try
                    {
                        dict[prop.Name] = prop.Value;
                    }
                    catch
                    {
                        dict[prop.Name] = null;
                    }
                }
                serializable.Add(dict);
            }
            else
            {
                serializable.Add(obj);
            }
        }

        return JsonConvert.SerializeObject(serializable, Formatting.Indented,
            new JsonSerializerSettings
            {
                // Prevent circular reference errors common with complex PS objects
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                // Handle types that can't be serialized (like SecureString)
                Error = (_, args) =>
                {
                    args.ErrorContext.Handled = true;
                }
            });
    }

    /// <summary>
    /// Formats output objects as CSV for clipboard export.
    /// Uses the DataTable conversion to ensure consistent columns.
    /// </summary>
    public string FormatAsCsv(List<object> outputObjects)
    {
        if (outputObjects.Count == 0) return string.Empty;

        var table = ConvertToDataTable(outputObjects);
        var lines = new List<string>();

        // Header row
        var headers = table.Columns.Cast<DataColumn>().Select(c => EscapeCsv(c.ColumnName));
        lines.Add(string.Join(",", headers));

        // Data rows
        foreach (DataRow row in table.Rows)
        {
            var values = row.ItemArray.Select(v => EscapeCsv(v?.ToString() ?? string.Empty));
            lines.Add(string.Join(",", values));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Escapes a value for CSV output.
    /// Values containing commas, quotes, or newlines are wrapped in double quotes
    /// with internal quotes doubled per RFC 4180.
    /// </summary>
    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
