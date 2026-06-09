using Playnite.SDK;
using System;
using System.Windows;

namespace RomM.SaveSync
{
    /// <summary>
    /// Interactive conflict resolver (ConflictPolicy.Ask). Sync runs on a background thread, so this
    /// marshals to the UI dispatcher to show a modal chooser and returns the user's decision. On any
    /// failure it falls back to KeepBoth — never silent data loss.
    /// </summary>
    internal sealed class DialogConflictResolver : IConflictResolver
    {
        private readonly IPlayniteAPI _playnite;

        public DialogConflictResolver(IPlayniteAPI playnite)
        {
            _playnite = playnite;
        }

        public ConflictChoice Resolve(ConflictInfo info)
        {
            try
            {
                var choice = ConflictChoice.KeepBoth;
                _playnite.MainView.UIDispatcher.Invoke(() =>
                {
                    var control = new ConflictResolutionView(info);
                    var window = _playnite.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMinimizeButton = false,
                        ShowMaximizeButton = false,
                        ShowCloseButton = true,
                    });

                    window.Title = "RomM — Save Conflict";
                    window.SizeToContent = SizeToContent.WidthAndHeight;
                    window.ResizeMode = ResizeMode.NoResize;
                    window.ShowInTaskbar = false;
                    window.Owner = _playnite.Dialogs.GetCurrentAppWindow();
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    window.Content = control;
                    control.Window = window;

                    window.ShowDialog();
                    choice = control.Choice;
                });
                return choice;
            }
            catch (Exception)
            {
                return ConflictChoice.KeepBoth;
            }
        }
    }
}
