using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmokeyVersionSwitcher
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading;

    public class Foo
    {
        public string Section { get; set; }
    }

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
            _versions.Add(new Foo() { Section = "Chiang" });
            BetaVersionList.DataContext = _versions;
        }
    }
}