using System.Reflection;

namespace MWL4
{
    public static class AppInfo
    {
        /// <summary>The program's name as shown in title bars and the About window.</summary>
        public const string DisplayName = "DICOM Modality Worklist Tester";

        public const string ProjectUrl = "https://github.com/droidgren/DMWLT4";

        /// <summary>GitHub's answer to "what is the newest release", which leaves out drafts and prereleases.</summary>
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/droidgren/DMWLT4/releases/latest";

        /// <summary>What Windows knows the program by. Must match the ID the installer puts on the Start menu shortcut.</summary>
        public const string AppUserModelId = "VGR.DMWLT4";

        public static string CurrentVersion
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version.ToString();
            }
        }
    }
}
