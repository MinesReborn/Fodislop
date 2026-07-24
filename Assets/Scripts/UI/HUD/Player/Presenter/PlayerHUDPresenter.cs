using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.UI.HUD.Player.Model;
using Fodinae.Scripts.UI.HUD.Player.View;
using UnityEngine;

namespace Fodinae.Scripts.UI.HUD.Player.Presenter
{
    [RequireComponent(typeof(PlayerHUDView))]
    public class PlayerHUDPresenter : MonoBehaviour
    {
        private PlayerHUDView _view;
        private IPlayerStats _model;

        private void Start()
        {
            _view = GetComponent<PlayerHUDView>();
            _model = PlayerStatsModel.Instance;
        }
    }
}
