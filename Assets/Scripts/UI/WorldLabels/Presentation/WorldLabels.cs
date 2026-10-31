#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Kern.UI.WorldLabels;

public sealed class WorldLabels : IWorldLabels, IPostLateTickable, IDisposable
{
    private const string TAG = "[WorldLabels]";
    private const string BubbleTextClass = "world-label-chat-text";
    private static readonly Color s_frameTint = new(1f, 1f, 1f, 0.6156863f);

    private static readonly ProfilerMarker s_postLateTickMarker =
        new("Kern.WorldLabels.PostLateTick");

    private readonly List<Entry> _entries = new();
    private readonly UIDocument _document;
    private readonly IGameplayCamera _camera;
    private VisualElement? _root;
    private VisualElement? _container;
    private bool _zoomErrorLogged;

    public WorldLabels(UIDocument document, IGameplayCamera camera)
    {
        _document = document;
        _camera = camera;
    }

    public IWorldLabel Create(WorldLabelKind kind)
    {
        if (_root == null)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/WorldLabels/Templates/WorldLabels")
                ?? throw new InvalidOperationException("Missing UI/WorldLabels/Templates/WorldLabels.");
            _root = template.CloneTree();
            _root.AddToClassList("world-labels");
            _root.pickingMode = PickingMode.Ignore;
            _document.rootVisualElement.Insert(0, _root);
            _container = _root.Q("WorldLabels");
        }

        var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
        VisualElement frame = label;
        if (kind == WorldLabelKind.ChatBubble)
        {
            frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("world-label-chat");
            ApplyBubbleFrame(frame);
            label.AddToClassList(BubbleTextClass);
            frame.Add(label);
        }
        else
        {
            label.AddToClassList("world-label-name");
            if (kind == WorldLabelKind.Nickname)
            {
                label.AddToClassList("world-label-nickname");
            }
        }

