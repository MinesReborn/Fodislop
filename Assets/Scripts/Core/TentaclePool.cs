using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    public static class TentaclePool
    {
        private static readonly Queue<LineRenderer> _pool = new();
        private static Transform _parent;

        private static Transform Parent
        {
            get
            {
                if (_parent == null)
                {
                    var go = new GameObject("[TentaclePool]");
                    go.SetActive(false);
                    _parent = go.transform;
                }

                return _parent;
            }
        }

        public static LineRenderer Get()
        {
            while (_pool.Count > 0)
            {
                var line = _pool.Dequeue();
                if (line != null)
                {
                    line.gameObject.SetActive(true);
                    return line;
                }
            }

            var go = new GameObject("Tentacle");
            go.transform.SetParent(Parent);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 5;
            lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        public static void Return(LineRenderer line)
        {
            if (line == null)
            {
                return;
            }

            line.gameObject.SetActive(false);
            line.transform.SetParent(Parent);
            _pool.Enqueue(line);
        }

        public static void Clear()
        {
            while (_pool.Count > 0)
            {
                var line = _pool.Dequeue();
                if (line != null)
                {
                    Object.Destroy(line.gameObject);
                }
            }
        }
    }
}
