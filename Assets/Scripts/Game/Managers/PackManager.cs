using System.Collections.Generic;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Utils;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class PackManager : SingletonMonoBehaviour<PackManager>
    {
        private Dictionary<Vector2Int, Pack> _packs = new();

        public void AddOrUpdatePack(ushort x, ushort y, PackType packType, byte variant, byte linkedClan)
        {
            if (MapManager.Instance == null)
            {
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
            float unityY = (MapManager.Instance.WorldHeight - 1 - y) + 0.5f;
            go.transform.position = new Vector3(x + 0.5f, unityY, 0);
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
        }

        public void ClearAllPacks()
        {
            foreach (var pack in _packs.Values)
            {
                if (pack != null)
                {
                    Destroy(pack.gameObject);
                }
            }

            _packs.Clear();
        }
    }
}
