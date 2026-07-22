using System.Collections.Generic;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class PackManager : SingletonMonoBehaviour<PackManager>
    {
        private const string TAG = "[PackManager]";
        private Dictionary<Vector2Int, Pack> _packs = new();

        public void AddOrUpdatePack(ushort x, ushort y, PackType packType, byte variant, byte linkedClan)
        {
            if (MapManager.Instance == null)
            {
                Debug.LogWarning($"{TAG} MapManager not ready, skipping pack at ({x},{y})");
                return;
            }

            var pos = new Vector2Int(x, y);
            if (_packs.TryGetValue(pos, out var pack))
            {
                pack.Initialize(packType, variant, linkedClan);
                return;
            }

            var go = new GameObject($"Pack_{x}_{y}");
            go.transform.SetParent(transform);
            go.transform.position = CoordinateUtils.ServerToUnityPos(x, y);
            pack = go.AddComponent<Pack>();
            pack.Initialize(packType, variant, linkedClan);
            _packs[pos] = pack;
        }

        public void RemovePack(ushort x, ushort y)
        {
            var pos = new Vector2Int(x, y);
            if (_packs.TryGetValue(pos, out var pack))
            {
                Destroy(pack.gameObject);
                _packs.Remove(pos);
            }
            else
            {
                Debug.LogWarning($"{TAG} RemovePack: no pack at ({x},{y})");
            }
        }

        public void ClearAllPacks()
        {
            int count = _packs.Count;
            foreach (var pack in _packs.Values)
            {
                if (pack != null)
                {
                    Destroy(pack.gameObject);
                }
            }

            _packs.Clear();
            Debug.Log($"{TAG} Cleared {count} packs");
        }
    }
}
