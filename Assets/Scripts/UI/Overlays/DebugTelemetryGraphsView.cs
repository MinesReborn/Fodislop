#nullable enable

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

internal sealed class DebugTelemetryGraphsView
{
    public const int GraphHistoryCapacity = 160;

    private readonly float[] _frametimeHistory = new float[GraphHistoryCapacity];
    private readonly float[] _gcAllocHistory = new float[GraphHistoryCapacity];
    private int _historyIndex;
    private int _historyCount;

    public int HistoryCount => _historyCount;

    public void PushSample(float frameMs, float allocKb)
    {
        _frametimeHistory[_historyIndex] = frameMs;
        _gcAllocHistory[_historyIndex] = allocKb;
        _historyIndex = (_historyIndex + 1) % GraphHistoryCapacity;
        if (_historyCount < GraphHistoryCapacity)
        {
            _historyCount++;
        }
    }

    public void ComputeAverages(float currentFrameMs, out float avgFrameMs, out float minFrameMs, out float maxFrameMs)
    {
        float minMs = float.MaxValue;
        float maxMs = 0f;
        float sumMs = 0f;
        for (int i = 0; i < _historyCount; i++)
        {
            float sample = _frametimeHistory[i];
            if (sample < minMs)
            {
                minMs = sample;
            }

            if (sample > maxMs)
            {
                maxMs = sample;
            }

            sumMs += sample;
        }

        minFrameMs = minMs;
        maxFrameMs = maxMs;
        avgFrameMs = _historyCount > 0 ? sumMs / _historyCount : currentFrameMs;
    }

    public static VisualElement CreateGraphsRow(
        out VisualElement graphsRow,
        out Label frametimeHeader,
        out VisualElement frametimeCanvas,
        out Label memoryHeader,
        out VisualElement memoryCanvas,
        Action<MeshGenerationContext> onGenerateFrametime,
        Action<MeshGenerationContext> onGenerateMemory)
    {
        graphsRow = new VisualElement
        {
            name = "f3-graphs-row",
            pickingMode = PickingMode.Ignore,
        };
        graphsRow.style.flexDirection = FlexDirection.Row;
        graphsRow.style.justifyContent = Justify.FlexStart;
        graphsRow.style.alignItems = Align.FlexEnd;
        graphsRow.style.marginLeft = 260;
        graphsRow.style.marginBottom = 6;

        var ftContainer = CreateGraphCard("Frametime", 320, 70, out frametimeHeader, out frametimeCanvas, onGenerateFrametime);
        ftContainer.style.marginRight = 10;
        graphsRow.Add(ftContainer);

        var memContainer = CreateGraphCard("GC Allocation", 240, 70, out memoryHeader, out memoryCanvas, onGenerateMemory);
        graphsRow.Add(memContainer);

        return graphsRow;
    }

    private static VisualElement CreateGraphCard(
        string title,
        float width,
        float height,
        out Label headerLabel,
        out VisualElement canvasElement,
        Action<MeshGenerationContext> generateCallback)
    {
        var container = new VisualElement
        {
            pickingMode = PickingMode.Ignore,
        };
        container.style.backgroundColor = new Color(0.03f, 0.03f, 0.03f, 0.85f);
        container.style.borderTopLeftRadius = 4;
        container.style.borderTopRightRadius = 4;
        container.style.borderBottomLeftRadius = 4;
        container.style.borderBottomRightRadius = 4;
        container.style.borderLeftColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
        container.style.borderRightColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
        container.style.borderTopColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
        container.style.borderBottomColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
        container.style.borderLeftWidth = 1;
        container.style.borderRightWidth = 1;
        container.style.borderTopWidth = 1;
        container.style.borderBottomWidth = 1;
        container.style.paddingLeft = 8;
        container.style.paddingRight = 8;
        container.style.paddingTop = 6;
        container.style.paddingBottom = 6;
        container.style.width = width;

        headerLabel = new Label(title)
        {
            pickingMode = PickingMode.Ignore,
        };
        headerLabel.style.color = new Color(0.9f, 0.93f, 1f, 1f);
        headerLabel.style.fontSize = 10;
        headerLabel.style.marginBottom = 4;

        canvasElement = new VisualElement
        {
            pickingMode = PickingMode.Ignore,
        };
        canvasElement.style.width = Length.Percent(100);
        canvasElement.style.height = height;
        canvasElement.generateVisualContent += generateCallback;

        container.Add(headerLabel);
        container.Add(canvasElement);
        return container;
    }

    public void GenerateFrametimeGraph(MeshGenerationContext context, VisualElement canvas)
    {
        if (_historyCount == 0)
        {
            return;
        }

        float canvasWidth = canvas.resolvedStyle.width > 0 ? canvas.resolvedStyle.width : 304f;
        float canvasHeight = canvas.resolvedStyle.height > 0 ? canvas.resolvedStyle.height : 70f;

        int totalBars = _historyCount;

        // 1 bg quad + 2 guide line quads + totalBars quads
        int quadCount = 1 + 2 + totalBars;
        int vertexCount = quadCount * 4;
        int indexCount = quadCount * 6;

        var mesh = context.Allocate(vertexCount, indexCount);

        int vertIdx = 0;

        void EmitQuad(Rect rect, Color32 color)
        {
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = color });

