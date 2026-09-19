#nullable enable

using UnityEditor;

namespace Kern.Editor;

// Перекомпиляция во время Play Mode запрещена для проекта.
//
// Перезагрузка домена посреди игры сбрасывает все несериализуемые поля, а
// зависимости VContainer ([Inject]) именно такие, и заново их никто не
// внедряет. Дальше любой OnEnable/OnDisable/OnDestroy падает на пустом поле.
// Код под это не защищается: правка скрипта применяется после выхода из игры.
//
// Настройка Unity (Preferences → General → Script Changes While Playing)
// хранится в EditorPrefs у каждого пользователя, поэтому выставляется здесь.
[InitializeOnLoad]
internal static class PlayModeCompilationPolicy
{
    private const string ScriptCompilationDuringPlayKey = "ScriptCompilationDuringPlay";

    // 0 — Recompile And Continue Playing,
    // 1 — Recompile After Finished Playing,
    // 2 — Stop Playing And Recompile.
    private const int RecompileAfterFinishedPlaying = 1;

    static PlayModeCompilationPolicy()
    {
        if (EditorPrefs.GetInt(ScriptCompilationDuringPlayKey, 0) != RecompileAfterFinishedPlaying)
        {
            EditorPrefs.SetInt(ScriptCompilationDuringPlayKey, RecompileAfterFinishedPlaying);
        }
    }
}
