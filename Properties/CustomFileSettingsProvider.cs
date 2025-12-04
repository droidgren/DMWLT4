using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Xml.Linq;

namespace MWL4.Properties
{
    public class CustomFileSettingsProvider : SettingsProvider
    {
        // Hardcoded path to %LocalAppData%\DMWL4\user.config
        private static readonly string UserConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DMWLT4",
            "user.config");

        public override string ApplicationName
        {
            get => System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            set { }
        }

        public override void Initialize(string name, NameValueCollection config)
        {
            if (string.IsNullOrEmpty(name)) name = "CustomFileSettingsProvider";
            base.Initialize(name, config);
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            var values = new SettingsPropertyValueCollection();
            XDocument doc = null;

            // 1. Attempt to load the XML file
            if (File.Exists(UserConfigPath))
            {
                try
                {
                    doc = XDocument.Load(UserConfigPath);
                }
                catch
                {
                    // If file is corrupt, we'll just use defaults
                }
            }

            // 2. Iterate through all settings defined in the application
            foreach (SettingsProperty prop in collection)
            {
                var value = new SettingsPropertyValue(prop)
                {
                    IsDirty = false
                };

                if (doc != null && doc.Root != null)
                {
                    var settingElement = doc.Root.Element(prop.Name);
                    if (settingElement != null)
                    {
                        // Found the value in the file
                        value.SerializedValue = settingElement.Value;
                    }
                    else
                    {
                        // Not in file, use default
                        value.SerializedValue = prop.DefaultValue;
                    }
                }
                else
                {
                    // File doesn't exist, use default
                    value.SerializedValue = prop.DefaultValue;
                }

                values.Add(value);
            }

            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            // 1. Load or Create the XML document
            XDocument doc;
            if (File.Exists(UserConfigPath))
            {
                try
                {
                    doc = XDocument.Load(UserConfigPath);
                }
                catch
                {
                    doc = new XDocument(new XElement("Settings"));
                }
            }
            else
            {
                doc = new XDocument(new XElement("Settings"));
            }

            if (doc.Root == null)
            {
                doc.Add(new XElement("Settings"));
            }

            // 2. Ensure directory exists
            var directory = Path.GetDirectoryName(UserConfigPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 3. Update values in the XML
            foreach (SettingsPropertyValue propValue in collection)
            {
                var element = doc.Root.Element(propValue.Name);
                if (element == null)
                {
                    element = new XElement(propValue.Name);
                    doc.Root.Add(element);
                }

                element.Value = propValue.SerializedValue?.ToString() ?? string.Empty;
            }

            // 4. Save
            doc.Save(UserConfigPath);
        }
    }
}