#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kern.Editor;

[InitializeOnLoad]
public static class PlanetMapBaker
{
    private const string GeneratorPath = "scripts/generate_planet_maps.py";

    private static readonly string[] _RequiredMaps =
    {
        "Assets/Textures/UI/planet_albedo.png",
        "Assets/Textures/UI/planet_normal.png",
        "Assets/Textures/UI/planet_packed.png",
    };

    static PlanetMapBaker()
    {
        // Проверка отложена до первого простоя редактора: во время загрузки
        // база ассетов ещё импортируется, и отсутствующий файл на этом
        // этапе ничего не значит.
        EditorApplication.delayCall += WarnIfMapsMissing;
    }

    [MenuItem("Kern/Planet/Bake Maps")]
    public static void Bake()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string generator = Path.Combine(projectRoot, GeneratorPath);

        if (!File.Exists(generator))
        {
            Debug.LogError($"[PlanetMapBaker] Генератор не найден: {GeneratorPath}");
            return;
        }

        // Запекание 8K идёт минутами, поэтому оно синхронное и с прогрессом:
        // молча висящий редактор читается как зависший.
        EditorUtility.DisplayProgressBar("Запекание карт планеты", "Идёт расчёт полей...", 0.5f);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = GeneratorPath,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Debug.Log($"[PlanetMapBaker] Карты запечены.\n{output}");
            }
            else
            {
                Debug.LogError($"[PlanetMapBaker] Запекание не удалось (код {process.ExitCode}).\n{errors}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PlanetMapBaker] Не удалось запустить python3: {ex.Message}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
    }

    private static void WarnIfMapsMissing()
    {
        foreach (string path in _RequiredMaps)
        {
            if (File.Exists(path))
            {
                continue;
            }

            Debug.LogError(
                $"[PlanetMapBaker] Нет карты планеты: {path}\n"
                + "Карты не хранятся в репозитории. Запеки их: меню "
                + "Kern > Planet > Bake Maps, либо python3 " + GeneratorPath + "\n"
                + "Пока их нет, планета в главном меню будет серой сферой.");
            return;
        }
    }
}
