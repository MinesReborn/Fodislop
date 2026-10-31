#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;

namespace Kern.Persistence;

internal static class WorldChunkSchema
{
    internal const uint SchemaHashSeed = 2166136261;

    internal static class SchemaIdCache<T> where T : unmanaged
    {
        internal static readonly uint Value = WorldChunkSchema.ComputeSchemaId<T>();
    }

    internal static uint ComputeSchemaId<T>() where T : unmanaged
    {
        string typeName = typeof(T).FullName ?? typeof(T).Name;
        uint hash = WorldChunkSchema.SchemaHashSeed;
        foreach (char character in typeName)
        {
            hash = unchecked((hash ^ (byte)character) * 16777619);
            hash = unchecked((hash ^ (byte)(character >> 8)) * 16777619);
        }

        return hash;
    }

}
