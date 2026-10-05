using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace LiteFactoryLauncher.Services;

public sealed class MinecraftServerListService
{
    public const string LightFactoryServerName = "Light Factory";
    public const string LightFactoryServerAddress = "lightfactory.mcsh.io";

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "logs",
        "server-list.log");

    public MinecraftServerListResult EnsureLightFactoryServer(string gameDirectory)
    {
        try
        {
            Log("[ServerList] Checking servers.dat");

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                return MinecraftServerListResult.Failed("Game directory is not configured.");
            }

            Directory.CreateDirectory(gameDirectory);
            var serverListPath = Path.Combine(gameDirectory, "servers.dat");
            var root = File.Exists(serverListPath)
                ? NbtFile.Read(serverListPath)
                : NbtFile.CreateEmptyServersDat();

            var servers = File.Exists(serverListPath)
                ? root.GetServerList()
                : root.GetOrCreateServerList();
            var existingServer = servers
                .OfType<NbtCompound>()
                .FirstOrDefault(server => string.Equals(
                    server.GetString("name"),
                    LightFactoryServerName,
                    StringComparison.OrdinalIgnoreCase));

            if (existingServer == null)
            {
                servers.Add(new NbtCompound(new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                {
                    ["name"] = new NbtString(LightFactoryServerName),
                    ["ip"] = new NbtString(LightFactoryServerAddress)
                }));

                NbtFile.WriteSafely(serverListPath, root);
                Log("[ServerList] Added Light Factory server");
                return MinecraftServerListResult.Updated("Added Light Factory server.");
            }

            if (string.Equals(existingServer.GetString("ip"), LightFactoryServerAddress, StringComparison.Ordinal))
            {
                Log("[ServerList] Light Factory server already exists");
                return MinecraftServerListResult.Unchanged("Light Factory server already exists.");
            }

            existingServer.SetString("ip", LightFactoryServerAddress);
            NbtFile.WriteSafely(serverListPath, root);
            Log("[ServerList] Updated Light Factory server address");
            return MinecraftServerListResult.Updated("Updated Light Factory server address.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            var message = $"Could not update servers.dat: {ex.Message}";
            Log($"[ServerList] {message}");
            return MinecraftServerListResult.Failed(message);
        }
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine(message);
        Debug.WriteLine(message);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write server-list log: {ex}");
            Debug.WriteLine($"Failed to write server-list log: {ex}");
        }
    }

    private static class NbtFile
    {
        public static NbtRoot CreateEmptyServersDat()
        {
            return new NbtRoot("", new NbtCompound(new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["servers"] = new NbtList(NbtTagType.Compound, new List<NbtTag>())
            }));
        }

        public static NbtRoot Read(string path)
        {
            using var fileStream = File.OpenRead(path);
            using var reader = new BinaryReader(fileStream, Encoding.UTF8, leaveOpen: false);

            var type = ReadTagType(reader);
            if (type != NbtTagType.Compound)
            {
                throw new InvalidDataException("servers.dat root tag is not a compound.");
            }

            var rootName = ReadString(reader);
            var root = ReadPayload(reader, type) as NbtCompound
                ?? throw new InvalidDataException("servers.dat root payload is invalid.");

            return new NbtRoot(rootName, root);
        }

        public static void WriteSafely(string path, NbtRoot root)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidDataException("servers.dat directory could not be resolved.");
            }

            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var fileStream = File.Create(tempPath))
                using (var writer = new BinaryWriter(fileStream, Encoding.UTF8, leaveOpen: false))
                {
                    WriteTagType(writer, NbtTagType.Compound);
                    WriteString(writer, root.Name);
                    WritePayload(writer, root.Value);
                }

                ValidateWrittenServerList(tempPath);

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static void ValidateWrittenServerList(string tempPath)
        {
            var root = Read(tempPath);
            var servers = root.GetServerList();
            var lightFactoryServer = servers
                .OfType<NbtCompound>()
                .FirstOrDefault(server => string.Equals(
                    server.GetString("name"),
                    LightFactoryServerName,
                    StringComparison.OrdinalIgnoreCase));

            if (lightFactoryServer == null)
            {
                throw new InvalidDataException("Written servers.dat does not contain the Light Factory server.");
            }

            if (!string.Equals(lightFactoryServer.GetString("ip"), LightFactoryServerAddress, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Written servers.dat contains an invalid Light Factory server address.");
            }
        }

        private static NbtTag ReadPayload(BinaryReader reader, NbtTagType type)
        {
            return type switch
            {
                NbtTagType.Byte => new NbtByte(reader.ReadByte()),
                NbtTagType.Short => new NbtShort(ReadInt16(reader)),
                NbtTagType.Int => new NbtInt(ReadInt32(reader)),
                NbtTagType.Long => new NbtLong(ReadInt64(reader)),
                NbtTagType.Float => new NbtFloat(ReadSingle(reader)),
                NbtTagType.Double => new NbtDouble(ReadDouble(reader)),
                NbtTagType.ByteArray => ReadByteArray(reader),
                NbtTagType.String => new NbtString(ReadString(reader)),
                NbtTagType.List => ReadList(reader),
                NbtTagType.Compound => ReadCompound(reader),
                NbtTagType.IntArray => ReadIntArray(reader),
                NbtTagType.LongArray => ReadLongArray(reader),
                _ => throw new InvalidDataException($"Unsupported NBT tag type: {type}")
            };
        }

        private static NbtByteArray ReadByteArray(BinaryReader reader)
        {
            var length = ReadLength(reader, "byte array");
            return new NbtByteArray(reader.ReadBytes(length));
        }

        private static NbtList ReadList(BinaryReader reader)
        {
            var elementType = ReadTagType(reader);
            var length = ReadLength(reader, "list");
            var items = new List<NbtTag>(length);

            for (var index = 0; index < length; index++)
            {
                items.Add(ReadPayload(reader, elementType));
            }

            return new NbtList(elementType, items);
        }

        private static NbtCompound ReadCompound(BinaryReader reader)
        {
            var tags = new Dictionary<string, NbtTag>(StringComparer.Ordinal);

            while (true)
            {
                var type = ReadTagType(reader);
                if (type == NbtTagType.End)
                {
                    return new NbtCompound(tags);
                }

                var name = ReadString(reader);
                tags[name] = ReadPayload(reader, type);
            }
        }

        private static NbtIntArray ReadIntArray(BinaryReader reader)
        {
            var length = ReadLength(reader, "int array");
            var values = new int[length];

            for (var index = 0; index < length; index++)
            {
                values[index] = ReadInt32(reader);
            }

            return new NbtIntArray(values);
        }

        private static NbtLongArray ReadLongArray(BinaryReader reader)
        {
            var length = ReadLength(reader, "long array");
            var values = new long[length];

            for (var index = 0; index < length; index++)
            {
                values[index] = ReadInt64(reader);
            }

            return new NbtLongArray(values);
        }

        private static void WriteNamedTag(BinaryWriter writer, string name, NbtTag tag)
        {
            WriteTagType(writer, tag.Type);
            WriteString(writer, name);
            WritePayload(writer, tag);
        }

        private static void WritePayload(BinaryWriter writer, NbtTag tag)
        {
            switch (tag)
            {
                case NbtByte value:
                    writer.Write(value.Value);
                    break;
                case NbtShort value:
                    WriteInt16(writer, value.Value);
                    break;
                case NbtInt value:
                    WriteInt32(writer, value.Value);
                    break;
                case NbtLong value:
                    WriteInt64(writer, value.Value);
                    break;
                case NbtFloat value:
                    WriteSingle(writer, value.Value);
                    break;
                case NbtDouble value:
                    WriteDouble(writer, value.Value);
                    break;
                case NbtByteArray value:
                    WriteInt32(writer, value.Value.Length);
                    writer.Write(value.Value);
                    break;
                case NbtString value:
                    WriteString(writer, value.Value);
                    break;
                case NbtList value:
                    WriteTagType(writer, value.ElementType);
                    WriteInt32(writer, value.Items.Count);
                    foreach (var item in value.Items)
                    {
                        WritePayload(writer, item);
                    }

                    break;
                case NbtCompound value:
                    foreach (var item in value.Tags)
                    {
                        WriteNamedTag(writer, item.Key, item.Value);
                    }

                    WriteTagType(writer, NbtTagType.End);
                    break;
                case NbtIntArray value:
                    WriteInt32(writer, value.Value.Length);
                    foreach (var item in value.Value)
                    {
                        WriteInt32(writer, item);
                    }

                    break;
                case NbtLongArray value:
                    WriteInt32(writer, value.Value.Length);
                    foreach (var item in value.Value)
                    {
                        WriteInt64(writer, item);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported NBT tag type: {tag.Type}");
            }
        }

        private static NbtTagType ReadTagType(BinaryReader reader)
        {
            return (NbtTagType)reader.ReadByte();
        }

        private static void WriteTagType(BinaryWriter writer, NbtTagType type)
        {
            writer.Write((byte)type);
        }

        private static string ReadString(BinaryReader reader)
        {
            var length = ReadUInt16(reader);
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException("NBT string ended unexpectedly.");
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("NBT string is too long.");
            }

            WriteUInt16(writer, (ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static int ReadLength(BinaryReader reader, string context)
        {
            var length = ReadInt32(reader);
            if (length < 0)
            {
                throw new InvalidDataException($"NBT {context} has a negative length.");
            }

            return length;
        }

        private static short ReadInt16(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(short));
            if (bytes.Length != sizeof(short))
            {
                throw new EndOfStreamException("NBT short ended unexpectedly.");
            }

            return (short)((bytes[0] << 8) | bytes[1]);
        }

        private static ushort ReadUInt16(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(ushort));
            if (bytes.Length != sizeof(ushort))
            {
                throw new EndOfStreamException("NBT unsigned short ended unexpectedly.");
            }

            return (ushort)((bytes[0] << 8) | bytes[1]);
        }

        private static int ReadInt32(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(int));
            if (bytes.Length != sizeof(int))
            {
                throw new EndOfStreamException("NBT int ended unexpectedly.");
            }

            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private static long ReadInt64(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(long));
            if (bytes.Length != sizeof(long))
            {
                throw new EndOfStreamException("NBT long ended unexpectedly.");
            }

            return ((long)bytes[0] << 56) |
                   ((long)bytes[1] << 48) |
                   ((long)bytes[2] << 40) |
                   ((long)bytes[3] << 32) |
                   ((long)bytes[4] << 24) |
                   ((long)bytes[5] << 16) |
                   ((long)bytes[6] << 8) |
                   bytes[7];
        }

        private static float ReadSingle(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(float));
            if (bytes.Length != sizeof(float))
            {
                throw new EndOfStreamException("NBT float ended unexpectedly.");
            }

            Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        private static double ReadDouble(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(sizeof(double));
            if (bytes.Length != sizeof(double))
            {
                throw new EndOfStreamException("NBT double ended unexpectedly.");
            }

            Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }

        private static void WriteInt16(BinaryWriter writer, short value)
        {
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static void WriteUInt16(BinaryWriter writer, ushort value)
        {
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static void WriteInt32(BinaryWriter writer, int value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static void WriteInt64(BinaryWriter writer, long value)
        {
            writer.Write((byte)((value >> 56) & 0xFF));
            writer.Write((byte)((value >> 48) & 0xFF));
            writer.Write((byte)((value >> 40) & 0xFF));
            writer.Write((byte)((value >> 32) & 0xFF));
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static void WriteSingle(BinaryWriter writer, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private static void WriteDouble(BinaryWriter writer, double value)
        {
            var bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            writer.Write(bytes);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup only. The original servers.dat is never deleted here.
            }
        }
    }

    private sealed class NbtRoot
    {
        public NbtRoot(string name, NbtCompound value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public NbtCompound Value { get; }

        public List<NbtTag> GetOrCreateServerList()
        {
            if (Value.Tags.ContainsKey("servers"))
            {
                return GetServerList();
            }

            var servers = new NbtList(NbtTagType.Compound, new List<NbtTag>());
            Value.Tags["servers"] = servers;
            return servers.Items;
        }

        public List<NbtTag> GetServerList()
        {
            if (Value.Tags.TryGetValue("servers", out var tag) &&
                tag is NbtList list &&
                list.ElementType == NbtTagType.Compound)
            {
                return list.Items;
            }

            throw new InvalidDataException("servers.dat does not contain a valid servers list.");
        }
    }

    private enum NbtTagType : byte
    {
        End = 0,
        Byte = 1,
        Short = 2,
        Int = 3,
        Long = 4,
        Float = 5,
        Double = 6,
        ByteArray = 7,
        String = 8,
        List = 9,
        Compound = 10,
        IntArray = 11,
        LongArray = 12
    }

    private abstract class NbtTag
    {
        protected NbtTag(NbtTagType type)
        {
            Type = type;
        }

        public NbtTagType Type { get; }
    }

    private sealed class NbtByte(byte value) : NbtTag(NbtTagType.Byte)
    {
        public byte Value { get; } = value;
    }

    private sealed class NbtShort(short value) : NbtTag(NbtTagType.Short)
    {
        public short Value { get; } = value;
    }

    private sealed class NbtInt(int value) : NbtTag(NbtTagType.Int)
    {
        public int Value { get; } = value;
    }

    private sealed class NbtLong(long value) : NbtTag(NbtTagType.Long)
    {
        public long Value { get; } = value;
    }

    private sealed class NbtFloat(float value) : NbtTag(NbtTagType.Float)
    {
        public float Value { get; } = value;
    }

    private sealed class NbtDouble(double value) : NbtTag(NbtTagType.Double)
    {
        public double Value { get; } = value;
    }

    private sealed class NbtByteArray(byte[] value) : NbtTag(NbtTagType.ByteArray)
    {
        public byte[] Value { get; } = value;
    }

    private sealed class NbtString(string value) : NbtTag(NbtTagType.String)
    {
        public string Value { get; } = value;
    }

    private sealed class NbtList(NbtTagType elementType, List<NbtTag> items) : NbtTag(NbtTagType.List)
    {
        public NbtTagType ElementType { get; } = elementType;

        public List<NbtTag> Items { get; } = items;

        public void Add(NbtTag tag)
        {
            if (tag.Type != ElementType)
            {
                throw new InvalidDataException("NBT list item type does not match the list element type.");
            }

            Items.Add(tag);
        }
    }

    private sealed class NbtCompound(Dictionary<string, NbtTag> tags) : NbtTag(NbtTagType.Compound)
    {
        public Dictionary<string, NbtTag> Tags { get; } = tags;

        public string? GetString(string name)
        {
            return Tags.TryGetValue(name, out var tag) && tag is NbtString value
                ? value.Value
                : null;
        }

        public void SetString(string name, string value)
        {
            Tags[name] = new NbtString(value);
        }
    }

    private sealed class NbtIntArray(int[] value) : NbtTag(NbtTagType.IntArray)
    {
        public int[] Value { get; } = value;
    }

    private sealed class NbtLongArray(long[] value) : NbtTag(NbtTagType.LongArray)
    {
        public long[] Value { get; } = value;
    }
}

public sealed class MinecraftServerListResult
{
    private MinecraftServerListResult(bool success, bool changed, string message)
    {
        Success = success;
        Changed = changed;
        Message = message;
    }

    public bool Success { get; }

    public bool Changed { get; }

    public string Message { get; }

    public static MinecraftServerListResult Unchanged(string message)
    {
        return new MinecraftServerListResult(true, false, message);
    }

    public static MinecraftServerListResult Updated(string message)
    {
        return new MinecraftServerListResult(true, true, message);
    }

    public static MinecraftServerListResult Failed(string message)
    {
        return new MinecraftServerListResult(false, false, message);
    }
}