        _container!.Add(frame);
        var entry = new Entry(this, frame, label, kind);
        _entries.Add(entry);
        return entry;
    }

    private static void ApplyBubbleFrame(VisualElement frame)
    {
        Sprite sprite = Resources.Load<Sprite>(ResourcePaths.LocalChatBubbleSprite)
            ?? throw new InvalidOperationException("Missing local chat bubble sprite.");
        frame.style.backgroundImage = new StyleBackground(sprite);
        frame.style.unityBackgroundImageTintColor = s_frameTint;
        Vector4 border = sprite.border;
        frame.style.unitySliceLeft = (int)border.x;
        frame.style.unitySliceBottom = (int)border.y;
        frame.style.unitySliceRight = (int)border.z;
        frame.style.unitySliceTop = (int)border.w;
        frame.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
    }

    // После всех LateUpdate, а не в LateTick: VContainer ставит LateTick перед
    // ScriptRunBehaviourLateUpdate, а камера (CameraFollow) двигается и меняет
    // зум именно там. Метка, посчитанная раньше камеры, бралась по кадру
    // прошлого кадра: при движении отставала от робота, при зуме кегль
    // запаздывал на кадр — ники «плавали» и меняли размер.
    public void PostLateTick()
    {
        using var marker = s_postLateTickMarker.Auto();
        if (!_document.enabled || _root?.panel == null)
        {
            return;
        }

        // Камера принадлежит Bootstrap и может быть уничтожена раньше этой сцены:
        // порядок разрушения сцен при выходе и в тестах не гарантирован.
        Camera? view = _camera.Camera;
        if (view == null)
        {
            return;
        }

        // Кегль и бокс метки пересчитываются по зуму камеры: USS задаёт базу в
        // пикселях панели, а панель от зума не зависит, и без пересчёта текст
        // над роботом оставался бы прежних 12 px при любом отдалении.
        if (!WorldLabelScale.TryFor(view.orthographicSize, out float scale))
        {
            // Камера без пригодного размера кадра — дефект, а не повод оставить
            // метки прежнего кегля: молчаливая подмена размера скрыла бы его
            // ровно тем же способом, каким он появился.
            if (!_zoomErrorLogged)
            {
                _zoomErrorLogged = true;
                Debug.LogError(
                    $"{TAG} Camera reports orthographicSize={view.orthographicSize}; " +
                    "world label font size cannot be matched to the zoom.");
            }

            return;
        }

        foreach (Entry entry in _entries)
        {
            // Куллер робота скрывает никнейм, пока робот далеко от камеры.
            // Такая метка не может стать видимой от перемещения самой камеры:
            // робот должен сначала попасть в область камеры и включить её
            // обратно. Не проецируем её мировую точку каждый кадр.
            if (!entry.Visible)
            {
                entry.ApplyHidden();
                continue;
            }

            entry.ApplyScale(scale);

            Vector3 viewport = view.WorldToViewportPoint(entry.Position);
            bool visible = viewport.z > 0f &&
                viewport.x >= -0.15f && viewport.x <= 1.15f &&
                viewport.y >= -0.15f && viewport.y <= 1.15f;
            if (!visible)
            {
                entry.ApplyHidden();
                continue;
            }

            Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(
                _root.panel, entry.Position, view);
            Vector2 local = _container!.WorldToLocal(panelPosition);
            entry.ApplyVisible(new Vector3(local.x, local.y, 0f));
        }
    }

    public void Dispose()
    {
        _entries.Clear();
        _root?.RemoveFromHierarchy();
        _root = null;
        _container = null;
    }

    private sealed class Entry : IWorldLabel
    {
        private readonly WorldLabels _owner;
        private readonly WorldLabelKind _kind;
        private const float PositionApplyEpsilonPx = 0.5f;

        // Зум квантуется пиксельной сеткой (PixelGrid.QuantizeOrthographicSize),
        // поэтому у камеры лишь несколько дискретных значений и запись кегля
        // случается на соседних шагах зума, а не каждый кадр. Порог оставлен
        // тем же приёмом, что и у позиции: дробная подстройка кегля ни на что
        // не влияет, а стиль без нужды помечается грязным.
        private const float ScaleApplyEpsilon = 0.002f;
        private const string OffscreenClass = "world-label-offscreen";

        // Source sprite tail tip is 8 px from its left edge.
        private const float TailAnchorSourceX = 8f;

        public VisualElement Frame { get; }
        public Label Label { get; }
        public Vector3 Position { get; private set; }
        public bool Visible { get; private set; } = true;

        private Vector3 _lastAppliedPosition;
        private bool _lastAppliedVisible;
        private bool _hasApplied;

        // База берётся из USS один раз, до первой записи: пока кегль не
        // переопределён, resolvedStyle отдаёт ровно то, что задано стилями,
        // и число 12 не дублируется в коде. Ноль означает «база ещё не
        // разложена» — тогда запись откладывается до следующего кадра.
        private float _baseFontSize;
        private TextShadow _baseShadow;
        private float _lastScale = -1f;
        private bool _sizeDirty;

        public Entry(WorldLabels owner, VisualElement frame, Label label, WorldLabelKind kind)
        {
            _owner = owner;
            Frame = frame;
            Label = label;
            _kind = kind;
        }

        public void SetText(string text) => Label.text = text;
        public void SetPosition(Vector3 position) => Position = position;
        public void SetVisible(bool visible) => Visible = visible;
        public void SetOpacity(float opacity) => Frame.style.opacity = Mathf.Clamp01(opacity);

        // Никнейм и пузырь масштабируются целиком: текст и 9-slice остаются
        // согласованы. У метки добычи масштабируются кегль и тень.
        public void ApplyScale(float scale)
        {
            if (_kind is WorldLabelKind.Nickname or WorldLabelKind.ChatBubble)
            {
                if (_lastScale <= 0f || !Mathf.Approximately(scale, _lastScale))
                {
                    Frame.style.scale = new Scale(new Vector3(scale, scale, 1f));
                    _lastScale = scale;
                    _sizeDirty = true;
                }

                return;
            }

            if (_baseFontSize <= 0f && !TryCaptureBase())
            {
                return;
            }

            if (_lastScale > 0f && Mathf.Abs(scale - _lastScale) <= ScaleApplyEpsilon)
            {
                return;
            }

            Label.style.fontSize = _baseFontSize * scale;
            TextShadow shadow = _baseShadow;
            shadow.offset *= scale;
            Label.style.textShadow = shadow;

            _lastScale = scale;

            // Смена кегля меняет бокс, а якорь облака задан его размером:
            // пересчёт смещения обязан повториться на кадре, где раскладка уже
            // отдаёт новые width/height. Пока этого не случилось, смещение
            // записано по старому размеру, и ApplyVisible его не запоминает.
            _sizeDirty = true;
        }

        private bool TryCaptureBase()
        {
            IResolvedStyle style = Label.resolvedStyle;
            if (!IsResolved(style.fontSize) || style.fontSize <= 0f)
            {
                return false;
            }

            _baseFontSize = style.fontSize;
            _baseShadow = style.textShadow;
            return true;
        }

        private static bool IsResolved(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        // Запись в style.translate помечает стили элемента грязными без
        // сравнения значений, поэтому безусловная запись каждый кадр держала
        // всю панель в состоянии style-dirty: дерево пересчитывало стили,
        // раскладку и перекраску, даже когда метки стояли на месте. Пишем
        // только при смене видимости или сдвиге сверх половины пикселя —
        // тем же приёмом, что MissionArrowUI.
        public void ApplyVisible(Vector3 position)
        {
            bool visibilityChanged = !_hasApplied || !_lastAppliedVisible;
            bool positionChanged = !_hasApplied ||
                (position - _lastAppliedPosition).sqrMagnitude >
                    PositionApplyEpsilonPx * PositionApplyEpsilonPx;
            if (!visibilityChanged && !positionChanged && !_sizeDirty)
            {
                return;
            }

            // Показываем независимо от того, посчитан ли размер. Раньше здесь
            // стоял выход по TryResolveSize, и это был дедлок: скрытие идёт
            // через visibility, размер доступен всегда, но на первом кадре он
            // ещё NaN, ранний выход оставлял метку скрытой, и снять скрытие
            // мог только ApplyVisible — то есть уже никогда. С переиспользованным
            // пузырём из пула сообщение переставало показываться навсегда.
            SetOffscreen(false);
            _lastAppliedVisible = true;
            _hasApplied = true;

            if (!TryResolveSize(out Vector2 size))
            {
                // Смещение не пишем, но и позицию не запоминаем: флаг
                // positionChanged останется поднятым, и следующий кадр повторит
                // попытку, как только раскладка посчитает размер.
                return;
            }

            // Якорь пузыря — кончик хвоста спрайта; никнейма — нижний левый угол.
            Vector3 offset = _kind == WorldLabelKind.ChatBubble
                ? new Vector3(position.x - (TailAnchorSourceX * _lastScale), position.y - (size.y * _lastScale))
                : _kind == WorldLabelKind.Nickname
                    ? new Vector3(position.x, position.y - size.y * _lastScale)
                    : position;
            Frame.style.translate = new Translate(offset.x, offset.y);
            if (_sizeDirty)
            {
                // Смещение только что посчитано по боксу прежнего кегля.
                // Запоминать позицию нельзя: сдвига больше не будет, флаг
                // positionChanged не поднимется, и якорь облака навсегда
                // останется на старом размере. Следующий кадр повторяет
                // пересчёт по уже разложенному боксу.
                _sizeDirty = false;
                return;
            }

            _lastAppliedPosition = position;
        }

        public void ApplyHidden()
        {
            if (_hasApplied && !_lastAppliedVisible)
            {
                return;
            }

            SetOffscreen(true);
            _lastAppliedVisible = false;
            _hasApplied = true;
        }

        public void Dispose()
        {
            _owner._entries.Remove(this);
            Frame.RemoveFromHierarchy();
        }

        // Скрытие через visibility, а не через UIState.SetHidden с display:none.
        // display:none выводит элемент из раскладки, ширина и высота становятся
        // NaN, и посчитать нижний центр больше нечем. visibility:hidden элемент
        // раскладывается, поэтому повторный показ всегда знает свой размер.
        private void SetOffscreen(bool offscreen) =>
            Frame.EnableInClassList(OffscreenClass, offscreen);

        private bool TryResolveSize(out Vector2 size)
        {
            if (_kind != WorldLabelKind.ChatBubble && _kind != WorldLabelKind.Nickname)
            {
                size = Vector2.zero;
                return true;
            }

            IResolvedStyle style = Frame.resolvedStyle;
            if (float.IsNaN(style.width) || float.IsNaN(style.height) ||
                style.width <= 0f || style.height <= 0f)
            {
                size = Vector2.zero;
                return false;
            }

            size = new Vector2(style.width, style.height);
            return true;
        }
    }
}
