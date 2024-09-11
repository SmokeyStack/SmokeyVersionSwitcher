using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmokeyVersionSwitcher
{
    class VersionList : ObservableCollection<WPFDataTypes.Version>
    {
        private readonly string _cacheFile;
        private readonly string _versiondb;
        private readonly HttpClient _client = new HttpClient();
        private readonly WPFDataTypes.IVersionCommands _commands;
        private readonly PropertyChangedEventHandler _versionPropertyChangedEventHandler;

        public VersionList(string cacheFile, string versiondb, WPFDataTypes.IVersionCommands commands, PropertyChangedEventHandler versionPropertyChangedEventHandler)
        {
            _cacheFile = cacheFile;
            _versiondb = versiondb;
            _commands = commands;
            _versionPropertyChangedEventHandler = versionPropertyChangedEventHandler;
            CollectionChanged += VersionListOnCollectionChanged;
        }

        private void VersionListOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    var version = item as WPFDataTypes.Version;
                    version.PropertyChanged -= _versionPropertyChangedEventHandler;
                }
            }
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    var version = item as WPFDataTypes.Version;
                    version.PropertyChanged += _versionPropertyChangedEventHandler;
                }
            }
        }

        private void ParseList(JArray data, bool isCache)
        {
            Clear();

            foreach (JObject keys in data.Cast<JObject>())
            {
                bool isNew = !isCache;
                Add(new WPFDataTypes.Version((string)keys["Name"], (string)keys["Type"], (string)keys["UUID"], _commands, isNew));
            }
        }

        public async Task LoadFromCache()
        {
            try
            {
                using (StreamReader reader = File.OpenText(_cacheFile))
                {
                    string data = await reader.ReadToEndAsync();
                    ParseList(JArray.Parse(data), true);
                }
            }
            catch (FileNotFoundException)
            { // ignore
            }
        }

        public async Task DownloadList()
        {
            HttpResponseMessage resp = await _client.GetAsync(_versiondb);
            resp.EnsureSuccessStatusCode();
            string data = await resp.Content.ReadAsStringAsync();
            File.WriteAllText(_cacheFile, data);
            ParseList(JArray.Parse(data), false);
        }
    }
}
