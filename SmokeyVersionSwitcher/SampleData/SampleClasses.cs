using System.Collections.Generic;

namespace SmokeyVersionSwitcher.SampleData
{
    public class SampleVersion
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }
        public string DisplayInstallStatus { get; set; }

        public bool IsInstalled { get; set; }

        public bool IsBeta { get; set; }
    }

    public class SampleVersionList : List<SampleVersion> { }
}