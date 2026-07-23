using System;
using System.Collections.Generic;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using Fodinae.Scripts.UI.HUD.Inventory.View;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.HUD.Inventory.Presenter
{
    [RequireComponent(typeof(InventoryView))]
    public class InventoryPresenter : MonoBehaviour
    {
        private InventoryView _view;
        private IInventoryModel _model;

        private void Start()
        {
            _view = GetComponent<InventoryView>();
            _model = Core.ServiceLocator.Resolve<IInventoryModel>() ?? InventoryModel.Instance;

            // Further MVP decoupling of specific methods should happen progressively
        }
    }
}
