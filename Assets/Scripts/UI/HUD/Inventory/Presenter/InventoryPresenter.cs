using Fodinae.Scripts.UI.HUD.Inventory.Interfaces;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using Fodinae.Scripts.UI.HUD.Inventory.View;
using UnityEngine;

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
            _model = InventoryModel.Instance;
        }
    }
}