            mesh.SetNextIndex((ushort)(vertIdx + 0));
            mesh.SetNextIndex((ushort)(vertIdx + 1));
            mesh.SetNextIndex((ushort)(vertIdx + 2));
            mesh.SetNextIndex((ushort)(vertIdx + 2));
            mesh.SetNextIndex((ushort)(vertIdx + 3));
            mesh.SetNextIndex((ushort)(vertIdx + 0));

            vertIdx += 4;
        }

        // 1. Background
        EmitQuad(new Rect(0, 0, canvasWidth, canvasHeight), new Color32(3, 3, 3, 242));

        // 2. Guide lines
        const float maxGraphMs = 33.3f;
        float y60 = canvasHeight - (16.7f / maxGraphMs * canvasHeight);
        float y30 = canvasHeight - (33.3f / maxGraphMs * canvasHeight);
        EmitQuad(new Rect(0, y60, canvasWidth, 1f), new Color32(51, 217, 77, 115));
        EmitQuad(new Rect(0, y30, canvasWidth, 1f), new Color32(255, 140, 26, 102));

        // 3. Bars
        float barWidth = canvasWidth / GraphHistoryCapacity;
        int startIndex = (_historyIndex - _historyCount + GraphHistoryCapacity) % GraphHistoryCapacity;

        for (int i = 0; i < _historyCount; i++)
        {
            int bufIdx = (startIndex + i) % GraphHistoryCapacity;
            float frameMs = _frametimeHistory[bufIdx];
            float barHeight = Mathf.Clamp(frameMs / maxGraphMs * canvasHeight, 2f, canvasHeight);
            float x = i * barWidth;
            float y = canvasHeight - barHeight;
            float w = Mathf.Max(1f, barWidth - 0.5f);

            Color32 barColor = frameMs switch
            {
                <= 16.7f => new Color32(64, 217, 89, 217),  // Green <= 60 fps
                <= 33.4f => new Color32(242, 204, 51, 217), // Yellow <= 30 fps
                _ => new Color32(242, 77, 64, 242),        // Red < 30 fps
            };

            EmitQuad(new Rect(x, y, w, barHeight), barColor);
        }
    }

    public void GenerateMemoryGraph(MeshGenerationContext context, VisualElement canvas)
    {
        if (_historyCount == 0)
        {
            return;
        }

        float canvasWidth = canvas.resolvedStyle.width > 0 ? canvas.resolvedStyle.width : 224f;
        float canvasHeight = canvas.resolvedStyle.height > 0 ? canvas.resolvedStyle.height : 70f;

        int totalBars = _historyCount;
        int quadCount = 1 + totalBars;
        int vertexCount = quadCount * 4;
        int indexCount = quadCount * 6;

        var mesh = context.Allocate(vertexCount, indexCount);

        int vertIdx = 0;

        void EmitQuad(Rect rect, Color32 color)
        {
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = color });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = color });

            mesh.SetNextIndex((ushort)(vertIdx + 0));
            mesh.SetNextIndex((ushort)(vertIdx + 1));
            mesh.SetNextIndex((ushort)(vertIdx + 2));
            mesh.SetNextIndex((ushort)(vertIdx + 2));
            mesh.SetNextIndex((ushort)(vertIdx + 3));
            mesh.SetNextIndex((ushort)(vertIdx + 0));

            vertIdx += 4;
        }

        // 1. Background
        EmitQuad(new Rect(0, 0, canvasWidth, canvasHeight), new Color32(3, 3, 3, 242));

        // 2. Bars
        const float maxAllocKb = 64f;
        float barWidth = canvasWidth / GraphHistoryCapacity;
        int startIndex = (_historyIndex - _historyCount + GraphHistoryCapacity) % GraphHistoryCapacity;

        for (int i = 0; i < _historyCount; i++)
        {
            int bufIdx = (startIndex + i) % GraphHistoryCapacity;
            float allocKb = _gcAllocHistory[bufIdx];
            float barHeight = Mathf.Clamp(allocKb / maxAllocKb * canvasHeight, 1f, canvasHeight);
            float x = i * barWidth;
            float y = canvasHeight - barHeight;
            float w = Mathf.Max(1f, barWidth - 0.5f);

            Color32 barColor = allocKb switch
            {
                <= 4f => new Color32(77, 179, 255, 217),   // Blue <= 4KB
                <= 16f => new Color32(102, 230, 230, 217), // Cyan <= 16KB
                <= 40f => new Color32(242, 191, 51, 217),  // Orange
                _ => new Color32(242, 77, 64, 242),       // Red > 40KB
            };

            EmitQuad(new Rect(x, y, w, barHeight), barColor);
        }
    }
}
