using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;

namespace SmokeyVersionSwitcher
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Threading;
    using System.Windows.Data;
    using Windows.ApplicationModel;
    using Windows.Foundation;
    using Windows.Management.Core;
    using Windows.Management.Deployment;
    using Windows.System;
    using WPFDataTypes;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IVersionCommands
    {
        public Preferences UserPreferences { get; }
        private static readonly string PREFERENCES = @"preferences.json";
        private static readonly string VERSION_DB = "https://raw.githubusercontent.com/SmokeyStack/versiondb/main/versions.json";
        private readonly VersionList _versions;
        private readonly HashSet<CollectionViewSource> _versionsList = new HashSet<CollectionViewSource>();
        private readonly Downloader _anonVersionDownloader = new Downloader();
        private readonly Downloader _userVersionDownloader = new Downloader();
        private readonly Task _userVersionDownloaderLoginTask;
        private volatile int _userVersionDownloaderLoginTaskStarted;
        private volatile bool _hasLaunched = false;

        public MainWindow()
        {
            if (File.Exists(PREFERENCES))
            {
                UserPreferences = JsonConvert.DeserializeObject<Preferences>(File.ReadAllText(PREFERENCES));
            }
            else
            {
                UserPreferences = new Preferences();
                RewritePreferences();
            }

            _versions = new VersionList("versions.json", VERSION_DB, this, VersionEntryPropertyChanged);
            InitializeComponent();
            ShowInstalledVersionsCheckbox.DataContext = this;
            CollectionViewSource versionListRelease = Resources["versionListRelease"] as CollectionViewSource;
            CollectionViewSource versionListBeta = Resources["versionListBeta"] as CollectionViewSource;
            CollectionViewSource versionListPreview = Resources["versionListPreview"] as CollectionViewSource;
            CollectionViewSource versionListInstalled = Resources["versionListInstalled"] as CollectionViewSource;
            versionListRelease.Filter += new FilterEventHandler((object sender, FilterEventArgs e) =>
            {
                Version version = e.Item as Version;
                e.Accepted = version.Type == "Release" && (version.IsInstalled || version.IsStatusChanging || !(ShowInstalledVersionsCheckbox.IsChecked ?? false));
            });
            versionListBeta.Filter += new FilterEventHandler((object sender, FilterEventArgs e) =>
            {
                Version version = e.Item as Version;
                e.Accepted = version.Type == "Beta" && (version.IsInstalled || version.IsStatusChanging || !(ShowInstalledVersionsCheckbox.IsChecked ?? false));
            });
            versionListPreview.Filter += new FilterEventHandler((object sender, FilterEventArgs e) =>
            {
                Version version = e.Item as Version;
                e.Accepted = version.Type == "Preview" && (version.IsInstalled || version.IsStatusChanging || !(ShowInstalledVersionsCheckbox.IsChecked ?? false));
            });
            versionListInstalled.Filter += new FilterEventHandler((object sender, FilterEventArgs e) =>
            {
                Version version = e.Item as Version;
                e.Accepted = version.IsInstalled || version.IsStatusChanging;
            });
            versionListRelease.Source = _versions;
            VersionListRelease.DataContext = versionListRelease;
            _ = _versionsList.Add(versionListRelease);
            versionListBeta.Source = _versions;
            VersionListBeta.DataContext = versionListBeta;
            _ = _versionsList.Add(versionListBeta);
            versionListPreview.Source = _versions;
            VersionListPreview.DataContext = versionListPreview;
            _ = _versionsList.Add(versionListPreview);
            versionListInstalled.Source = _versions;
            VersionListInstalled.DataContext = versionListInstalled;
            _userVersionDownloaderLoginTask = new Task(() =>
            {
                _userVersionDownloader.EnableUserAuthorization();
            });
            Dispatcher.Invoke(LoadVersionList);
        }

        public ICommand InstallCommand => new RelayCommand((v) => InvokeInstall((Version)v));

        public ICommand UninstallCommand => new RelayCommand((v) => InvokeUninstall((Version)v));

        private async void LoadVersionList()
        {
            LoadingProgressLabel.Content = "Loading versions from cache";
            LoadingProgressBar.Value = 1;
            LoadingProgressGrid.Visibility = Visibility.Visible;

            try
            {
                await _versions.LoadFromCache();
            }
            catch (Exception e)
            {
                Debug.WriteLine("List cache load failed:\n" + e.ToString());
            }

            LoadingProgressLabel.Content = "Updating versions list from " + VERSION_DB;
            LoadingProgressBar.Value = 2;

            try
            {
                await _versions.DownloadList();
            }
            catch (Exception e)
            {
                Debug.WriteLine("List download failed:\n" + e.ToString());
                _ = MessageBox.Show("Failed to update version list from the internet. Some new versions might be missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadingProgressGrid.Visibility = Visibility.Collapsed;
            VersionListInstalled.SelectedIndex = 0;
        }

        private void VersionEntryPropertyChanged(object sender, PropertyChangedEventArgs e) => RefreshVersionLists();

        private void InvokeLaunch(object sender, RoutedEventArgs args)
        {
            Version version = (Version)VersionListInstalled.SelectedItem;

            if (_hasLaunched)
            {
                return;
            }

            _hasLaunched = true;
            _ = Task.Run(async () =>
            {
                version.StatusInfo = new Status(State.Registering);
                string gameDirectory = Path.GetFullPath(version.GameDirectory);

                try
                {
                    await ReRegisterPackage(version.GamePackageFamily, gameDirectory);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("App re-register failed:\n" + e.ToString());
                    _ = MessageBox.Show("App re-register failed:\n" + e.ToString());
                    _hasLaunched = false;
                    version.StatusInfo = null;
                    return;
                }

                version.StatusInfo = new Status(State.Launching);

                try
                {
                    IList<AppDiagnosticInfo> package = await AppDiagnosticInfo.RequestInfoForPackageAsync(version.GamePackageFamily);

                    if (package.Count > 0)
                    {
                        _ = await package[0].LaunchAsync();
                    }

                    Debug.WriteLine("App launch finished!");
                    _hasLaunched = false;
                    version.StatusInfo = null;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("App launch failed:\n" + e.ToString());
                    _ = MessageBox.Show("App launch failed:\n" + e.ToString());
                    _hasLaunched = false;
                    version.StatusInfo = null;
                    return;
                }
            });
        }

        private async Task DeploymentProgressWrapper(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> t)
        {
            TaskCompletionSource<int> source = new TaskCompletionSource<int>();
            t.Progress += (v, p) =>
            {
                Debug.WriteLine("Deployment progress: " + p.state + " " + p.percentage + "%");
            };
            t.Completed += (v, p) =>
            {
                if (p == AsyncStatus.Error)
                {
                    Debug.WriteLine("Deployment failed: " + v.GetResults().ErrorText);
                    source.SetException(new Exception("Deployment failed: " + v.GetResults().ErrorText));
                }
                else
                {
                    Debug.WriteLine("Deployment done: " + p);
                    source.SetResult(1);
                }
            };
            _ = await source.Task;
        }

        private string GetBackupMinecraftDataDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string tmpDir = Path.Combine(localAppData, "TmpSmokeyStackMinecraftLocalState");
            return tmpDir;
        }

        private void BackupMinecraftDataForRemoval(string packageFamily)
        {
            Windows.Storage.ApplicationData data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            string tempDirectory = GetBackupMinecraftDataDirectory();

            if (Directory.Exists(tempDirectory))
            {
                Debug.WriteLine("BackupMinecraftDataForRemoval error: " + tempDirectory + " already exists");
                _ = Process.Start("explorer.exe", tempDirectory);
                _ = MessageBox.Show("The temporary directory for backing up MC data already exists. This probably means that we failed last time backing up the data. Please back the directory up manually.");
                throw new Exception("Temporary dir exists");
            }

            Debug.WriteLine("Moving Minecraft data to: " + tempDirectory);
            Directory.Move(data.LocalFolder.Path, tempDirectory);
        }

        private void RestoreMove(string from, string to)
        {
            foreach (string f in Directory.EnumerateFiles(from))
            {
                string ft = Path.Combine(to, Path.GetFileName(f));

                if (File.Exists(ft))
                {
                    if (MessageBox.Show("The file " + ft + " already exists in the destination.\nDo you want to replace it? The old file will be lost otherwise.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    {
                        continue;
                    }

                    File.Delete(ft);
                }

                File.Move(f, ft);
            }
            foreach (string f in Directory.EnumerateDirectories(from))
            {
                string tp = Path.Combine(to, Path.GetFileName(f));
                if (!Directory.Exists(tp))
                {
                    if (File.Exists(tp) && MessageBox.Show("The file " + tp + " is not a directory. Do you want to remove it? The data from the old directory will be lost otherwise.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    {
                        continue;
                    }

                    _ = Directory.CreateDirectory(tp);
                }

                RestoreMove(f, tp);
            }
        }

        private void RestoreMinecraftDataFromReinstall(string packageFamily)
        {
            string tempDirectory = GetBackupMinecraftDataDirectory();

            if (!Directory.Exists(tempDirectory))
            {
                return;
            }

            Windows.Storage.ApplicationData data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            Debug.WriteLine("Moving backup Minecraft data to: " + data.LocalFolder.Path);
            RestoreMove(tempDirectory, data.LocalFolder.Path);
            Directory.Delete(tempDirectory, true);
        }

        private async Task RemovePackage(Package pacakge, string packageFamily)
        {
            Debug.WriteLine("Removing package: " + pacakge.Id.FullName);

            if (!pacakge.IsDevelopmentMode)
            {
                BackupMinecraftDataForRemoval(packageFamily);
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pacakge.Id.FullName, 0));
            }
            else
            {
                Debug.WriteLine("Package is in development mode");
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pacakge.Id.FullName, RemovalOptions.PreserveApplicationData));
            }

            Debug.WriteLine("Removal of package done: " + pacakge.Id.FullName);
        }

        private string GetPackagePath(Package package)
        {
            try
            {
                return package.InstalledLocation.Path;
            }
            catch (FileNotFoundException)
            {
                return "";
            }
        }

        private async Task UnregisterPackage(string packageFamily, string gameDirectory)
        {
            foreach (Package package in new PackageManager().FindPackages(packageFamily))
            {
                string location = GetPackagePath(package);

                if (location == "" || location == gameDirectory)
                {
                    await RemovePackage(package, packageFamily);
                }
            }
        }

        private async Task ReRegisterPackage(string packageFamily, string gameDirectory)
        {
            foreach (Package package in new PackageManager().FindPackages(packageFamily))
            {
                string location = GetPackagePath(package);

                if (location == gameDirectory)
                {
                    Debug.WriteLine("Skipping package removal - same path: " + package.Id.FullName + " " + location);
                    return;
                }

                await RemovePackage(package, packageFamily);
            }

            Debug.WriteLine("Registering package");
            string manifestPath = Path.Combine(gameDirectory, "AppxManifest.xml");
            await DeploymentProgressWrapper(new PackageManager().RegisterPackageAsync(new Uri(manifestPath), null, DeploymentOptions.DevelopmentMode));
            Debug.WriteLine("App re-register done!");
            RestoreMinecraftDataFromReinstall(packageFamily);
        }

        private void InvokeInstall(Version version)
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            version.IsNew = false;
            version.StatusInfo = new Status(State.Initializing)
            {
                CancelCommand = new RelayCommand((o) => cancelSource.Cancel())
            };
            Debug.WriteLine("Download start");

            _ = Task.Run(async () =>
            {
                string downloadPath = (version.Type == "Preview" ? "Minecraft-Preview-" : "Minecraft-") + version.Name + ".Appx";
                Downloader downloader = _anonVersionDownloader;

                if (version.Type == "Beta")
                {
                    downloader = _userVersionDownloader;

                    if (Interlocked.CompareExchange(ref _userVersionDownloaderLoginTaskStarted, 1, 0) == 0)
                    {
                        _userVersionDownloaderLoginTask.Start();
                    }

                    Debug.WriteLine("Waiting for authentication");

                    try
                    {
                        await _userVersionDownloaderLoginTask;
                        Debug.WriteLine("Authentication complete");
                    }
                    catch (WUTokenHelper.WUTokenException e)
                    {
                        Debug.WriteLine("Authentication failed:\n" + e.ToString());
                        _ = MessageBox.Show("Failed to authenticate because: " + e.Message, "Authentication failed");
                        version.StatusInfo = null;
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Authentication failed:\n" + e.ToString());
                        _ = MessageBox.Show(e.ToString(), "Authentication failed");
                        version.StatusInfo = null;
                        return;
                    }
                }

                try
                {
                    await downloader.Download(version.UUID, "1", downloadPath, (current, total) =>
                    {
                        if (version.StatusInfo.State != State.Installing)
                        {
                            Debug.WriteLine("Actual download started");
                            version.StatusInfo.State = State.Installing;

                            if (total.HasValue)
                            {
                                version.StatusInfo.TotalBytes = total.Value;
                            }
                        }

                        version.StatusInfo.DownloadedBytes = current;
                    }, cancelSource.Token);
                    Debug.WriteLine("Download complete");
                }
                catch (BadUpdateIdentityException)
                {
                    Debug.WriteLine("Download failed due to failure to fetch download URL");
                    _ = MessageBox.Show(
                        "Unable to fetch download URL for version." +
                        (version.Type == "Beta" ? "\nFor beta versions, please make sure your account is subscribed to the Minecraft beta programme in the Xbox Insider Hub app." : "")
                    );
                    version.StatusInfo = null;
                    return;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Download failed:\n" + e.ToString());

                    if (!(e is TaskCanceledException))
                    {
                        _ = MessageBox.Show("Download failed:\n" + e.ToString());
                    }

                    version.StatusInfo = null;
                    return;
                }

                try
                {
                    version.StatusInfo.State = State.Extracting;
                    string directoryPath = version.GameDirectory;

                    if (Directory.Exists(directoryPath))
                    {
                        Directory.Delete(directoryPath, true);
                    }

                    ZipFile.ExtractToDirectory(downloadPath, directoryPath);
                    version.StatusInfo = null;
                    File.Delete(Path.Combine(directoryPath, "AppxSignature.p7x"));
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Extraction failed:\n" + e.ToString());
                    _ = MessageBox.Show("Extraction failed:\n" + e.ToString());
                    version.StatusInfo = null;
                    return;
                }
                version.StatusInfo = null;
                version.UpdateInstallStatus();
                Dispatcher.Invoke(LoadVersionList);
            });
        }

        private async Task Remove(Version version)
        {
            version.StatusInfo = new Status(State.Uninstalling);
            await UnregisterPackage(version.GamePackageFamily, Path.GetFullPath(version.GameDirectory));
            Directory.Delete(version.GameDirectory, true);
            version.StatusInfo = null;
            version.UpdateInstallStatus();
            Debug.WriteLine("Removed version " + version.Name);
            Dispatcher.Invoke(LoadVersionList);
        }

        private void InvokeUninstall(Version version)
        {
            _ = Task.Run(async () => await Remove(version));
        }

        private void ShowInstalledVersionsCheckboxChanged(object sender, RoutedEventArgs e)
        {
            UserPreferences.ShowInstalledVersions = ShowInstalledVersionsCheckbox.IsChecked ?? false;
            RefreshVersionLists();
            RewritePreferences();
        }

        private void RefreshVersionLists()
        {
            Dispatcher.Invoke(() =>
            {
                foreach (CollectionViewSource source in _versionsList)
                {
                    source.View.Refresh();
                }
            });
        }

        private void RewritePreferences()
        {
            File.WriteAllText(PREFERENCES, JsonConvert.SerializeObject(UserPreferences));
        }

        private void MenuItemOpenLogFileClicked(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(@"Log.txt"))
            {
                _ = MessageBox.Show("Log file not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                _ = Process.Start(@"Log.txt");
            }
        }

        private void MenuItemOpenDataDirectoryClicked(object sender, RoutedEventArgs e) => Process.Start(@"explorer.exe", Directory.GetCurrentDirectory());

        private void MenuItemRefreshVersionListClicked(object sender, RoutedEventArgs e) => Dispatcher.Invoke(LoadVersionList);
    }

    namespace WPFDataTypes
    {
        public class NotifyPropertyChangedBase : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public interface IVersionCommands
        {
            ICommand InstallCommand { get; }
            ICommand UninstallCommand { get; }
        }

        public class Version : NotifyPropertyChangedBase
        {
            public Version(string name, string type, string uuid, IVersionCommands commands, bool isNew)
            {
                Name = name;
                Type = type;
                UUID = uuid;
                IsNew = isNew;
                InstallCommand = commands.InstallCommand;
                UninstallCommand = commands.UninstallCommand;
                GameDirectory = (type == "Preview" ? "Minecraft-Preview-" : "Minecraft-") + Name;
            }

            public string Name { get; set; }
            public string Type { get; set; }
            public string UUID { get; set; }
            public bool IsNew
            {
                get { return _isNew; }
                set
                {
                    _isNew = value;
                    OnPropertyChanged("IsNew");
                }
            }
            public string GameDirectory { get; set; }
            public string GamePackageFamily => Type == "Preview" ? MinecraftPackageFamilies.MinecraftPreview : MinecraftPackageFamilies.Minecraft;
            public bool IsInstalled => Directory.Exists(GameDirectory);
            public ICommand InstallCommand { get; set; }
            public ICommand UninstallCommand { get; set; }
            public Status StatusInfo
            {
                get => _statusInfo;
                set { _statusInfo = value; OnPropertyChanged("StatusInfo"); OnPropertyChanged("IsStatusChanging"); }
            }
            public bool IsStatusChanging => StatusInfo != null;
            public void UpdateInstallStatus() => OnPropertyChanged("IsInstalled");
            private Status _statusInfo;
            private bool _isNew = false;
        }

        public enum State
        {
            Initializing,
            Installing,
            Extracting,
            Registering,
            Launching,
            Uninstalling
        }

        public class Status : NotifyPropertyChangedBase
        {
            private State _state;
            private long _downloadedBytes;
            private long _totalBytes;
            public Status(State state) => _state = state;
            public State State
            {
                get => _state;
                set
                {
                    _state = value;
                    OnPropertyChanged("IsProgressIndeterminate");
                    OnPropertyChanged("DisplayStatus");
                }
            }
            public bool IsProgressIndeterminate
            {
                get
                {
                    switch (_state)
                    {
                        case State.Launching:
                            return true;
                        default: return false;
                    }
                }
            }
            public long DownloadedBytes
            {
                get => _downloadedBytes;
                set { _downloadedBytes = value; OnPropertyChanged("DownloadedBytes"); OnPropertyChanged("DisplayStatus"); }
            }
            public long TotalBytes
            {
                get => _totalBytes;
                set { _totalBytes = value; OnPropertyChanged("TotalBytes"); OnPropertyChanged("DisplayStatus"); }
            }
            public string DisplayStatus
            {
                get
                {
                    switch (_state)
                    {
                        case State.Initializing: return "Preparing...";
                        case State.Installing:
                            return "Downloading... " + (DownloadedBytes / 1024 / 1024) + "MiB/" + (TotalBytes / 1024 / 1024) + "MiB";
                        case State.Extracting: return "Extracting...";
                        case State.Registering: return "Registering...";
                        case State.Launching: return "Launching...";
                        case State.Uninstalling: return "Uninstalling...";
                        default: return "This shouldn't happen...";
                    }
                }
            }
            public ICommand CancelCommand { get; set; }
        }
    }
}