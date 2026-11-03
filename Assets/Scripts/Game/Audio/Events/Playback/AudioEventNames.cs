#nullable enable

using System.Collections.Generic;
using System.Text;
using MinesServer.Data;
using MinesServer.Networking.Contracts.Packets;

namespace Kern.Game.Audio.Events.Playback;

// Имя звукового события по значению перечисления: PascalCase превращается в
// sfx/snake_case. Результат кэшируется — перечисление конечно, а событий за
// кадр бывает много.
//
// Отдельный тип, потому что это перевод одного представления в другое и к
// жизненному циклу события отношения не имеет.
internal static class AudioEventNames
{
    private static readonly Dictionary<AudioEventType, string> s_cache = new();

    public static string Get(AudioEventType audioEvent)
    {
        if (s_cache.TryGetValue(audioEvent, out string? cachedName))
        {
            return cachedName;
        }

        string name = audioEvent.ToString();
        var builder = new StringBuilder("sfx/");
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        string result = builder.ToString();
        s_cache[audioEvent] = result;
        return result;
    }
}
