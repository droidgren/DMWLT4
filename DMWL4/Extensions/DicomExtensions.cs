using FellowOakDicom;
using System.Linq;

namespace MWL4.Extensions
{
    public static class DicomExtensions
    {
        /// <summary>
        /// Reads the full string value of an element. Unlike GetSingleValueOrDefault this also
        /// works for multi-valued elements (VM &gt; 1), which are returned backslash-separated
        /// instead of being silently replaced by an empty string.
        /// </summary>
        private static string ReadString(DicomDataset dataset, DicomTag tag)
        {
            return dataset.TryGetString(tag, out var value) && value != null
                ? value.Trim()
                : string.Empty;
        }

        public static string GetStringSafe(this DicomDataset dataset, DicomTag tag, DicomTag sequenceTag = null)
        {
            if (dataset == null) return string.Empty;

            if (sequenceTag == null)
            {
                return ReadString(dataset, tag);
            }

            if (dataset.TryGetSequence(sequenceTag, out var sequence) && sequence.Items.Any())
            {
                return ReadString(sequence.Items.First(), tag);
            }

            return string.Empty;
        }

        public static string GetNestedStringSafe(this DicomDataset dataset, DicomTag parentSequence, DicomTag childSequence, DicomTag targetTag)
        {
            if (dataset == null) return string.Empty;

            if (dataset.TryGetSequence(parentSequence, out var pSeq) && pSeq.Items.Any())
            {
                var parentItem = pSeq.Items.First();
                if (parentItem.TryGetSequence(childSequence, out var cSeq) && cSeq.Items.Any())
                {
                    return ReadString(cSeq.Items.First(), targetTag);
                }
            }
            return string.Empty;
        }
    }
}