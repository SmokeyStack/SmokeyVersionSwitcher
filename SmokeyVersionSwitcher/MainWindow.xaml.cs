using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmokeyVersionSwitcher
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<Foo> _versions;
        public MainWindow()
        {
            InitializeComponent();
            _versions = new List<Foo>();
            //_versions.Add(new Foo() { Section = "Chiang" });
            //BetaVersionList.DataContext = _versions;
            try
            {
                //JsonSerializer serializer = new JsonSerializer();
                //Foo foo = (Foo)serializer.Deserialize(reader, typeof(Foo));
                JArray jArray = JsonConvert.DeserializeObject<JArray>(File.ReadAllText("versions.json"));
                foreach (JObject keys in jArray)
                {
                    MessageBox.Show((string)keys["Name"]);
                    _versions.Add(new Foo() { Name = (string)keys["Name"], Type = (string)keys["Type"] });
                }
                VersionList.DataContext = _versions;
            }
            catch (Exception)
            {
            }
        }
    }

    public class Foo
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }
}