using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Downloads;
using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomM.SaveSync
{
    /// <summary>
    /// Desktop sidebar entry that hosts <see cref="GameSyncStatusControl"/> — compare local vs
    /// RomM saves/states for the selected (or picker-chosen) game.
    /// </summary>
    internal class SaveSyncSidebarItem : SidebarItem
    {
        private readonly IRomM _romM;
        private SidebarItemControl _root;
        private GameSyncStatusControl _statusControl;

        internal SaveSyncSidebarItem(IRomM romM)
        {
            _romM = romM ?? throw new ArgumentNullException(nameof(romM));
            Type = SiderbarItemType.View;
            Title = "Save Sync Status";
            Icon = new Image
            {
                Source = new BitmapImage(new Uri(global::RomM.RomM.Icon)),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
            };
            Visible = true;
            Opened = () =>
            {
                if (_root == null)
                {
                    _statusControl = new GameSyncStatusControl(_romM);
                    _root = new SidebarItemControl();
                    _root.SetTitle("Save Sync Status");
                    _root.AddContent(_statusControl);
                }

                _statusControl.RefreshGameList();
                var current = API.Instance.MainView.SelectedGames?.FirstOrDefault();
                _statusControl.GameContextChanged(null, current);
                return _root;
            };
        }

        internal void NotifyGameSelected(Game game)
        {
            _statusControl?.GameContextChanged(null, game);
        }
    }
}
