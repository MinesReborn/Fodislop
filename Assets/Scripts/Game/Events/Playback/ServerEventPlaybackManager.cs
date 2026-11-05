#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

using Kern.Game.Audio.Events.Playback;
using Kern.Game.Audio.Music.Playback;
using Kern.Game.Visual.Effects.Events.Playback;
using Kern.Game.Player.Cameras;
using Kern.Game.Rendering;
using Kern.Game.Visual.Effects.Cells;
using Kern.Game.Visual.Effects.Crystals;
using Kern.Game.Visual.Effects.Pooling;
using Kern.Game.Visual.Effects.Sprites;
using Kern.Game.Visual.Effects.Weapons;

namespace Kern.Game.Events.Playback
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Game.Managers", sourceAssembly: null, sourceClassName: "ServerAudioEventManager")]
    [DefaultExecutionOrder(100)]
    public class ServerEventPlaybackManager : MonoBehaviour, IServerAudioService, IServerVisualEffectService
    {
        private const string TAG = "[ServerEventPlaybackManager]";
        private ServerMusicPlayback? _music;
        private ServerMusicPlayback Music => _music ??= new ServerMusicPlayback(_audioSystem, _operations, _presentationState);
        // Select the crystal collection visual here. Set to CurrentAssets to
        // use the existing standalone crystal sprites instead.
        private const CrystalCollectionVisualVariant CrystalCollectionVariant =
            CrystalCollectionVisualVariant.SourceMineralAtlas;
        private const int SourceCrystalSortingOrder = 5; // SpriteRenderer.m_SortingOrder in the source prefab.
        private readonly List<IServerWorldEffect> _activeEffects = new();
        private readonly Stack<GunShotWorldEffect> _availableGunShots = new();
        private const int MaximumRetainedGunShots = 8;
        private readonly Dictionary<(global::MinesServer.Data.AudioEventType Event, ushort Bot, ushort X, ushort Y), float> _lastAudioEventTimes = new();
        private readonly Dictionary<ushort, SpriteAnimationWorldEffect> _healingEffects = new();
        private SpriteAnimationWorldEffect? _heldHealEffect;
        private readonly Queue<VisualPacket> _pendingVisualPackets = new();
        [Inject]
        private IWorldLabels _worldLabels = null!;
        [Inject]
        private Kern.Game.Player.Cameras.CameraFollow _cameraFollow = null!;

        [Inject]
        private IVisualEffectService _visualEffectService = null!;

        [Inject]
        private IRobotService _robotService = null!;

        [Inject]
        private IAudioSystem _audioSystem = null!;
        [Inject]
        private IWorldPresentationState _presentationState = null!;

        [Inject]
        private CellDestructionClip _cellDestructionClip = null!;
        [Inject]
        private SpriteAnimationLibrary _spriteAnimations = null!;
        [Inject]
        private Kern.World.Lighting.LightingEngine _lighting = null!;

        [Inject]
        private IAssetLoader _assetLoader = null!;

        [Inject]
        private MapManager _mapManager = null!;

        [Inject]
        private VisualEffectPool _visualEffectPool = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        [Inject]
        private void SubscribeToPresentationState(IWorldPresentationState presentationState)
        {
            if (_presentationState != null)
            {
                _presentationState.Changed -= Music.HandlePresentationChanged;
            }

            _presentationState = presentationState;
            _presentationState.Changed -= Music.HandlePresentationChanged;
            _presentationState.Changed += Music.HandlePresentationChanged;
        }

        public void Play(AudioPacket packet)
        {
            if (packet.EventType == global::MinesServer.Data.AudioEventType.Music)
            {
                Music.Play();
                return;
            }

            if (!_presentationState.IsStable)
            {
                return;
            }

            var audioEventKey = (packet.EventType, packet.TargetBotId, packet.X, packet.Y);
            bool isCellDestruction = CellDestructionEffect.IsCellDestruction(packet.EventType);
            if (!isCellDestruction && _lastAudioEventTimes.TryGetValue(audioEventKey, out float lastTime) && Time.time - lastTime < 0.04f)
            {
                return;
            }

            // В фоне (MuteInBackground) мастер-шина FMOD на паузе: голос,
            // запущенный на запаузенной шине, не завершается и копится, а при
            // возврате фокуса все накопленные голоса снимаются с паузы разом
            // и наслаиваются. Звук, случившийся в фоне, всё равно не был
            // слышен — отбрасываем его сразу, а не отыгрываем потом.
            if (!Application.isFocused && _audioSystem.IsBackgroundMuted)
            {
                return;
            }

            _lastAudioEventTimes[audioEventKey] = Time.time;
            if (_lastAudioEventTimes.Count > 128)
            {
                _lastAudioEventTimes.Clear();
            }

            // Destroy and Geology correspond to legacy CellDestructionD/G.
            // Their protocol has no separate VisualEffect identifier: play exactly one
            // shared animation at the packet cell, ignoring robot facing.
            if (isCellDestruction)
            {
                _activeEffects.Add(new CellDestructionEffect(packet.EventType,
                    WorldCoordinateConverter.ServerToUnityPos(packet.X, packet.Y, _mapManager.WorldHeight),
                    _cellDestructionClip, _sceneObjects, _lighting));
            }

            var effect = new ServerAudioEvent(
                packet,
                slot: null,
                _robotService,
                _localPlayer,
                _audioSystem,
                _assetLoader,
                _mapManager,
                _visualEffectPool,
                _operations);
            _activeEffects.Add(effect);
        }

        public void Play(global::MinesServer.Data.AudioEventType audioEventType, ushort x, ushort y, ushort targetBotId = 0, int param = 0) =>
            Play(new AudioPacket(audioEventType, targetBotId, x, y, Array.Empty<MinesServer.Networking.Contracts.Packets.StringPairPacket>()));

        public void PlayEffect(global::MinesServer.Data.VisualEffect effectType, ushort x, ushort y, ushort targetBotId = 0, int param = 0) =>
            PlayEffect(new VisualPacket(effectType, targetBotId, x, y, Array.Empty<MinesServer.Networking.Contracts.Packets.StringPairPacket>()));

        // Packet handlers run inside NetworkService's synchronous drain. Effect
        // construction can initialize fonts, shaders, textures and UI, so the
        // handler only records the intent; Update builds it on the main loop.
        public void PlayEffect(VisualPacket packet)
        {
            if (!_presentationState.IsStable)
            {
                return;
            }

            _pendingVisualPackets.Enqueue(packet);
        }

        private void CreateVisualEffect(VisualPacket packet)
        {
            if (packet.EffectType == global::MinesServer.Data.VisualEffect.Crystal)
            {
                // Preserve the existing crystal flight/digits; the source flash
                // is a separate animation at the mined cell.
                _activeEffects.Add(new SpriteAnimationWorldEffect("CrystallSparkle",
                    WorldCoordinateConverter.ServerToUnityPos(packet.X, packet.Y, _mapManager.WorldHeight),
                    _spriteAnimations, _sceneObjects));
            }

            if (TryPlaySpriteAnimationEffect(packet))
            {
                return;
            }

            IVisualEffectSlot? slot = packet.EffectType == global::MinesServer.Data.VisualEffect.Crystal &&
                CrystalCollectionVariant == CrystalCollectionVisualVariant.SourceMineralAtlas
                ? _visualEffectService.Acquire(SourceCrystalSortingOrder)
                : _visualEffectService.Acquire();

            var effect = new ServerVisualEvent(
                packet,
                slot,
                _robotService,
                _assetLoader,
                _mapManager,
                _visualEffectPool,
                _operations,
                _entityBatchRenderer,
                _spriteAnimations,
                CrystalCollectionVariant,
                _sceneObjects,
                _worldLabels,
                lighting: _lighting);
            _activeEffects.Add(effect);
        }

        public void PlayBoundaryDig(int serverX, int serverY, global::MinesServer.Data.Direction direction)
        {
            float angle = direction switch
            {
                global::MinesServer.Data.Direction.Up => 0f,
                global::MinesServer.Data.Direction.Right => -90f,
                global::MinesServer.Data.Direction.Down => 180f,
                global::MinesServer.Data.Direction.Left => 90f,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
            };
            var packet = new VisualPacket(global::MinesServer.Data.VisualEffect.Bz, 0, 0, 0,
                Array.Empty<MinesServer.Networking.Contracts.Packets.StringPairPacket>());
            _activeEffects.Add(new ServerVisualEvent(
                packet, _visualEffectService.Acquire(), _robotService, _assetLoader, _mapManager,
                _visualEffectPool, _operations, _entityBatchRenderer, _spriteAnimations,
                CrystalCollectionVariant, _sceneObjects, _worldLabels,
                new Vector2Int(serverX, serverY), angle, _lighting));
        }

        public void SetLocalHealHeld(bool held)
        {
            if (!held)
            {
                _heldHealEffect?.Dispose();
                _heldHealEffect = null;
                return;
            }

            if (_heldHealEffect != null && !_heldHealEffect.IsDisposed)
            {
                return;
            }

            var player = _localPlayer.Current;
            if (player == null)
            {
                return;
            }

            ushort botId = checked((ushort)player.BotId);
            if (!_healingEffects.TryGetValue(botId, out SpriteAnimationWorldEffect healing) || healing.IsDisposed)
            {
                healing = new SpriteAnimationWorldEffect("Heal", player.transform.position,
                    _spriteAnimations, _sceneObjects, player.transform, lighting: _lighting);
                _healingEffects[botId] = healing;
                _activeEffects.Add(healing);
            }

            healing.Hold();
            _heldHealEffect = healing;
        }

        private bool TryPlaySpriteAnimationEffect(VisualPacket packet)
        {
            // Effects.Create(Hurt): only the local target starts TpCamera shake.
            if (packet.EffectType == global::MinesServer.Data.VisualEffect.Hurt &&
                _localPlayer.Current is { } player && player.BotId == packet.TargetBotId)
            {
                _cameraFollow.TriggerHurtShake();
            }

            if (packet.EffectType == global::MinesServer.Data.VisualEffect.GunShot)
            {
                if (_robotService.TryGetRobot(packet.TargetBotId, out IRobotView? target) && target != null)
                {
                    Vector3 origin = WorldCoordinateConverter.ServerToUnityPos(packet.X, packet.Y, _mapManager.WorldHeight);
                    uint seed = unchecked((uint)Time.frameCount ^ packet.TargetBotId);
                    GunShotWorldEffect effect;
                    if (_availableGunShots.Count > 0)
                    {
                        effect = _availableGunShots.Pop();
                        effect.Activate(origin, target.transform.position, seed);
                    }
                    else
                    {
                        effect = new GunShotWorldEffect(origin, target.transform.position,
                            _spriteAnimations, _sceneObjects, seed);
                    }
                    _activeEffects.Add(effect);
                }

                return true;
            }

            if (packet.EffectType is global::MinesServer.Data.VisualEffect.Pull or global::MinesServer.Data.VisualEffect.Push)
            {
                if (!_robotService.TryGetRobot(packet.TargetBotId, out IRobotView? robot) || robot == null)
                {
                    return true;
                }

                Vector3 cellPosition = WorldCoordinateConverter.ServerToUnityPos(packet.X, packet.Y, _mapManager.WorldHeight);
                bool isPull = packet.EffectType == global::MinesServer.Data.VisualEffect.Pull;
                Vector3 start = isPull ? robot.transform.position : cellPosition;
                Vector3 end = isPull ? cellPosition : robot.transform.position;
                _activeEffects.Add(new SpriteAnimationWorldEffect("Arrow", start, _spriteAnimations, _sceneObjects,
                    destinationPosition: end));
                return true;
            }

            if (packet.EffectType == global::MinesServer.Data.VisualEffect.BombTick)
            {
                Vector3 bombPosition = WorldCoordinateConverter.ServerToUnityPos(packet.X, packet.Y, _mapManager.WorldHeight);
                _activeEffects.Add(new SpriteAnimationWorldEffect(BombClip(ReadBombType(packet)), bombPosition,
                    _spriteAnimations, _sceneObjects));
                return true;
            }

            // Only existing visual events are consumed. Audio packets never
            // manufacture Hurt/Heal/etc., and Bz keeps its already ported spark.
            string? id = packet.EffectType switch
            {
                global::MinesServer.Data.VisualEffect.Hurt => "Hurt",
                global::MinesServer.Data.VisualEffect.Heal => "Heal",
                global::MinesServer.Data.VisualEffect.Death => "Explosion",
                global::MinesServer.Data.VisualEffect.Boom => "Boom",
                global::MinesServer.Data.VisualEffect.NoHp => "RobotSmoke",
                global::MinesServer.Data.VisualEffect.NoHpSmall => "RobotSmoke",
                global::MinesServer.Data.VisualEffect.Smoke => "Smoke0",
                global::MinesServer.Data.VisualEffect.Volcano => "Smoke0",
                global::MinesServer.Data.VisualEffect.TpOut => "Tpo",
                // Raw ResourcesManager.tpi PPtr resolves to Respio (path id 1207).
                global::MinesServer.Data.VisualEffect.TpIn => "Respio",
                _ => null,
            };
            if (id == null) { return false; }
            Vector3 position = WorldCoordinateConverter.ServerToUnityPos(packet.X, packet.Y, _mapManager.WorldHeight);
            Transform? follow = null;
            if (packet.TargetBotId != 0 && (id is "Hurt" or "Heal" or "RobotSmoke"))
            {
                if (!_robotService.TryGetRobot(packet.TargetBotId, out IRobotView? robot) || robot == null)
                {
                    return true; // No robot: do not create a phantom entity for an effect.
                }

                position = robot.transform.position;
                if (id is "Hurt" or "Heal") { follow = robot.transform; }
            }

            if (id == "Heal" && _healingEffects.TryGetValue(packet.TargetBotId, out SpriteAnimationWorldEffect healing) && !healing.IsDisposed)
            {
                healing.Refresh();
                return true;
            }

            int count = packet.EffectType == global::MinesServer.Data.VisualEffect.Volcano ? 6 : 1;
            for (int index = 0; index < count; index++)
            {
                string clip = packet.EffectType == global::MinesServer.Data.VisualEffect.Volcano
                    ? "Smoke" + UnityEngine.Random.Range(0, 3)
                    : id;
                int boomType = packet.EffectType == global::MinesServer.Data.VisualEffect.Boom ? ReadBombType(packet) : 0;
                var effect = new SpriteAnimationWorldEffect(clip, position, _spriteAnimations, _sceneObjects, follow, boomType,
                    lighting: _lighting);
                _activeEffects.Add(effect);
                if (id == "Heal") { _healingEffects[packet.TargetBotId] = effect; }
            }

            return true;
        }

        private static string BombClip(int bombType) => bombType switch
        {
            0 => "BombPlasma",
            1 => "BombProton",
            2 => "BombDischarge",
            _ => throw new ArgumentOutOfRangeException(nameof(bombType), bombType, "Unknown bomb type."),
        };

        private static int ReadBombType(VisualPacket packet)
        {
            foreach (var parameter in packet.Parameters)
            {
                if (!parameter.Key.Equals("bombType", StringComparison.OrdinalIgnoreCase) &&
                    !parameter.Key.Equals("type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = parameter.Value.Trim();
                if (int.TryParse(value, out int numericType))
                {
                    return numericType;
                }

                return value.ToLowerInvariant() switch
                {
                    "plasma" => 0,
                    "proton" => 1,
                    "discharge" => 2,
                    _ => throw new FormatException($"Unknown bomb type '{parameter.Value}'."),
                };
            }

            // BoomScript's default enum value is Plasma (0).
            return 0;
        }

        public void ClearActiveEvents()
        {
            _music?.StopMusic();
            int count = _activeEffects.Count;
            foreach (var effect in _activeEffects)
            {
                effect.Dispose();
                RecycleGunShot(effect);
            }

            _activeEffects.Clear();
            _healingEffects.Clear();
            _heldHealEffect = null;
            if (_cameraFollow != null)
            {
                _cameraFollow.ClearHurtShake();
            }
            if (count > 0)
            {
                Debug.Log($"{TAG} Cleared {count} active effects");
            }
        }

        protected void OnDestroy()
        {
            if (_presentationState != null)
            {
                _presentationState.Changed -= Music.HandlePresentationChanged;
            }

            ClearActiveEvents();
            while (_availableGunShots.Count > 0)
            {
                _availableGunShots.Pop().Destroy();
            }
            _audioSystem?.StopBus(AudioBusType.Music, 0.2f);
        }

        private void DrainPendingVisualEffects()
        {
            if (_pendingVisualPackets.Count == 0)
            {
                return;
            }

            if (!_presentationState.IsStable)
            {
                _pendingVisualPackets.Clear();
                return;
            }

            while (_pendingVisualPackets.Count > 0)
            {
                VisualPacket packet = _pendingVisualPackets.Dequeue();
                try
                {
                    CreateVisualEffect(packet);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"{TAG} Failed to create visual effect {packet.EffectType}: {exception}");
                }
            }
        }

        protected void Update()
        {
            DrainPendingVisualEffects();
            if (!_presentationState.IsStable || _activeEffects.Count == 0)
            {
                return;
            }

            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                effect.Update();
                if (effect.IsDisposed)
                {
                    _activeEffects.RemoveAt(i);
                    RecycleGunShot(effect);
                }
            }
        }

        private void RecycleGunShot(IServerWorldEffect effect)
        {
            if (effect is not GunShotWorldEffect gunShot)
            {
                return;
            }
            if (_availableGunShots.Count < MaximumRetainedGunShots)
            {
                _availableGunShots.Push(gunShot);
            }
            else
            {
                gunShot.Destroy();
            }
        }
    }
}
