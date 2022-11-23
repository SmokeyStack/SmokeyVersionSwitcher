using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SmokeyVersionSwitcher
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Threading;
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
        private static readonly string minecraft_package_family = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";

        private readonly VersionList _versions;
        private readonly Downloader _anonVersionDownloader = new Downloader();
        private readonly Downloader _userVersionDownloader = new Downloader();
        private readonly Task _userVersionDownloaderLoginTask;
        private volatile int _userVersionDownloaderLoginTaskStarted;
        private volatile bool _has_launched = false;

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
            if (_has_launched)
                return;

            _has_launched = true;
            MessageBox.Show("InvokeLaunch");
            Task.Run(async () =>
            {
                v.StatusInfo = new Status(State.Registering);
                string gameDir = Path.GetFullPath(v.GameDirectory);
                try
                {
                    await ReRegisterPackage(gameDir);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("App re-register failed:\n" + e.ToString());
                    MessageBox.Show("App re-register failed:\n" + e.ToString());
                    _has_launched = false;
                    v.StatusInfo = null;
                    return;
                }
                v.StatusInfo = new Status(State.Launching);
                try
                {
                    var pkg = await AppDiagnosticInfo.RequestInfoForPackageAsync(minecraft_package_family);
                    if (pkg.Count > 0)
                        await pkg[0].LaunchAsync();
                    Debug.WriteLine("App launch finished!");
                    _has_launched = false;
                    v.StatusInfo = null;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("App launch failed:\n" + e.ToString());
                    MessageBox.Show("App launch failed:\n" + e.ToString());
                    _has_launched = false;
                    v.StatusInfo = null;
                    return;
                }
            });

        }

        private void InvokeInstall(Version v)
        {
            MessageBox.Show("InvokeInstall");
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            v.StatusInfo = new Status(State.Initializing);
            Debug.WriteLine("Download start");

            Task.Run(async () =>
            {
                string dlPath = "Minecraft-" + v.Name + ".Appx";
                Downloader downloader = _anonVersionDownloader;

                if (v.Type == "Beta")
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
                        MessageBox.Show("Failed to authenticate because: " + e.Message + "\nPlease make sure your account is subscribed to the beta programme.\n\n" + e.ToString(), "Authentication failed");
                        v.StatusInfo = null;
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Authentication failed:\n" + e.ToString());
                        MessageBox.Show(e.ToString(), "Authentication failed");
                        v.StatusInfo = null;
                        return;
                    }
                }

                try
                {
                    await downloader.Download(v.UUID, "1", dlPath, (current, total) =>
                    {
                        if (v.StatusInfo.State != State.Installing)
                        {
                            Debug.WriteLine("Actual download started");
                            v.StatusInfo.State = State.Installing;

                            if (total.HasValue)
                                v.StatusInfo.TotalBytes = total.Value;
                        }

                        v.StatusInfo.DownloadedBytes = current;
                    }, cancelSource.Token);
                    Debug.WriteLine("Download complete");
                }
                catch (BadUpdateIdentityException)
                {
                    Debug.WriteLine("Download failed due to failure to fetch download URL");
                    MessageBox.Show(
                        "Unable to fetch download URL for version." +
                        (v.Type == "Beta" ? "\nFor beta versions, please make sure your account is subscribed to the Minecraft beta programme in the Xbox Insider Hub app." : "")
                    );
                    v.StatusInfo = null;
                    return;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Download failed:\n" + e.ToString());

                    if (!(e is TaskCanceledException))
                        MessageBox.Show("Download failed:\n" + e.ToString());

                    v.StatusInfo = null;
                    return;
                }

                try
                {
                    v.StatusInfo.State = State.Extracting;
                    string dirPath = v.GameDirectory;

                    if (Directory.Exists(dirPath))
                        Directory.Delete(dirPath, true);

                    ZipFile.ExtractToDirectory(dlPath, dirPath);
                    v.StatusInfo = null;
                    File.Delete(Path.Combine(dirPath, "AppxSignature.p7x"));
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Extraction failed:\n" + e.ToString());
                    MessageBox.Show("Extraction failed:\n" + e.ToString());
                    v.StatusInfo = null;
                    return;
                }
                v.StatusInfo = null;
                v.UpdateInstallStatus();
            });
        }

        private void InvokeUninstall(Version v)
        {
            MessageBox.Show("InvokeUninstall");
            Directory.Delete(v.GameDirectory, true);
            v.UpdateInstallStatus();
        }

        private async Task ReRegisterPackage(string gameDir)
        {
            foreach (var pkg in new PackageManager().FindPackages(minecraft_package_family))
            {
                string location = GetPackagePath(pkg);
                if (location == gameDir)
                {
                    Debug.WriteLine("Skipping package removal - same path: " + pkg.Id.FullName + " " + location);
                    return;
                }
                await RemovePackage(pkg);
            }
            Debug.WriteLine("Registering package");
            string manifestPath = Path.Combine(gameDir, "AppxManifest.xml");
            await DeploymentProgressWrapper(new PackageManager().RegisterPackageAsync(new Uri(manifestPath), null, DeploymentOptions.DevelopmentMode));
            Debug.WriteLine("App re-register done!");
            RestoreMinecraftDataFromReinstall();
        }

        private string GetPackagePath(Package pkg)
        {
            try
            {
                return pkg.InstalledLocation.Path;
            }
            catch (FileNotFoundException)
            {
                return "";
            }
        }

        private async Task RemovePackage(Package pkg)
        {
            Debug.WriteLine("Removing package: " + pkg.Id.FullName);
            if (!pkg.IsDevelopmentMode)
            {
                BackupMinecraftDataForRemoval();
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, 0));
            }
            else
            {
                Debug.WriteLine("Package is in development mode");
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, RemovalOptions.PreserveApplicationData));
            }
            Debug.WriteLine("Removal of package done: " + pkg.Id.FullName);
        }

        private async Task DeploymentProgressWrapper(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> t)
        {
            TaskCompletionSource<int> src = new TaskCompletionSource<int>();
            t.Progress += (v, p) =>
            {
                Debug.WriteLine("Deployment progress: " + p.state + " " + p.percentage + "%");
            };
            t.Completed += (v, p) =>
            {
                if (p == AsyncStatus.Error)
                {
                    Debug.WriteLine("Deployment failed: " + v.GetResults().ErrorText);
                    src.SetException(new Exception("Deployment failed: " + v.GetResults().ErrorText));
                }
                else
                {
                    Debug.WriteLine("Deployment done: " + p);
                    src.SetResult(1);
                }
            };
            await src.Task;
        }

        private string GetBackupMinecraftDataDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string tmpDir = Path.Combine(localAppData, "TmpMinecraftLocalState");
            return tmpDir;
        }

        private void BackupMinecraftDataForRemoval()
        {
            var data = ApplicationDataManager.CreateForPackageFamily(minecraft_package_family);
            string tmpDir = GetBackupMinecraftDataDir();
            if (Directory.Exists(tmpDir))
            {
                Debug.WriteLine("BackupMinecraftDataForRemoval error: " + tmpDir + " already exists");
                Process.Start("explorer.exe", tmpDir);
                MessageBox.Show("The temporary directory for backing up MC data already exists. This probably means that we failed last time backing up the data. Please back the directory up manually.");
                throw new Exception("Temporary dir exists");
            }
            Debug.WriteLine("Moving Minecraft data to: " + tmpDir);
            Directory.Move(data.LocalFolder.Path, tmpDir);
        }


        private void RestoreMinecraftDataFromReinstall()
        {
            string tmpDir = GetBackupMinecraftDataDir();
            if (!Directory.Exists(tmpDir))
                return;
            var data = ApplicationDataManager.CreateForPackageFamily(minecraft_package_family);
            Debug.WriteLine("Moving backup Minecraft data to: " + data.LocalFolder.Path);
            RestoreMove(tmpDir, data.LocalFolder.Path);
            Directory.Delete(tmpDir, true);
        }

        private void RestoreMove(string from, string to)
        {
            foreach (var f in Directory.EnumerateFiles(from))
            {
                string ft = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(ft))
                {
                    if (MessageBox.Show("The file " + ft + " already exists in the destination.\nDo you want to replace it? The old file will be lost otherwise.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    File.Delete(ft);
                }
                File.Move(f, ft);
            }
            foreach (var f in Directory.EnumerateDirectories(from))
            {
                string tp = Path.Combine(to, Path.GetFileName(f));
                if (!Directory.Exists(tp))
                {
                    if (File.Exists(tp) && MessageBox.Show("The file " + tp + " is not a directory. Do you want to remove it? The data from the old directory will be lost otherwise.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    Directory.CreateDirectory(tp);
                }
                RestoreMove(f, tp);
            }
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
            private Status _status_info;
            public Status StatusInfo
            {
                get { return _status_info; }
                set { _status_info = value; OnPropertyChanged("StatusInfo"); OnPropertyChanged("IsStatusChanging"); }
            }
            public bool IsStatusChanging => StatusInfo != null;

            public void UpdateInstallStatus()
            {
                OnPropertyChanged("IsInstalled");
            }

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
            private long _downloaded_bytes;
            private long _total_bytes;
            public Status(State state)
            {
                _state = state;
            }
            public State State
            {
                get { return _state; }
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
                        case State.Initializing:
                        case State.Extracting:
                        case State.Uninstalling:
                        case State.Registering:
                        case State.Launching:
                            return true;
                        default: return false;
                    }
                }
            }
            public long DownloadedBytes
            {
                get { return _downloaded_bytes; }
                set { _downloaded_bytes = value; OnPropertyChanged("DownloadedBytes"); OnPropertyChanged("DisplayStatus"); }
            }
            public long TotalBytes
            {
                get { return _total_bytes; }
                set { _total_bytes = value; OnPropertyChanged("TotalBytes"); OnPropertyChanged("DisplayStatus"); }
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
                        case State.Registering: return "Regestering...";
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