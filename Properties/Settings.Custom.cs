using System.Configuration;

namespace DMWLT4.Properties // Make sure this matches the namespace in Settings.Designer.cs
{
    // Apply the provider to the entire Settings class
    [SettingsProvider(typeof(MWL4.Properties.CustomFileSettingsProvider))]
    internal sealed partial class Settings
    {
        // This partial class merges with the auto-generated Designer.cs
    }
}