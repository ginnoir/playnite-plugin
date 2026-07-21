using Playnite.SDK.Controls;
using Playnite.SDK.Models;

namespace RomM.SaveSync
{
    public partial class GameSyncStatusControl : PluginUserControl
    {
        private readonly SaveStatusViewModel _viewModel;

        internal GameSyncStatusControl(IRomM romM)
        {
            _viewModel = new SaveStatusViewModel(romM);
            DataContext = _viewModel;
            InitializeComponent();
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            _viewModel.Load(newContext);
        }

        internal void RefreshGameList() => _viewModel.RefreshAvailableGames();
    }
}
