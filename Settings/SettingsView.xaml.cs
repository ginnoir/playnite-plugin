using Playnite.SDK;
using RomM.SaveSync;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace RomM.Settings
{
    public partial class SettingsView : UserControl
    {
        private bool InManualCellCommit = false;

        public SettingsView()
        {
            InitializeComponent();
        }

        private void Click_Delete(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is EmulatorMapping mapping)
            {
                var res = SettingsViewModel.Instance.PlayniteAPI.Dialogs.ShowMessage(string.Format("Delete this mapping?\r\n\r\n{0}", mapping.GetDescriptionLines().Aggregate((a, b) => $"{a}{Environment.NewLine}{b}")), "Confirm delete", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    SettingsViewModel.Instance.Mappings.Remove(mapping);
                }
            }
        }

        private void Click_BrowseDestination(object sender, RoutedEventArgs e)
        {
            var mapping = ((FrameworkElement)sender).DataContext as EmulatorMapping;
            string path;
            if ((path = GetSelectedFolderPath()) == null) return;
            var playnite = SettingsViewModel.Instance.PlayniteAPI;
            if (playnite.Paths.IsPortable)
            {
                path = path.Replace(playnite.Paths.ApplicationPath, Playnite.SDK.ExpandableVariables.PlayniteDirectory);
            }

            mapping.DestinationPath = path;
        }

        private async void Click_TestConnection(object sender, RoutedEventArgs e)
        {
            var settings = SettingsViewModel.Instance;
            var dialogs = settings.PlayniteAPI.Dialogs;

            var host = settings.RomMHost?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(host))
            {
                dialogs.ShowMessage("RomM Host is empty.", "RomM");
                return;
            }

            if (!settings.HasAnyAuth)
            {
                dialogs.ShowMessage("Provide either a valid API Token or username and password.", "RomM");
                return;
            }

            var button = (Button)sender;
            var originalContent = button.Content;

            try
            {
                button.IsEnabled = false;
                button.Content = "Testing...";

                using (var req = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/users/me"))
                {
                    req.Headers.Authorization = HttpClientSingleton.BuildAuthHeader(settings);
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    using (var resp = await HttpClientSingleton.Instance.SendAsync(req, cts.Token))
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            dialogs.ShowMessage($"Connection successful ({(int)resp.StatusCode}).", "RomM");
                        }
                        else if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                        {
                            dialogs.ShowMessage($"Authentication rejected (HTTP {(int)resp.StatusCode}). Check your API token or username/password.", "RomM");
                        }
                        else
                        {
                            dialogs.ShowMessage($"Connection failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", "RomM");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                dialogs.ShowMessage("Connection timed out after 10 seconds.", "RomM");
            }
            catch (HttpRequestException ex)
            {
                dialogs.ShowMessage($"Connection failed: {ex.Message}", "RomM");
            }
            catch (Exception ex)
            {
                dialogs.ShowMessage($"Unexpected error: {ex.Message}", "RomM");
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = originalContent;
            }
        }

        private async void Click_TestSyncConnection(object sender, RoutedEventArgs e)
        {
            var settings = SettingsViewModel.Instance;
            var dialogs = settings.PlayniteAPI.Dialogs;
            var host = settings.RomMHost?.Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(host) || !settings.HasAnyAuth)
            {
                dialogs.ShowMessage("Set the RomM host and a valid API token (or username/password) first.", "RomM Save Sync");
                return;
            }

            var button = (Button)sender;
            var original = button.Content;
            button.IsEnabled = false;
            button.Content = "Working...";

            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    var logger = LogManager.GetLogger();
                    var client = new SaveSyncClient(host, logger);

                    var readable = client.CheckDevicesReadable();
                    if (!readable.Ok)
                    {
                        var hint = readable.Forbidden || readable.Unauthorized
                            ? " The token is likely missing the 'devices.read'/'devices.write' scopes."
                            : "";
                        dialogs.ShowMessage($"Could not reach the device API (HTTP {(int)readable.Status}).{hint}", "RomM Save Sync");
                        return;
                    }

                    var deviceId = DeviceIdentity.ReRegister(client, settings, logger);
                    if (string.IsNullOrEmpty(deviceId))
                    {
                        dialogs.ShowMessage("Device registration failed. Ensure the token has the 'devices.write' scope.", "RomM Save Sync");
                    }
                    else
                    {
                        dialogs.ShowMessage($"Connected. Registered as device '{settings.DeviceName}'.", "RomM Save Sync");
                    }
                });
            }
            catch (Exception ex)
            {
                dialogs.ShowMessage($"Save-sync test failed: {ex.Message}", "RomM Save Sync");
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = original;
            }
        }

        private void Click_Browse7zDestination(object sender, RoutedEventArgs e)
        {
            string path;
            if ((path = SettingsViewModel.Instance.PlayniteAPI.Dialogs.SelectFile("7Zip Executable|7z.exe")) == null) return;

            SettingsViewModel.Instance.PathTo7z = path;
        }

        private static string GetSelectedFolderPath()
        {
            return SettingsViewModel.Instance.PlayniteAPI.Dialogs.SelectFolder();
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (!InManualCellCommit && sender is DataGrid grid)
            {
                InManualCellCommit = true;

                // HACK!!!!
                // Alternate approach 1: try to find new value here and store that somewhere as the currently selected emu
                // Alternate approach 2: the "right" way(?) https://stackoverflow.com/a/34332709
                if (e.Column.Header?.ToString() == "Emulator" || e.Column.Header?.ToString() == "Profile")
                {
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                }

                InManualCellCommit = false;
            }
        }

        private void DataGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                if (e.Uri.Scheme == Uri.UriSchemeHttp || e.Uri.Scheme == Uri.UriSchemeHttps)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
            }
            e.Handled = true;
        }
    }
}
