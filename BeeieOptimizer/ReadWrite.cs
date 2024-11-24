public static class ReadWrite {
    public static byte ReadU8(byte[] buffer, int offset) {
        return buffer[offset];
    }
    public static ushort ReadU16(byte[] buffer, int offset) {
        return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
    }
    public static uint ReadU32(byte[] buffer, int offset) {
        return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }
    public static ulong ReadU64(byte[] buffer, int offset) {
        return ((ulong)buffer[offset] << 56) | ((ulong)buffer[offset + 1] << 48) | ((ulong)buffer[offset + 2] << 40) | ((ulong)buffer[offset + 3] << 32) |
                ((ulong)buffer[offset + 4] << 24) | ((ulong)buffer[offset + 5] << 16) | ((ulong)buffer[offset + 6] << 8) | buffer[offset + 7];
    }
    public static byte ReadU8(byte[] buffer, uint offset) {
        return buffer[offset];
    }
    public static ushort ReadU16(byte[] buffer, uint offset) {
        return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
    }
    public static uint ReadU32(byte[] buffer, uint offset) {
        return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }
    public static ulong ReadU64(byte[] buffer, uint offset) {
        return ((ulong)buffer[offset] << 56) | ((ulong)buffer[offset + 1] << 48) | ((ulong)buffer[offset + 2] << 40) | ((ulong)buffer[offset + 3] << 32) |
                ((ulong)buffer[offset + 4] << 24) | ((ulong)buffer[offset + 5] << 16) | ((ulong)buffer[offset + 6] << 8) | buffer[offset + 7];
    }
    public static void WriteU8(byte[] buffer, int offset, byte value) {
        buffer[offset] = value;
    }
    public static void WriteU16(byte[] buffer, int offset, ushort value) {
        offset += 2;
        for (int i = 0; i < 2; i++) {
            buffer[--offset] = (byte)value;
            value >>= 8;
        }
    }
    public static void WriteU32(byte[] buffer, int offset, uint value) {
        offset += 4;
        for (int i = 0; i < 4; i++) {
            buffer[--offset] = (byte)value;
            value >>= 8;
        }
    }
    public static void WriteU64(byte[] buffer, int offset, ulong value) {
        offset += 8;
        for (int i = 0; i < 8; i++) {
            buffer[--offset] = (byte)value;
            value >>= 8;
        }
    }
}