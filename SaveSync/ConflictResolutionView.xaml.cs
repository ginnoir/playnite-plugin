using Playnite.SDK;
using System;
using System.Windows.Controls;

namespace RomM.SaveSync
{
    /// <summary>Modal save-conflict chooser. Set <see cref="Window"/> before showing; read <see cref="Choice"/> after.</summary>
    public partial class ConflictResolutionView : UserControl
    {
        public Window Window { get; set; }
        internal ConflictChoice Choice { get; private set; } = ConflictChoice.Skip;

        internal ConflictResolutionView(ConflictInfo info)
        {
            InitializeComponent();

            HeaderText.Text = $"\"{info.GameName}\" changed both locally and on RomM since the last sync. Choose which save to keep.";
            LocalSizeText.Text = $"Size: {FormatSize(info.LocalSizeBytes)}";
            LocalTimeText.Text = $"Modified: {FormatTime(info.LocalUpdatedUtc)}";
            RemoteSizeText.Text = info.RemoteSizeBytes.HasValue ? $"Size: {FormatSize(info.RemoteSizeBytes.Value)}" : "Size: unknown";
            RemoteTimeText.Text = info.RemoteUpdatedUtc.HasValue ? $"Modified: {FormatTime(info.RemoteUpdatedUtc.Value)}" : "Modified: unknown";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }

        private static string FormatTime(DateTime utc)
        {
            // Stored/compared in UTC; show the user their local time.
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void Close(ConflictChoice choice)
        {
            Choice = choice;
            Window?.Close();
        }

        private void KeepLocal_Click(object sender, System.Windows.RoutedEventArgs e) => Close(ConflictChoice.KeepLocal);
        private void KeepRemote_Click(object sender, System.Windows.RoutedEventArgs e) => Close(ConflictChoice.KeepRemote);
        private void KeepBoth_Click(object sender, System.Windows.RoutedEventArgs e) => Close(ConflictChoice.KeepBoth);
        private void Skip_Click(object sender, System.Windows.RoutedEventArgs e) => Close(ConflictChoice.Skip);
    }
}
