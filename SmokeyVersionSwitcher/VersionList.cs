using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using System.Windows;

namespace SmokeyVersionSwitcher
{
    class VersionList : ObservableCollection<WPFDataTypes.Version>
    {
        private readonly string _cache_file;
        private readonly string _versiondb;
        private readonly HttpClient _client = new HttpClient();
        private readonly WPFDataTypes.IVersionCommands _commands;

        public VersionList(string cache_file, string versiondb, WPFDataTypes.IVersionCommands commands)
        {
            _cache_file = cache_file;
            _versiondb = versiondb;
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

        public async Task DownloadList()
        {
            HttpResponseMessage resp = await _client.GetAsync(_versiondb);
            _ = resp.EnsureSuccessStatusCode();
            string data = await resp.Content.ReadAsStringAsync();
            File.WriteAllText(_cache_file, data);
            ParseList(JArray.Parse(data));
        }
    }
}
