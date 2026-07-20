using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public class MapbConverter : EditorWindow
    {
        [MenuItem("Tools/Convert Server Mapb")]
        public static void ShowWindow()
        {
            GetWindow<MapbConverter>("Mapb Converter");
        }

        private string _serverMapPath = "";
        private string _serverWorldName = "pallada";
        private int _chunksW = 157;  // 5000 / 32 = 156.25 -> 157
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
            EditorGUILayout.LabelField($"  Road:  {_serverWorldName}_road.mapb");
            EditorGUILayout.LabelField($"  Durability: {_serverWorldName}_durability.mapb (ignored)");

            EditorGUILayout.Space();

            bool canConvert = !string.IsNullOrEmpty(_serverMapPath)
                           && Directory.Exists(_serverMapPath)
                           && FileExists(_serverWorldName + ".mapb")
                           && FileExists(_serverWorldName + "_road.mapb");

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
                "Merge logic: cells[x,y] == 0 (Unloaded) ? road[x,y] : cells[x,y]\n" +
                "Durability layer is ignored (client doesn't use it).",
                MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        private bool FileExists(string fileName)
        {
            return File.Exists(Path.Combine(_serverMapPath, fileName));
        }

        private void Convert()
        {
            string cellsPath = Path.Combine(_serverMapPath, _serverWorldName + ".mapb");
            string roadPath = Path.Combine(_serverMapPath, _serverWorldName + "_road.mapb");
            string outputPath = Path.Combine(_outputFolder, _serverWorldName + "_cells.mapb");

            if (!File.Exists(cellsPath))
            {
                EditorUtility.DisplayDialog("Error", $"Cells file not found: {cellsPath}", "OK");
                return;
            }

            if (!File.Exists(roadPath))
            {
                EditorUtility.DisplayDialog("Error", $"Road file not found: {roadPath}", "OK");
                return;
            }

            string outputDir = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            try
            {
                EditorUtility.DisplayProgressBar("Converting Mapb", "Opening files...", 0f);

                long totalChunks = (long)_chunksW * _chunksH;
                int chunkArea = _chunkSize * _chunkSize;
                long cellsFileSize = new FileInfo(cellsPath).Length;
                long roadFileSize = new FileInfo(roadPath).Length;
                long expectedFileSize = totalChunks * chunkArea;

                if (cellsFileSize < expectedFileSize)
                {
                    EditorUtility.DisplayDialog("Warning",
                        $"Cells file size ({cellsFileSize}) is smaller than expected ({expectedFileSize}). " +
                        "World dimensions in config may not match actual map size.", "Continue anyway");
                }

                using (FileStream cellsFs = File.OpenRead(cellsPath))
                using (FileStream roadFs = File.OpenRead(roadPath))
                using (FileStream outFs = File.Create(outputPath))
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
                    byte[] roadChunk = new byte[chunkArea];
                    byte[] mergedChunk = new byte[chunkArea];

                    // Process chunks
                    for (long cx = 0; cx < _chunksW; cx++)
                    {
                        for (long cy = 0; cy < _chunksH; cy++)
                        {
                            long chunkIndex = cy + _chunksH * cx; // Column-major!
                            float progress = (float)(cx * _chunksH + cy) / totalChunks;

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
                            int readCells = cellsFs.Read(cellChunk, 0, chunkArea);
                            if (readCells < chunkArea)
                            {
                                Array.Clear(cellChunk, readCells, chunkArea - readCells);
                            }

                            // Read road chunk
                            long roadOffset = chunkIndex * chunkArea;
                            roadFs.Seek(roadOffset, SeekOrigin.Begin);
                            int readRoad = roadFs.Read(roadChunk, 0, chunkArea);
                            if (readRoad < chunkArea)
                            {
                                Array.Clear(roadChunk, readRoad, chunkArea - readRoad);
                            }

                            // Merge: cells == 0 (Unloaded) ? road : cells
                            for (int i = 0; i < chunkArea; i++)
                            {
                                mergedChunk[i] = cellChunk[i] == 0 ? roadChunk[i] : cellChunk[i];
                            }

                            // RLE encode
                            var rle = EncodeRLE(mergedChunk);

                            // Record offset BEFORE writing chunk data
                            chunkOffsets[chunkIndex] = outFs.Position;

                            // Write RLE data: (ushort count, byte value) pairs
                            foreach (var (count, value) in rle)
                            {
                                writer.Write(count);
                                writer.Write(value);
                            }
                        }
                    }

                    // Rewrite offset table
                    outFs.Position = offsetTablePos;
                    foreach (long offset in chunkOffsets)
                    {
                        writer.Write(offset);
                    }

                    outFs.Flush();
                }

                EditorUtility.ClearProgressBar();

                AssetDatabase.Refresh();

                long outputSize = new FileInfo(outputPath).Length;
                EditorUtility.DisplayDialog("Success",
                    $"Converted successfully!\n\n" +
                    $"Output: {outputPath}\n" +
                    $"Size: {FormatBytes(outputSize)}\n" +
                    $"Chunks: {totalChunks}\n" +
                    $"Chunk size: {_chunkSize}x{_chunkSize}",
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[MapbConverter] Conversion failed: {ex}");
                EditorUtility.DisplayDialog("Error", $"Conversion failed:\n{ex.Message}", "OK");
            }
        }

        private static List<(ushort count, byte value)> EncodeRLE(byte[] data)
        {
            var result = new List<(ushort, byte)>();
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
                result.Add(((ushort)run, val));
                i += run;
            }
            return result;
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
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
