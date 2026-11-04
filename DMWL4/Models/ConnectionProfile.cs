using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace MWL4.Models
{
    /// <summary>A named set of connection settings for an SCP.</summary>
    public class ConnectionProfile
    {
        public string Name { get; set; }
        public string CallingAET { get; set; }
        public string ServerAET { get; set; }
        public string ServerHost { get; set; }
        public int ServerPort { get; set; }
        public bool UseTLS { get; set; }
        public bool TlsIgnoreCertErrors { get; set; }
        public string TlsClientCertThumbprint { get; set; }

        public override string ToString() => Name;

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(List<ConnectionProfile>));

        public static string Serialize(IEnumerable<ConnectionProfile> profiles)
        {
            using (var writer = new StringWriter())
            {
                Serializer.Serialize(writer, new List<ConnectionProfile>(profiles));
                return writer.ToString();
            }
        }

        /// <summary>Reads profiles from XML. Throws on invalid XML; an empty string gives an empty list.</summary>
        public static List<ConnectionProfile> Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return new List<ConnectionProfile>();
            using (var reader = new StringReader(xml))
            {
                return (List<ConnectionProfile>)Serializer.Deserialize(reader) ?? new List<ConnectionProfile>();
            }
        }
    }
}
