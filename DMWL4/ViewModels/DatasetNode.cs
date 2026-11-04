using FellowOakDicom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MWL4.ViewModels
{
    /// <summary>
    /// One line in the dataset details tree: an element, a sequence or a sequence item.
    /// </summary>
    public class DatasetNode
    {
        public string Text { get; set; }
        public List<DatasetNode> Children { get; } = new List<DatasetNode>();

        private const int MaxValueLength = 1024;

        private static readonly HashSet<string> BinaryVRs = new HashSet<string>
        {
            "OB", "OD", "OF", "OL", "OV", "OW", "UN"
        };

        /// <summary>Builds the tree for all elements of the dataset, including sequences and private tags.</summary>
        public static List<DatasetNode> Build(DicomDataset dataset)
        {
            var nodes = new List<DatasetNode>();
            if (dataset == null) return nodes;

            foreach (var item in dataset)
            {
                nodes.Add(BuildItem(dataset, item));
            }
            return nodes;
        }

        private static DatasetNode BuildItem(DicomDataset dataset, DicomItem item)
        {
            var tag = item.Tag;
            var vr = item.ValueRepresentation?.Code ?? "??";
            var prefix = $"{FormatTag(tag)} {vr}  {GetTagName(tag)}";

            if (item is DicomSequence seq)
            {
                var node = new DatasetNode { Text = $"{prefix} [{seq.Items.Count} item(s)]" };
                for (var i = 0; i < seq.Items.Count; i++)
                {
                    var itemNode = new DatasetNode { Text = $"Item {i + 1}" };
                    itemNode.Children.AddRange(Build(seq.Items[i]));
                    node.Children.Add(itemNode);
                }
                return node;
            }

            if (item is DicomFragmentSequence frag)
            {
                return new DatasetNode { Text = $"{prefix} = <{frag.Fragments.Count} fragment(s)>" };
            }

            return new DatasetNode { Text = $"{prefix} = {GetValue(dataset, item)}" };
        }

        private static string FormatTag(DicomTag tag) => $"({tag.Group:X4},{tag.Element:X4})";

        private static string GetTagName(DicomTag tag)
        {
            if (tag.IsPrivate)
            {
                var creator = tag.PrivateCreator?.Creator;
                var name = tag.DictionaryEntry?.Name;
                if (!string.IsNullOrEmpty(name) && name != DicomDictionary.UnknownTag.Name)
                {
                    return $"{name} [Private: {creator}]";
                }
                return string.IsNullOrEmpty(creator) ? "Private Tag" : $"Private Tag [{creator}]";
            }

            var entry = tag.DictionaryEntry;
            if (entry == null || entry == DicomDictionary.UnknownTag) return "Unknown Tag";
            return entry.Name;
        }

        private static string GetValue(DicomDataset dataset, DicomItem item)
        {
            if (item is DicomElement element)
            {
                if (BinaryVRs.Contains(element.ValueRepresentation.Code))
                {
                    return $"<{element.Buffer?.Size ?? 0} bytes>";
                }

                try
                {
                    if (dataset.TryGetString(element.Tag, out var value))
                    {
                        value = value ?? string.Empty;
                        if (value.Length > MaxValueLength) value = value.Substring(0, MaxValueLength) + "…";
                        return value.Length == 0 ? "<empty>" : value;
                    }
                }
                catch (Exception)
                {
                    // Fall through to the length-only display below.
                }

                return $"<{element.Buffer?.Size ?? 0} bytes>";
            }

            return string.Empty;
        }

        /// <summary>Returns the tree as indented plain text.</summary>
        public static string ToText(IEnumerable<DatasetNode> nodes)
        {
            var sb = new StringBuilder();
            AppendText(sb, nodes, 0);
            return sb.ToString();
        }

        private static void AppendText(StringBuilder sb, IEnumerable<DatasetNode> nodes, int depth)
        {
            foreach (var node in nodes ?? Enumerable.Empty<DatasetNode>())
            {
                sb.Append(' ', depth * 2).AppendLine(node.Text);
                AppendText(sb, node.Children, depth + 1);
            }
        }

        public override string ToString() => Text;
    }
}
