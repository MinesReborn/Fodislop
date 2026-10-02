#nullable enable

using System.Collections.Generic;
using System.Text;
using MinesServer.Data;
using MinesServer.Networking.Shared.Packets;

namespace Kern.Game;

// Имя звукового события по значению перечисления: PascalCase превращается в
// sfx/snake_case. Результат кэшируется — перечисление конечно, а событий за
// кадр бывает много.
//
// Отдельный тип, потому что это перевод одного представления в другое и к
// жизненному циклу события отношения не имеет.
internal static class SfxEventNames
{
    private static readonly Dictionary<SFX, string> s_cache = new();

    public static string Get(SFX sfx)
    {
        if (s_cache.TryGetValue(sfx, out string? cachedName))
        {
            return cachedName;
        }

        string name = sfx.ToString();
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
        s_cache[sfx] = result;
        return result;
    }
}
