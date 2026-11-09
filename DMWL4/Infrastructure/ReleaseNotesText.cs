using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MWL4.Infrastructure
{
    /// <summary>Makes the Markdown of a GitHub release readable as plain text.</summary>
    public static class ReleaseNotesText
    {
        private static readonly Regex Heading = new Regex(@"^#+\s*");
        private static readonly Regex Bullet = new Regex(@"^[*-]\s+");
        private static readonly Regex TableSeparator = new Regex(@"^\|[\s:|-]*-[\s:|-]*\|$");

        /// <summary>Leaves out the Downloads section, which only says what is attached to the release page.</summary>
        public static string ToPlainText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

            var lines = new List<string>();
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (TableSeparator.IsMatch(line)) continue;
                if (line.StartsWith(">", StringComparison.Ordinal)) line = line.TrimStart('>').TrimStart();

                var text = Heading.Replace(line, string.Empty);
                if (text.Length != line.Length && text.Trim().Equals("Downloads", StringComparison.OrdinalIgnoreCase)) break;

                text = Bullet.Replace(text, "• ");
                lines.Add(text.Replace("**", string.Empty).Replace("`", string.Empty));
            }
            return string.Join(Environment.NewLine, lines).Trim();
        }
    }
}
