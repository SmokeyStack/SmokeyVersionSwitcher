using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace SmokeyVersionSwitcher
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Threading;
    using WPFDataTypes;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IVersionCommands
    {
        private readonly VersionList _versions;
        private readonly Downloader _anonVersionDownloader = new Downloader();
        private readonly Downloader _userVersionDownloader = new Downloader();
        private readonly Task _userVersionDownloaderLoginTask;
        private volatile int _userVersionDownloaderLoginTaskStarted;

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

        private void MenuItemOpenLogFileClicked(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(@"Log.txt"))
                MessageBox.Show("Log file not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            else
                Process.Start(@"Log.txt");
        }

        private void MenuItemOpenDataDirClicked(object sender, RoutedEventArgs e)
        {
            Process.Start(@"explorer.exe", Directory.GetCurrentDirectory());
        }

        public ICommand LaunchCommand => new RelayCommand((v) => InvokeLaunch((Version)v));
        public ICommand InstallCommand => new RelayCommand((v) => InvokeInstall((Version)v));
        public ICommand UninstallCommand => new RelayCommand((v) => InvokeUninstall((Version)v));

        private void InvokeLaunch(Version v)
        {
            MessageBox.Show("InvokeLaunch");
            Debug.WriteLine(v.Name);
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
            Debug.WriteLine("Download start");

            Task.Run(async () =>
            {
                string dlPath = "Minecraft-" + v.Name + ".Appx";
                Downloader downloader = _anonVersionDownloader;

                if (v.Type == "Beta")
                {
                    downloader = _userVersionDownloader;
                    if (Interlocked.CompareExchange(ref _userVersionDownloaderLoginTaskStarted, 1, 0) == 0)
                        _userVersionDownloaderLoginTask.Start();

                    await _userVersionDownloaderLoginTask;
                }

                try
                {
                    await downloader.Download(v.UUID, "1", dlPath, (current, total) =>
                    {
                        if (v.DownloadInfo.IsInitializing)
                        {
                            Debug.WriteLine("Actual download started");
                            v.DownloadInfo.IsInitializing = false;

                            if (total.HasValue)
                                v.DownloadInfo.TotalSize = total.Value;
                        }

                        v.DownloadInfo.DownloadedBytes = current;
                    }, cancelSource.Token);
                    Debug.WriteLine("Download complete");
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Download failed:\n" + e.ToString());

                    if (!(e is TaskCanceledException))
                        MessageBox.Show("Download failed:\n" + e.ToString());

                    v.DownloadInfo = null;
                    return;
                }

                try
                {
                    v.DownloadInfo.IsExtracting = true;
                    string dirPath = v.GameDirectory;

                    if (Directory.Exists(dirPath))
                        Directory.Delete(dirPath, true);

                    ZipFile.ExtractToDirectory(dlPath, dirPath);
                    v.DownloadInfo = null;
                    File.Delete(Path.Combine(dirPath, "AppxSignature.p7x"));
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Extraction failed:\n" + e.ToString());
                    MessageBox.Show("Extraction failed:\n" + e.ToString());
                    v.DownloadInfo = null;
                    return;
                }
                v.DownloadInfo = null;
                v.UpdateInstallStatus();
            });
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
            public Version(string name, string type, string uuid, IVersionCommands commands)
            {
                this.Name = name;
                this.Type = type;
                this.UUID = uuid;
                this.LaunchCommand = commands.LaunchCommand;
                this.InstallCommand = commands.InstallCommand;
                this.UninstallCommand = commands.UninstallCommand;
            }

            public string Name { get; set; }
            public string Type { get; set; }
            public string UUID { get; set; }
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

        //public class Test : NotifyPropertyChangedBase
        //{
        //    public ResourceDictionaryLocation SelectedDefaultLocationListItem = new Version("UwU", "Type", "UUID", MainWindow.IVersionCommands));
        //    public ObservableCollection<string> Items { get; set; }
        //    public string ItemToAdd { get; set; }
        //    private string selectedItem { get; set; }
        //    public string SelectedItem
        //    {
        //        get { return selectedItem; }
        //        set { selectedItem = value; OnPropertyChanged("SelectedItem"); }
        //    }

        //    public void Addnew()
        //    {
        //        this.Items.Add("UwU");
        //        this.SelectedItem = "UwU";
        //    }
        //}
    }
}