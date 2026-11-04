using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace MWL4.Infrastructure
{
    /// <summary>
    /// Saves and restores the order and width of DataGrid columns as "Prop=displayIndex,width;..."
    /// (empty width = Auto). Columns are identified by the path of their binding.
    /// </summary>
    public static class ColumnLayout
    {
        public static string GetBoundProperty(DataGridColumn column)
        {
            if (column is DataGridBoundColumn bound && bound.Binding is Binding binding)
            {
                return binding.Path?.Path;
            }
            return null;
        }

        public static string Save(IEnumerable<DataGridColumn> columns)
        {
            var parts = new List<string>();
            foreach (var column in columns)
            {
                var prop = GetBoundProperty(column);
                if (string.IsNullOrEmpty(prop)) continue;
                // A column the user resized gets an absolute width; Auto/star widths are not stored.
                var width = column.Width.IsAbsolute
                    ? column.Width.Value.ToString("0.#", CultureInfo.InvariantCulture)
                    : string.Empty;
                parts.Add($"{prop}={column.DisplayIndex},{width}");
            }
            return string.Join(";", parts);
        }

        /// <summary>
        /// Applies a saved layout. <paramref name="originalOrder"/> is the column order from the XAML; it decides
        /// the place of columns that are missing from the layout (e.g. added in a newer version): they go last.
        /// </summary>
        public static void Apply(IList<DataGridColumn> columns, IList<DataGridColumn> originalOrder, string layout)
        {
            if (string.IsNullOrWhiteSpace(layout)) return;

            var entries = new Dictionary<string, (int Index, double? Width)>();
            foreach (var part in layout.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var values = kv[1].Split(',');
                if (!int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) continue;
                double? width = null;
                if (values.Length > 1 && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0)
                {
                    width = w;
                }
                entries[kv[0]] = (index, width);
            }
            if (entries.Count == 0) return;

            var ordered = columns
                .Select(c => new { Column = c, Prop = GetBoundProperty(c) })
                .OrderBy(x => x.Prop != null && entries.ContainsKey(x.Prop) ? entries[x.Prop].Index : int.MaxValue)
                .ThenBy(x => originalOrder.IndexOf(x.Column))
                .ToList();

            // Assigning DisplayIndex 0..n-1 in ascending order keeps earlier assignments in place.
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Column.DisplayIndex = i;
                if (ordered[i].Prop != null && entries.TryGetValue(ordered[i].Prop, out var entry) && entry.Width.HasValue)
                {
                    ordered[i].Column.Width = new DataGridLength(entry.Width.Value);
                }
            }
        }

        /// <summary>Restores the XAML column order and automatic widths.</summary>
        public static void Reset(IList<DataGridColumn> originalOrder)
        {
            for (var i = 0; i < originalOrder.Count; i++)
            {
                originalOrder[i].DisplayIndex = i;
                originalOrder[i].Width = DataGridLength.Auto;
            }
        }
    }
}
