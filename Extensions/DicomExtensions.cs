using FellowOakDicom;
using System.Linq;

namespace MWL4.Extensions
{
    public static class DicomExtensions
    {
        public static string GetStringSafe(this DicomDataset dataset, DicomTag tag, DicomTag sequenceTag = null)
        {
            if (dataset == null) return string.Empty;

            if (sequenceTag == null)
            {
                return dataset.GetSingleValueOrDefault(tag, string.Empty).Trim();
            }

            if (dataset.TryGetSequence(sequenceTag, out var sequence) && sequence.Items.Any())
            {
                var firstItem = sequence.Items.First();
                return firstItem.GetSingleValueOrDefault(tag, string.Empty).Trim();
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
                    return cSeq.Items.First().GetSingleValueOrDefault(targetTag, string.Empty).Trim();
                }
            }
            return string.Empty;
        }
    }
}