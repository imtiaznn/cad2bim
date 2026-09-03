namespace Cad2Bim.Bim.Ifc {
    /// <summary>
    /// IFC's 22-character compressed GUID: the 128 bits of a GUID in base 64 over the IFC
    /// alphabet, most significant bits first — one byte in the first two characters (so the
    /// leading character never exceeds '3'), then five 3-byte groups of four characters each.
    /// </summary>
    public static class IfcGuid {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        public static string New() => ToCompressed(Guid.NewGuid());

        public static string ToCompressed(Guid guid) {
            byte[] bytes = guid.ToByteArray();

            // .NET stores Data1..Data3 little-endian; the encoding wants the spec's byte order.
            uint data1 = BitConverter.ToUInt32(bytes, 0);
            ushort data2 = BitConverter.ToUInt16(bytes, 4);
            ushort data3 = BitConverter.ToUInt16(bytes, 6);

            uint[] groups = {
                data1 >> 24,
                data1 & 0xFFFFFF,
                ((uint)data2 << 8) | ((uint)data3 >> 8),
                ((uint)(data3 & 0xFF) << 16) | ((uint)bytes[8] << 8) | bytes[9],
                ((uint)bytes[10] << 16) | ((uint)bytes[11] << 8) | bytes[12],
                ((uint)bytes[13] << 16) | ((uint)bytes[14] << 8) | bytes[15]
            };

            Span<char> result = stackalloc char[22];
            int position = 0;

            for (int i = 0; i < groups.Length; i++) {
                int digits = i == 0 ? 2 : 4;
                uint value = groups[i];
                for (int d = digits - 1; d >= 0; d--) {
                    result[position + d] = Alphabet[(int)(value % 64)];
                    value /= 64;
                }
                position += digits;
            }

            return new string(result);
        }
    }
}
