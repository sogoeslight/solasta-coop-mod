using System;

namespace SolastaCoop
{
    internal static class TravelWire
    {
        internal const byte EventCode = 190;
        internal const string Magic = "SolastaCoopView.travel/1";
        internal const int MaxFrameBytes = 131072;
        internal const int Hello = 0, Frame = 1, End = 2, Pointer = 3;
        internal static bool Valid(object[] data)
        {
            if (data == null || data.Length < 3 || !Object.Equals(data[0], Magic) || !(data[1] is int)) return false;
            int kind = (int)data[1];
            if (kind == Hello) return data.Length == 3 && data[2] is bool;
            if (kind == End) return data.Length == 3 && data[2] is int;
            if (kind == Frame)
                return data.Length == 4 && data[2] is int && data[3] is byte[] && Jpeg((byte[])data[3]);
            if (kind == Pointer)
                return data.Length == 6 && data[2] is int && data[3] is float && data[4] is float && data[5] is bool &&
                    Unit((float)data[3]) && Unit((float)data[4]);
            return false;
        }
        internal static bool Unit(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value) && value >= 0 && value <= 1; }
        // Check dimensions before Unity allocates a decompressed texture from a remote payload.
        internal static bool Jpeg(byte[] data)
        {
            if (data == null || data.Length < 12 || data.Length > MaxFrameBytes || data[0] != 255 || data[1] != 216) return false;
            int p = 2;
            while (p + 3 < data.Length)
            {
                if (data[p++] != 255) return false;
                while (p < data.Length && data[p] == 255) p++;
                if (p >= data.Length) return false;
                int marker = data[p++];
                if (marker == 217 || marker == 218) return false;
                if (marker == 216 || marker == 1 || (marker >= 208 && marker <= 215)) continue;
                if (p + 1 >= data.Length) return false;
                int length = (data[p] << 8) + data[p + 1];
                if (length < 2 || p + length > data.Length) return false;
                if ((marker >= 192 && marker <= 195) || (marker >= 197 && marker <= 199) || (marker >= 201 && marker <= 203) || (marker >= 205 && marker <= 207))
                {
                    if (length < 8) return false;
                    int h = (data[p + 3] << 8) + data[p + 4], w = (data[p + 5] << 8) + data[p + 6];
                    return w >= 16 && w <= 1280 && h >= 16 && h <= 720 && data[data.Length - 2] == 255 && data[data.Length - 1] == 217;
                }
                p += length;
            }
            return false;
        }
        internal static bool HostFrame(int sender, int master, bool member) { return member && sender > 0 && sender == master; }
    }
}
