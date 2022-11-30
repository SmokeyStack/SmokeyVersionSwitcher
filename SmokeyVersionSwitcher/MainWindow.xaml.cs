using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SmokeyVersionSwitcher
{
    using System.ComponentModel;
    using System.Collections.ObjectModel;
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

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IVersionCommands
    {
        private static readonly string VERSIONDB = "https://raw.githubusercontent.com/SmokeyStack/versiondb/main/versions.json";

        private readonly VersionList _versions;
        private readonly Downloader _anonVersionDownloader = new Downloader();
        private readonly Downloader _userVersionDownloader = new Downloader();
        private readonly Task _userVersionDownloaderLoginTask;
        private volatile int _userVersionDownloaderLoginTaskStarted;
        private volatile bool _hasLaunched = false;
        private readonly ObservableCollection<Version> _installedVersions = new ObservableCollection<Version> { };

        public MainWindow()
        {
            InitializeComponent();

            InstalledList.DataContext = _installedVersions;
            InstalledList.SelectedIndex = 0;

            _versions = new VersionList("versions.json", VERSIONDB, this);
            VersionList.DataContext = _versions;

            Debug.WriteLine("UwU");
            _userVersionDownloaderLoginTask = new Task(() =>
            {
                _userVersionDownloader.EnableUserAuthorization();
            });

            try
            {
                TextTest.DataContext = "Whooo";
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }

            Dispatcher.Invoke(LoadVersionList);

            foreach (Version v in _versions)
            {
                if (v.IsInstalled)
                {
                    _installedVersions.Add(v);
                }
            }
        }

        private void UpdateStatus(Version v)
        {
            if (v.StatusInfo == null)
            {
                VersionLoadingProgressLabel.Content = "";
                return;
            }

            VersionLoadingProgressLabel.Content = v.StatusInfo.DisplayStatus;
        }

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

            LoadingProgressLabel.Content = "Updating versions list from " + VERSIONDB;
            LoadingProgressBar.Value = 2;

            try
            {
                await _versions.DownloadList();
            }
            catch (Exception e)
            {
                Debug.WriteLine("List download failed:\n" + e.ToString());
                MessageBox.Show("Failed to update version list from the internet. Some new versions might be missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadingProgressGrid.Visibility = Visibility.Collapsed;
        }

        private void MenuItemRefreshVersionListClicked(object sender, RoutedEventArgs e) => Dispatcher.Invoke(LoadVersionList);

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

        private void MenuItemOpenDataDirClicked(object sender, RoutedEventArgs e) => Process.Start(@"explorer.exe", Directory.GetCurrentDirectory());

        public ICommand LaunchCommand => new RelayCommand((v) => InvokeLaunch((Version)v));
        public ICommand InstallCommand => new RelayCommand((v) => InvokeInstall((Version)v));
        public ICommand UninstallCommand => new RelayCommand((v) => InvokeUninstall((Version)v));

        private void Test(object sender, RoutedEventArgs e)
        {
            Version v = (Version)InstalledList.SelectedItem;

            if (_hasLaunched)
            {
                return;
            }

            _hasLaunched = true;
            _ = Task.Run(async () =>
            {
                v.StatusInfo = new Status(State.Registering);
                Dispatcher.Invoke(new Action(() => UpdateStatus(v)));
                string gameDir = Path.GetFullPath(v.GameDirectory);

                try
                {
                    await ReRegisterPackage(v.GamePackageFamily, gameDir);
                }
                catch (Exception ee)
                {
                    Debug.WriteLine("App re-register failed:\n" + ee.ToString());
                    _ = MessageBox.Show("App re-register failed:\n" + ee.ToString());
                    _hasLaunched = false;
                    v.StatusInfo = null;
                    Dispatcher.Invoke(new Action(() => UpdateStatus(v)));
                    return;
                }

                v.StatusInfo = new Status(State.Launching);
                Dispatcher.Invoke(new Action(() => UpdateStatus(v)));

                try
                {
                    System.Collections.Generic.IList<AppDiagnosticInfo> pkg = await AppDiagnosticInfo.RequestInfoForPackageAsync(v.GamePackageFamily);

                    if (pkg.Count > 0)
                    {
                        _ = await pkg[0].LaunchAsync();
                    }

                    Debug.WriteLine("App launch finished!");
                    _hasLaunched = false;
                    v.StatusInfo = null;
                    Dispatcher.Invoke(new Action(() => UpdateStatus(v)));
                }
                catch (Exception eee)
                {
                    Debug.WriteLine("App launch failed:\n" + eee.ToString());
                    _ = MessageBox.Show("App launch failed:\n" + eee.ToString());
                    _hasLaunched = false;
                    v.StatusInfo = null;
                    Dispatcher.Invoke(new Action(() => UpdateStatus(v)));
                    return;
                }
            });
        }

        private void InvokeInstall(Version v)
        {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            v.StatusInfo = new Status(State.Initializing)
            {
                CancelCommand = new RelayCommand((o) => cancelSource.Cancel())
            };
            Debug.WriteLine("Download start");

            _ = Task.Run(async () =>
              {
                  string dlPath = (v.Type == "Preview" ? "Minecraft-Preview-" : "Minecraft-") + v.Name + ".Appx";
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
                          _ = MessageBox.Show("Failed to authenticate because: " + e.Message, "Authentication failed");
                          v.StatusInfo = null;
                          return;
                      }
                      catch (Exception e)
                      {
                          Debug.WriteLine("Authentication failed:\n" + e.ToString());
                          _ = MessageBox.Show(e.ToString(), "Authentication failed");
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
                              {
                                  v.StatusInfo.TotalBytes = total.Value;
                              }
                          }

                          v.StatusInfo.DownloadedBytes = current;
                      }, cancelSource.Token);
                      Debug.WriteLine("Download complete");
                  }
                  catch (BadUpdateIdentityException)
                  {
                      Debug.WriteLine("Download failed due to failure to fetch download URL");
                      _ = MessageBox.Show(
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
                      {
                          _ = MessageBox.Show("Download failed:\n" + e.ToString());
                      }

                      v.StatusInfo = null;
                      return;
                  }

                  try
                  {
                      v.StatusInfo.State = State.Extracting;
                      string dirPath = v.GameDirectory;

                      if (Directory.Exists(dirPath))
                      {
                          Directory.Delete(dirPath, true);
                      }

                      ZipFile.ExtractToDirectory(dlPath, dirPath);
                      v.StatusInfo = null;
                      File.Delete(Path.Combine(dirPath, "AppxSignature.p7x"));
                      _installedVersions.Add(v);
                  }
                  catch (Exception e)
                  {
                      Debug.WriteLine("Extraction failed:\n" + e.ToString());
                      _ = MessageBox.Show("Extraction failed:\n" + e.ToString());
                      v.StatusInfo = null;
                      return;
                  }
                  v.StatusInfo = null;
                  v.UpdateInstallStatus();
              });
        }

        private void InvokeUninstall(Version v) => Task.Run(async () => await Remove(v));

        private async Task Remove(Version v)
        {
            v.StatusInfo = new Status(State.Uninstalling);
            await UnregisterPackage(v.GamePackageFamily, Path.GetFullPath(v.GameDirectory));
            Directory.Delete(v.GameDirectory, true);
            v.StatusInfo = null;
            v.UpdateInstallStatus();
            Debug.WriteLine("Removed release version " + v.Name);

        }

        private async Task UnregisterPackage(string packageFamily, string gameDir)
        {
            foreach (Package pkg in new PackageManager().FindPackages(packageFamily))
            {
                string location = GetPackagePath(pkg);
                if (location == "" || location == gameDir)
                {
                    await RemovePackage(pkg, packageFamily);
                }
            }
        }

        private void InvokeLaunch(Version v)
        {
            if (_hasLaunched)
            {
                return;
            }

            _hasLaunched = true;
            _ = Task.Run(async () =>
              {
                  v.StatusInfo = new Status(State.Registering);
                  string gameDir = Path.GetFullPath(v.GameDirectory);
                  try
                  {
                      await ReRegisterPackage(v.GamePackageFamily, gameDir);
                  }
                  catch (Exception e)
                  {
                      Debug.WriteLine("App re-register failed:\n" + e.ToString());
                      _ = MessageBox.Show("App re-register failed:\n" + e.ToString());
                      _hasLaunched = false;
                      v.StatusInfo = null;
                      return;
                  }
                  v.StatusInfo = new Status(State.Launching);
                  try
                  {
                      System.Collections.Generic.IList<AppDiagnosticInfo> pkg = await AppDiagnosticInfo.RequestInfoForPackageAsync(v.GamePackageFamily);

                      if (pkg.Count > 0)
                      {
                          _ = await pkg[0].LaunchAsync();
                      }

                      Debug.WriteLine("App launch finished!");
                      _hasLaunched = false;
                      v.StatusInfo = null;
                  }
                  catch (Exception e)
                  {
                      Debug.WriteLine("App launch failed:\n" + e.ToString());
                      _ = MessageBox.Show("App launch failed:\n" + e.ToString());
                      _hasLaunched = false;
                      v.StatusInfo = null;
                      return;
                  }
              });

        }

        private async Task ReRegisterPackage(string packageFamily, string gameDir)
        {
            foreach (var pkg in new PackageManager().FindPackages(packageFamily))
            {
                string location = GetPackagePath(pkg);
                if (location == gameDir)
                {
                    Debug.WriteLine("Skipping package removal - same path: " + pkg.Id.FullName + " " + location);
                    return;
                }
                await RemovePackage(pkg, packageFamily);
            }

            Debug.WriteLine("Registering package");
            string manifestPath = Path.Combine(gameDir, "AppxManifest.xml");
            await DeploymentProgressWrapper(new PackageManager().RegisterPackageAsync(new Uri(manifestPath), null, DeploymentOptions.DevelopmentMode));
            Debug.WriteLine("App re-register done!");
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

        private async Task RemovePackage(Package pkg, string packageFamily)
        {
            Debug.WriteLine("Removing package: " + pkg.Id.FullName);
            if (!pkg.IsDevelopmentMode)
            {
                BackupMinecraftDataForRemoval(packageFamily);
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, 0));
            }
            else
            {
                Debug.WriteLine("Package is in development mode");
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, RemovalOptions.PreserveApplicationData));
            }
            Debug.WriteLine("Removal of package done: " + pkg.Id.FullName);
        }

        private void BackupMinecraftDataForRemoval(string packageFamily)
        {
            Windows.Storage.ApplicationData data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            string tmpDir = GetBackupMinecraftDataDir();

            if (Directory.Exists(tmpDir))
            {
                Debug.WriteLine("BackupMinecraftDataForRemoval error: " + tmpDir + " already exists");
                _ = Process.Start("explorer.exe", tmpDir);
                _ = MessageBox.Show("The temporary directory for backing up MC data already exists. This probably means that we failed last time backing up the data. Please back the directory up manually.");
                throw new Exception("Temporary dir exists");
            }

            Debug.WriteLine("Moving Minecraft data to: " + tmpDir);
            Directory.Move(data.LocalFolder.Path, tmpDir);
        }

        private string GetBackupMinecraftDataDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string tmpDir = Path.Combine(localAppData, "TmpMinecraftLocalState");
            return tmpDir;
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

        private void RestoreMinecraftDataFromReinstall(string packageFamily)
        {
            string tmpDir = GetBackupMinecraftDataDir();

            if (!Directory.Exists(tmpDir))
            {
                return;
            }

            Windows.Storage.ApplicationData data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            Debug.WriteLine("Moving backup Minecraft data to: " + data.LocalFolder.Path);
            RestoreMove(tmpDir, data.LocalFolder.Path);
            Directory.Delete(tmpDir, true);
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
    }

    struct MinecraftPackageFamilies
    {
        public static readonly string Minecraft = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
        public static readonly string MinecraftPreview = "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe";
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
                this.GameDirectory = (type == "Preview" ? "Minecraft-Preview-" : "Minecraft-") + Name;
            }

            public string Name { get; set; }
            public string Type { get; set; }
            public string UUID { get; set; }
            public string GameDirectory { get; set; }
            public string GamePackageFamily => Type == "Preview" ? MinecraftPackageFamilies.MinecraftPreview : MinecraftPackageFamilies.Minecraft;
            public bool IsInstalled => Directory.Exists(GameDirectory);
            public ICommand LaunchCommand { get; set; }
            public ICommand InstallCommand { get; set; }
            public ICommand UninstallCommand { get; set; }
            private Status _statusInfo;
            public Status StatusInfo
            {
                get => _statusInfo;
                set { _statusInfo = value; OnPropertyChanged("StatusInfo"); OnPropertyChanged("IsStatusChanging"); }
            }
            public bool IsStatusChanging => StatusInfo != null;

            public void UpdateInstallStatus() => OnPropertyChanged("IsInstalled");
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
            public Status(State state)
            {
                _state = state;
            }
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
                get { return _downloadedBytes; }
                set { _downloadedBytes = value; OnPropertyChanged("DownloadedBytes"); OnPropertyChanged("DisplayStatus"); }
            }
            public long TotalBytes
            {
                get { return _totalBytes; }
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