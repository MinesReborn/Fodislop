using System;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Player.Logic;
using Fodinae.Scripts.UI.HUD.Player.Model;
using Fodinae.Scripts.UI.HUD.Player.View;
using UnityEngine;
using UnityEngine.UIElements;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using Fodinae.Scripts.Networking;

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
            _model = ServiceLocator.Resolve<IPlayerStats>() ?? PlayerStatsModel.Instance;
        }
    }
}
