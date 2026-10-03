using System.IO;
using System.Text;
using PluginShelf.Models;

namespace PluginShelf.Services;

public sealed record PeInspection(PluginArchitecture Architecture, IReadOnlySet<string> Exports);

/// <summary>
/// Reads PE headers and export names without loading or executing a third-party plug-in.
/// </summary>
public static class PeInspector
{
    public static PeInspection? Inspect(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return null;

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 64 || peOffset > stream.Length - 24) return null;
            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550) return null;

            var machine = reader.ReadUInt16();
            var sectionCount = reader.ReadUInt16();
            stream.Position = peOffset + 20;
            var optionalHeaderSize = reader.ReadUInt16();
            stream.Position = peOffset + 24;
            if (optionalHeaderSize < 96 || stream.Position + optionalHeaderSize > stream.Length) return null;

            var optionalStart = stream.Position;
            var magic = reader.ReadUInt16();
            var directoryOffset = magic switch
            {
                0x10B => 96L,  // PE32
                0x20B => 112L, // PE32+
                _ => -1L
            };
            if (directoryOffset < 0 || optionalHeaderSize < directoryOffset + 8) return null;

            stream.Position = optionalStart + directoryOffset;
            var exportRva = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // export directory size
            var architecture = machine switch
            {
                0x014C => PluginArchitecture.X86,
                0x8664 => PluginArchitecture.X64,
                0xAA64 or 0xA641 or 0xA64E => PluginArchitecture.Arm64,
                _ => magic == 0x20B ? PluginArchitecture.X64 : PluginArchitecture.Unknown
            };

            var exports = exportRva == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : ReadExportNames(stream, reader, peOffset, sectionCount, optionalHeaderSize, exportRva);
            return new PeInspection(architecture, exports);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static HashSet<string> ReadExportNames(Stream stream, BinaryReader reader, long peOffset,
        ushort sectionCount, ushort optionalHeaderSize, uint exportRva)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize)>();
        var sectionTable = peOffset + 24L + optionalHeaderSize;
        if (sectionTable < 0 || sectionTable > stream.Length - (sectionCount * 40L)) return result;

        for (var i = 0; i < sectionCount; i++)
        {
            stream.Position = sectionTable + i * 40L;
            _ = reader.ReadBytes(8); // section name
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            sections.Add((virtualAddress, virtualSize, rawPointer, rawSize));
        }

        if (!TryRvaToOffset(exportRva, sections, out var exportOffset) || exportOffset > stream.Length - 40) return result;
        stream.Position = exportOffset + 24;
        var numberOfNames = reader.ReadUInt32();
        stream.Position = exportOffset + 32;
        var namesRva = reader.ReadUInt32();
        if (numberOfNames > 10000 || !TryRvaToOffset(namesRva, sections, out var namesOffset)) return result;

        for (uint i = 0; i < numberOfNames; i++)
        {
            var entryOffset = namesOffset + (long)i * 4;
            if (entryOffset < 0 || entryOffset > stream.Length - 4) break;
            stream.Position = entryOffset;
            var nameRva = reader.ReadUInt32();
            if (!TryRvaToOffset(nameRva, sections, out var nameOffset) || nameOffset >= stream.Length) continue;
            stream.Position = nameOffset;
            var bytes = new List<byte>(64);
            while (stream.Position < stream.Length && bytes.Count < 512)
            {
                var value = reader.ReadByte();
                if (value == 0) break;
                bytes.Add(value);
            }
            if (bytes.Count > 0)
                result.Add(Encoding.ASCII.GetString(bytes.ToArray()));
        }
        return result;
    }

    private static bool TryRvaToOffset(uint rva,
        IReadOnlyList<(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize)> sections,
        out long offset)
    {
        foreach (var section in sections)
        {
            var span = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress && (ulong)rva < (ulong)section.VirtualAddress + span)
            {
                offset = (long)section.RawPointer + (rva - section.VirtualAddress);
                return offset >= 0;
            }
        }
        offset = 0;
        return false;
    }
}
