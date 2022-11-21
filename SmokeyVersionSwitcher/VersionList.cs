using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace SmokeyVersionSwitcher
{
    class VersionList : ObservableCollection<WPFDataTypes.Version>
    {
        private readonly string _cache_file;
        private readonly WPFDataTypes.IVersionCommands _commands;

        public VersionList(string cache_file, WPFDataTypes.IVersionCommands commands)
        {
            _cache_file = cache_file;
            _commands = commands;
        }

        private void ParseList(JArray data)
        {
            Clear();

            foreach (JObject keys in data)
                Add(new WPFDataTypes.Version((string)keys["Name"], (string)keys["Type"], (string)keys["UUID"], _commands));
        }

        public async Task LoadFromCache()
        {
            try
            {
                JArray jArray = JsonConvert.DeserializeObject<JArray>(File.ReadAllText(_cache_file));
                ParseList(jArray);
            }
            catch (FileNotFoundException)
            { // ignore
            }
        }
    }
}
