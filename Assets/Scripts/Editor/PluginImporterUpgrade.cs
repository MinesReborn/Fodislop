#nullable enable

using UnityEditor;
using UnityEngine;

namespace Fodinae.EditorTools
{
    /// <summary>
    /// Пересохраняет .meta нативных плагинов в текущей версии сериализации.
    /// </summary>
    /// <remarks>
    /// Unity ругается «PluginImporter object at version 1, below the supported
    /// minimum (2). Open and re-save the file to upgrade» на метаданные
    /// вендорных пакетов. Руками их не переписать: во второй версии
    /// platformData имеет другую форму, и ошибка молча отключит плагин на части
    /// платформ. Правильный путь — дать это сделать самому импортёру:
    /// SaveAndReimport читает старую запись и пишет новую.
    /// </remarks>
    internal static class PluginImporterUpgrade
    {
        [MenuItem("Tools/Fodinae/Пересохранить метаданные плагинов")]
        private static void ResaveAll()
        {
            int upgraded = 0;
            foreach (string guid in AssetDatabase.FindAssets(string.Empty))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not PluginImporter importer)
                {
                    continue;
                }

                importer.SaveAndReimport();
                upgraded++;
            }

            Debug.Log($"[PluginImporterUpgrade] Пересохранено импортёров плагинов: {upgraded}.");
        }
    }
}
