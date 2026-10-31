#nullable enable

using System;
using System.IO;
using Kern.Core;
using Kern.Persistence;
using MinesServer.Data;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor
{

public sealed class MapbConverter : EditorWindow
{
    [MenuItem("Kern/World/Convert Server Mapb")]
    public static void ShowWindow()
    {
        GetWindow<MapbConverter>("Mapb Converter");
    }

    private string _serverMapPath = string.Empty;
    private string _serverWorldName = "pallada";
    private int _chunksW = 313;  // 10016 / 32 = 313 (current Pallada width)
    private int _chunksH = 1250; // 40000 / 32 = 1250
    private int _chunkSize = 32;
    private string _outputFolder = "Assets/StreamingAssets/WorldMaps";
    private bool _includeRoadLayer = true;

    private Vector2 _scrollPos;

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.LabelField("Server Map Converter", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Server Configuration", EditorStyles.boldLabel);
        _serverMapPath = EditorGUILayout.TextField("Server Map Folder", _serverMapPath);
        _serverWorldName = EditorGUILayout.TextField("World Name", _serverWorldName);
        _chunksW = EditorGUILayout.IntField("Chunks Width (W)", _chunksW);
        _chunksH = EditorGUILayout.IntField("Chunks Height (H)", _chunksH);
        _chunkSize = EditorGUILayout.IntField("Chunk Size", _chunkSize);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output Configuration", EditorStyles.boldLabel);
        _outputFolder = EditorGUILayout.TextField("Output Folder (relative to project)", _outputFolder);
        _includeRoadLayer = EditorGUILayout.Toggle("Merge Road Layer", _includeRoadLayer);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Expected Files", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"  Cells: {_serverWorldName}.mapb");
        if (_includeRoadLayer)
        {
            EditorGUILayout.LabelField($"  Road:  {_serverWorldName}_road.mapb");
        }

        EditorGUILayout.LabelField($"  Durability: {_serverWorldName}_durability.mapb (ignored)");

        EditorGUILayout.Space();

        bool canConvert = !string.IsNullOrEmpty(_serverMapPath)
                       && Directory.Exists(_serverMapPath)
                       && FileExists(_serverWorldName + ".mapb")
                       && (!_includeRoadLayer || FileExists(_serverWorldName + "_road.mapb"));

        using (new EditorGUI.DisabledScope(!canConvert))
        {
            if (GUILayout.Button("Convert to Client Format", GUILayout.Height(40)))
            {
                Convert();
            }
        }

        if (!canConvert)
        {
            EditorGUILayout.HelpBox("Please specify valid server map folder with required files.", MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Info", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Server format: raw byte arrays (no header, no offset table, no RLE)\n" +
            "Client format: header (16 bytes) + offset table (int64[]) + RLE compressed chunks\n" +
            (_includeRoadLayer
                ? "Merge logic: cells[x,y] == 0 (Unloaded) ? road[x,y] : cells[x,y]\n"
                : "Road layer is disabled; only cells data is converted\n") +
            "Durability layer is ignored (client doesn't use it).",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private bool FileExists(string fileName)
    {
        return File.Exists(Path.Combine(_serverMapPath, fileName));
    }

    private async void Convert()
    {
        long worldWidth = (long)_chunksW * _chunkSize;
        long worldHeight = (long)_chunksH * _chunkSize;
        if (_chunksW <= 0 || _chunksH <= 0 ||
            _chunkSize != WorldGridDefaults.ChunkSize ||
            worldWidth > ushort.MaxValue || worldHeight > ushort.MaxValue)
        {
            EditorUtility.DisplayDialog(
                "Invalid World Dimensions",
                $"Client maps require positive chunk counts, chunk size " +
                $"{WorldGridDefaults.ChunkSize}, and world dimensions no larger than " +
                $"{ushort.MaxValue} cells. Current dimensions: {worldWidth}x{worldHeight} " +
                $"with chunk size {_chunkSize}.",
                "OK");
            return;
        }

        string cellsPath = Path.Combine(_serverMapPath, _serverWorldName + ".mapb");
        string roadPath = Path.Combine(_serverMapPath, _serverWorldName + "_road.mapb");
        string outputPath = Path.Combine(_outputFolder, _serverWorldName + "_cells.mapb");
        string temporaryOutputPath = outputPath + ".convert.tmp";
        string? missingRequiredPath = null;

        if (!File.Exists(cellsPath))
        {
            missingRequiredPath = cellsPath;
        }
        else if (_includeRoadLayer && !File.Exists(roadPath))
        {
            missingRequiredPath = roadPath;
        }

        if (missingRequiredPath != null)
        {
            EditorUtility.DisplayDialog("Error", $"Required input file not found: {missingRequiredPath}", "OK");
            return;
        }

        Directory.CreateDirectory(_outputFolder);

        try
        {
            EditorUtility.DisplayProgressBar("Converting Mapb", "Opening files...", 0f);

            long totalChunks = (long)_chunksW * _chunksH;
            int chunkArea = _chunkSize * _chunkSize;
            long cellsFileSize = new FileInfo(cellsPath).Length;
            long expectedFileSize = totalChunks * chunkArea;

            if (cellsFileSize < expectedFileSize)
            {
                string warnMsg = $"Cells file size ({cellsFileSize}) is smaller than expected ({expectedFileSize}). " +
                    "World dimensions in config may not match actual map size.";
                if (!EditorUtility.DisplayDialog("Warning", warnMsg, "Continue anyway", "Cancel"))
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }
            }

            using (FileStream cellsFs = File.OpenRead(cellsPath))
            using (FileStream? roadFs = _includeRoadLayer ? File.OpenRead(roadPath) : null)
            using (FileStream outFs = File.Create(temporaryOutputPath))
            using (BinaryWriter writer = new BinaryWriter(outFs))
            {
                // Write header: widthChunks, heightChunks, chunkSize, reserved
                writer.Write(_chunksW);
                writer.Write(_chunksH);
                writer.Write(_chunkSize);
                writer.Write(0); // reserved

                // Placeholder for offset table
                long offsetTablePos = outFs.Position;
                long[] chunkOffsets = new long[totalChunks];
                for (long i = 0; i < totalChunks; i++)
                {
                    writer.Write(0L); // placeholder
                }

                byte[] cellChunk = new byte[chunkArea];
                byte[]? roadChunk = _includeRoadLayer ? new byte[chunkArea] : null;
                byte[] mergedChunk = new byte[chunkArea];

                // Process chunks
                for (long cx = 0; cx < _chunksW; cx++)
                {
                    for (long cy = 0; cy < _chunksH; cy++)
                    {
                        long chunkIndex = cy + (_chunksH * cx); // Column-major!
                        float progress = (float)((cx * _chunksH) + cy) / totalChunks;

                        if (chunkIndex % 1000 == 0)
                        {
                            EditorUtility.DisplayProgressBar(
                                "Converting Mapb",
                                $"Processing chunk {chunkIndex}/{totalChunks} (cx={cx}, cy={cy})",
                                progress);
                        }

                        // Read cell chunk
                        long cellOffset = chunkIndex * chunkArea;
                        cellsFs.Seek(cellOffset, SeekOrigin.Begin);
                        int readCells = ReadChunk(cellsFs, cellChunk);
                        if (readCells < chunkArea)
                        {
                            Array.Clear(cellChunk, readCells, chunkArea - readCells);
                        }

                        // Read road chunk
                        if (_includeRoadLayer)
                        {
                            FileStream roadStream = roadFs ??
                                throw new InvalidOperationException("Road stream was not opened.");
                            byte[] roadData = roadChunk ??
                                throw new InvalidOperationException("Road buffer was not allocated.");
                            long roadOffset = chunkIndex * chunkArea;
                            roadStream.Seek(roadOffset, SeekOrigin.Begin);
                            int readRoad = ReadChunk(roadStream, roadData);
                            if (readRoad < chunkArea)
                            {
                                Array.Clear(roadData, readRoad, chunkArea - readRoad);
                            }
                        }

                        // Merge: cells == 0 (Unloaded) ? road : cells
                        for (int i = 0; i < chunkArea; i++)
                        {
                            mergedChunk[i] = _includeRoadLayer && cellChunk[i] == 0
                                ? roadChunk![i]
                                : cellChunk[i];
                        }

                        // Record offset BEFORE writing chunk data
                        chunkOffsets[chunkIndex] = outFs.Position;

                        WriteRLE(writer, mergedChunk);
                    }
                }

                // Rewrite offset table
                outFs.Position = offsetTablePos;
                foreach (long offset in chunkOffsets)
                {
                    writer.Write(offset);
                }

                writer.Flush();
                outFs.Flush(flushToDisk: true);
            }

            // Собранная карта сразу становится текущим форматом. Иначе каждый
            // мобильный клиент перекодирует сотни тысяч чанков при первом входе.
            await WorldLayer<CellType>.MigrateLegacyFileAsync(
                temporaryOutputPath,
                _chunksW,
                _chunksH,
                _chunkSize);

            if (File.Exists(outputPath))
            {
                File.Replace(temporaryOutputPath, outputPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryOutputPath, outputPath);
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.Refresh();

            long outputSize = new FileInfo(outputPath).Length;
            string successMsg = $"Converted successfully!\n\nOutput: {outputPath}\nSize: {FormatBytes(outputSize)}\nChunks: {totalChunks}\nChunk size: {_chunkSize}x{_chunkSize}";
            EditorUtility.DisplayDialog("Success", successMsg, "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"[MapbConverter] Conversion failed: {ex}");
            EditorUtility.DisplayDialog("Error", $"Conversion failed:\n{ex.Message}", "OK");
        }
        finally
        {
            if (File.Exists(temporaryOutputPath))
            {
                try
                {
                    File.Delete(temporaryOutputPath);
                }
                catch (IOException cleanupException)
                {
                    Debug.LogWarning(
                        $"[MapbConverter] Could not remove temporary output '{temporaryOutputPath}': " +
                        cleanupException.Message);
                }
                catch (UnauthorizedAccessException cleanupException)
                {
                    Debug.LogWarning(
                        $"[MapbConverter] Could not remove temporary output '{temporaryOutputPath}': " +
                        cleanupException.Message);
                }
            }
        }
    }

    private static int ReadChunk(FileStream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static void WriteRLE(BinaryWriter writer, byte[] data)
    {
        int i = 0;
        int len = data.Length;

        while (i < len)
        {
            byte val = data[i];
            int run = 1;

            // Max run length is ushort.MaxValue (65535)
            while (i + run < len && data[i + run] == val && run < 65535)
            {
                run++;
            }

            writer.Write((ushort)run);
            writer.Write(val);
            i += run;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = new[] {
"B", "KB", "MB", "GB"
 };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:F2} {suffixes[i]}";
    }
}
}
