using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace SmokeyVersionSwitcher
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading;
    using WPFDataTypes;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IVersionCommands
    {
        private readonly VersionList _versions;

        public MainWindow()
        {
            InitializeComponent();
            _versions = new VersionList("versions.json", this);
            VersionList.DataContext = _versions;
            Dispatcher.Invoke(async () =>
            {
                try
                {
                    await _versions.LoadFromCache();
                }
                catch (Exception e)
                {
                    Debug.WriteLine("List cache load failed:\n" + e.ToString());
                }
            });


        }

        public ICommand LaunchCommand => new RelayCommand((v) => InvokeLaunch((Version)v));
        public ICommand InstallCommand => new RelayCommand((v) => InvokeInstall((Version)v));
        public ICommand UninstallCommand => new RelayCommand((v) => InvokeUninstall((Version)v));

        private void InvokeLaunch(Version v)
        {
            MessageBox.Show("InvokeLaunch");
        }

        private void InvokeInstall(Version v)
        {
            MessageBox.Show("InvokeInstall");
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            v.DownloadInfo = new VersionDownloadInfo
            {
                IsInitializing = true,
                CancelCommand = new RelayCommand((o) => cancelSource.Cancel())
            };
            //TODO - Implement actual downloading
            string dl_path = "Minecraft-" + v.Name;
            Directory.CreateDirectory(dl_path);
            v.DownloadInfo = null;
            v.UpdateInstallStatus();
        }

        private void InvokeUninstall(Version v)
        {
            MessageBox.Show("InvokeUninstall");
            Directory.Delete(v.GameDirectory, true);
            v.UpdateInstallStatus();
        }
    }

    namespace WPFDataTypes
    {
        public class NotifyPropertyChangedBase : INotifyPropertyChanged
        {

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }

        }

        public interface IVersionCommands
        {
            ICommand LaunchCommand { get; }

            ICommand InstallCommand { get; }

            ICommand UninstallCommand { get; }
        }

        public class Version : NotifyPropertyChangedBase
        {
            public Version(string name, string type, IVersionCommands commands)
            {
                this.Name = name;
                this.Type = type;
                this.LaunchCommand = commands.LaunchCommand;
                this.InstallCommand = commands.InstallCommand;
                this.UninstallCommand = commands.UninstallCommand;
            }

            public string Name { get; set; }
            public string Type { get; set; }
            public string GameDirectory => "Minecraft-" + Name;
            public bool IsInstalled => Directory.Exists(GameDirectory);

            public ICommand LaunchCommand { get; set; }
            public ICommand InstallCommand { get; set; }
            public ICommand UninstallCommand { get; set; }

            private VersionDownloadInfo _downloadInfo;
            public VersionDownloadInfo DownloadInfo
            {
                get { return _downloadInfo; }
                set { _downloadInfo = value; OnPropertyChanged("DownloadInfo"); OnPropertyChanged("IsDownloading"); }
            }

            public bool IsDownloading => DownloadInfo != null;

            public void UpdateInstallStatus()
            {
                OnPropertyChanged("IsInstalled");
            }

        }

        public class VersionDownloadInfo : NotifyPropertyChangedBase
        {

            private bool _isInitializing;
            private bool _isExtracting;
            private long _downloadedBytes;
            private long _totalSize;

            public bool IsInitializing
            {
                get { return _isInitializing; }
                set { _isInitializing = value; OnPropertyChanged("IsProgressIndeterminate"); OnPropertyChanged("DisplayStatus"); }
            }

            public bool IsExtracting
            {
                get { return _isExtracting; }
                set { _isExtracting = value; OnPropertyChanged("IsProgressIndeterminate"); OnPropertyChanged("DisplayStatus"); }
            }

            public bool IsProgressIndeterminate
            {
                get { return IsInitializing || IsExtracting; }
            }

            public long DownloadedBytes
            {
                get { return _downloadedBytes; }
                set { _downloadedBytes = value; OnPropertyChanged("DownloadedBytes"); OnPropertyChanged("DisplayStatus"); }
            }

            public long TotalSize
            {
                get { return _totalSize; }
                set { _totalSize = value; OnPropertyChanged("TotalSize"); OnPropertyChanged("DisplayStatus"); }
            }

            public string DisplayStatus
            {
                get
                {
                    if (IsInitializing)
                        return "Downloading...";
                    if (IsExtracting)
                        return "Extracting...";
                    return "Downloading... " + (DownloadedBytes / 1024 / 1024) + "MiB/" + (TotalSize / 1024 / 1024) + "MiB";
                }
            }

            public ICommand CancelCommand { get; set; }

        }
    }
}