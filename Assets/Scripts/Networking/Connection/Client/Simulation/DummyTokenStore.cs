#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client
{
    /// <summary>
    /// Owns offline dummy-transport token persistence independently from the
    /// connection lifecycle and packet simulation.
    /// </summary>
    public sealed class DummyTokenStore
    {
        private readonly string _path = Path.Combine(
            Application.temporaryCachePath,
            "server_tokens.json");

        public HashSet<string> Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new HashSet<string>();
                }

                string json = File.ReadAllText(_path);
                List<string>? tokens = JsonConvert.DeserializeObject<List<string>>(json);
                return tokens == null ? new HashSet<string>() : new HashSet<string>(tokens);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DummyTokenStore] Failed to load tokens: {exception.Message}");
                return new HashSet<string>();
            }
        }

        public void Save(IEnumerable<string> tokens)
        {
            try
            {
                string json = JsonConvert.SerializeObject(new List<string>(tokens));
                File.WriteAllText(_path, json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DummyTokenStore] Failed to save tokens: {exception.Message}");
            }
        }
    }
}
