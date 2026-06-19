using System.Buffers.Binary;
using System.Text;

namespace P12Bridge.Infrastructure;

internal static class BinaryPropertyListReader
{
    private const string Magic = "bplist00";
    private const int TrailerLength = 32;

    public static bool HasBinaryHeader(byte[] bytes) =>
        bytes.Length >= Magic.Length
        && Encoding.ASCII.GetString(bytes, 0, Magic.Length) == Magic;

    public static Dictionary<string, string> ReadTopLevelStringDictionary(byte[] bytes)
    {
        var reader = new Reader(bytes);
        return reader.ReadTopLevelStringDictionary();
    }

    private sealed class Reader
    {
        private readonly byte[] bytes;
        private readonly int offsetIntSize;
        private readonly int objectRefSize;
        private readonly int objectCount;
        private readonly int topObjectIndex;
        private readonly int offsetTableOffset;

        public Reader(byte[] bytes)
        {
            this.bytes = bytes;

            if (!HasBinaryHeader(bytes) || bytes.Length < Magic.Length + TrailerLength)
            {
                throw new FormatException("Binary plist header or trailer is missing.");
            }

            var trailerOffset = bytes.Length - TrailerLength;
            offsetIntSize = bytes[trailerOffset + 6];
            objectRefSize = bytes[trailerOffset + 7];
            objectCount = CheckedInt(ReadUnsigned(trailerOffset + 8, 8));
            topObjectIndex = CheckedInt(ReadUnsigned(trailerOffset + 16, 8));
            offsetTableOffset = CheckedInt(ReadUnsigned(trailerOffset + 24, 8));

            if (!IsSupportedSize(offsetIntSize)
                || !IsSupportedSize(objectRefSize)
                || objectCount <= 0
                || topObjectIndex < 0
                || topObjectIndex >= objectCount)
            {
                throw new FormatException("Binary plist trailer is invalid.");
            }

            var offsetTableLength = CheckedInt((ulong)objectCount * (ulong)offsetIntSize);
            if (offsetTableOffset < Magic.Length
                || offsetTableOffset > trailerOffset
                || offsetTableLength > trailerOffset - offsetTableOffset)
            {
                throw new FormatException("Binary plist offset table is invalid.");
            }
        }

        public Dictionary<string, string> ReadTopLevelStringDictionary() =>
            ReadStringDictionary(topObjectIndex);

        private Dictionary<string, string> ReadStringDictionary(int objectIndex)
        {
            var objectOffset = ReadObjectOffset(objectIndex);
            var marker = ReadByte(objectOffset);
            if ((marker & 0xF0) != 0xD0)
            {
                throw new FormatException("Binary plist top object is not a dictionary.");
            }

            var count = ReadObjectLength(objectOffset, marker, out var refsOffset);
            var keyRefsOffset = refsOffset;
            var valueRefsOffset = checked(keyRefsOffset + count * objectRefSize);
            RequireAvailable(valueRefsOffset, checked(count * objectRefSize));

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var keyRef = ReadObjectReference(keyRefsOffset + index * objectRefSize);
                var valueRef = ReadObjectReference(valueRefsOffset + index * objectRefSize);
                var key = ReadStringObject(keyRef);

                if (TryReadStringObject(valueRef, out var value))
                {
                    values[key] = value;
                }
            }

            return values;
        }

        private string ReadStringObject(int objectIndex) =>
            TryReadStringObject(objectIndex, out var value)
                ? value
                : throw new FormatException("Binary plist dictionary key is not a string.");

        private bool TryReadStringObject(int objectIndex, out string value)
        {
            value = string.Empty;
            var objectOffset = ReadObjectOffset(objectIndex);
            var marker = ReadByte(objectOffset);
            var type = marker & 0xF0;

            if (type != 0x50 && type != 0x60)
            {
                return false;
            }

            var length = ReadObjectLength(objectOffset, marker, out var contentOffset);
            var byteLength = checked(type == 0x60 ? length * 2 : length);
            RequireAvailable(contentOffset, byteLength);

            value = type == 0x60
                ? Encoding.BigEndianUnicode.GetString(bytes, contentOffset, byteLength)
                : Encoding.ASCII.GetString(bytes, contentOffset, byteLength);

            return true;
        }

        private int ReadObjectLength(int objectOffset, byte marker, out int contentOffset)
        {
            var lengthNibble = marker & 0x0F;
            if (lengthNibble < 0x0F)
            {
                contentOffset = checked(objectOffset + 1);
                return lengthNibble;
            }

            var lengthOffset = checked(objectOffset + 1);
            var lengthMarker = ReadByte(lengthOffset);
            if ((lengthMarker & 0xF0) != 0x10)
            {
                throw new FormatException("Binary plist extended length is not an integer.");
            }

            var integerByteLength = 1 << (lengthMarker & 0x0F);
            RequireAvailable(lengthOffset + 1, integerByteLength);
            contentOffset = checked(lengthOffset + 1 + integerByteLength);
            return CheckedInt(ReadUnsigned(lengthOffset + 1, integerByteLength));
        }

        private int ReadObjectOffset(int objectIndex)
        {
            if (objectIndex < 0 || objectIndex >= objectCount)
            {
                throw new FormatException("Binary plist object reference is out of range.");
            }

            var offset = CheckedInt(ReadUnsigned(offsetTableOffset + objectIndex * offsetIntSize, offsetIntSize));
            if (offset < Magic.Length || offset >= offsetTableOffset)
            {
                throw new FormatException("Binary plist object offset is out of range.");
            }

            return offset;
        }

        private int ReadObjectReference(int offset) =>
            CheckedInt(ReadUnsigned(offset, objectRefSize));

        private byte ReadByte(int offset)
        {
            RequireAvailable(offset, 1);
            return bytes[offset];
        }

        private ulong ReadUnsigned(int offset, int size)
        {
            RequireAvailable(offset, size);

            return size switch
            {
                1 => bytes[offset],
                2 => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, size)),
                4 => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, size)),
                8 => BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset, size)),
                _ => throw new FormatException("Binary plist integer size is unsupported.")
            };
        }

        private void RequireAvailable(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length || length > bytes.Length - offset)
            {
                throw new FormatException("Binary plist data is truncated.");
            }
        }

        private static bool IsSupportedSize(int size) =>
            size is 1 or 2 or 4 or 8;

        private static int CheckedInt(ulong value) =>
            value > int.MaxValue
                ? throw new FormatException("Binary plist value is too large.")
                : (int)value;
    }
}
