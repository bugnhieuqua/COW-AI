using System.Buffers.Binary;
using System.Text;

namespace COW_AI.Services;

/// <summary>
/// Triển khai thuật toán sinh token Sec-MS-GEC v2 của Edge TTS
/// (CS64 + SHA-256 biến thể dùng cho DRM của Microsoft Edge Read Aloud).
/// </summary>
internal static class EdgeTtsDrm
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    private static readonly uint[] Sha256Initial =
    {
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
        0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
    };

    private static readonly uint[] Sha256State =
    {
        0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A,
        0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2
    };

    private static readonly uint[] Sha256Schedule =
    {
        0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA, 0xA622EE8C, 0xD1DD9491, 0xC0DD25A0,
        0xF9E70AB6, 0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33, 0xD258B060, 0x72766564
    };

    private static readonly uint[] Sha256RoundConstants =
    {
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2
    };

    private const uint Sha256Adjustment = 0xC9DC06A7;
    private const uint Sigma0Adjustment = 0x059C0B80;
    private const uint Sigma1Adjustment = 0x000B92C0;

    private static readonly uint[] Sha256BlockConstants = BuildBlockConstants();

    private const uint Cs64Factor = 0x82893933;
    private const uint Cs64Adjustment0 = 0xF2F90B1A;
    private const uint Cs64Adjustment1 = 0x0B1A0000;

    private static readonly (uint Coefficient, uint Offset)[] Cs64Coefficients =
    {
        (0xE82E91B1, 0xB32700D0),
        (0x44DE24C6, 0x88DFF51D),
        (0x3BEB65BC, 0x29514038),
        (0xC89C5933, 0xB328CBDF),
        (0x8D1EC211, 0xD405B4D5),
        (0x314EF5D5, 0x26E55F3C),
        (0xE92491B1, 0x607801C6),
        (0xF0A5B9D5, 0x82B2C1FB),
        (0x06064CF3, 0xFA51A3DA),
        (0xDA79FA60, 0x207CC464),
        (0xBED20560, 0x3C3C74F3),
        (0x89F9C9F8, 0xD2D14B61),
        (0x35FA38C6, 0xBB04A2C1),
        (0xCB49E3C6, 0xC5626167)
    };

    /// <summary>
    /// Sinh token Sec-MS-GEC v2: "&lt;CS64 16 hex&gt;-&lt;SHA256 64 hex&gt;".
    /// </summary>
    public static string GenerateSecMsGec(DateTimeOffset? now = null)
    {
        var unixSeconds = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var ticks = unixSeconds + 11644473600L;
        ticks -= ticks % 300;
        ticks *= 10_000_000L;

        var data = Encoding.ASCII.GetBytes($"{ticks}{TrustedClientToken}");
        var digest = TokenHash(data);

        return $"{ComputeCs64Hex(data, digest)}-{Convert.ToHexString(digest)}";
    }

    private static uint[] BuildBlockConstants()
    {
        var constants = (uint[])Sha256RoundConstants.Clone();
        constants[14] = Sha256Adjustment;
        return constants;
    }

    private static byte[] TokenHash(byte[] data)
    {
        if (data.Length < 7)
        {
            throw new ArgumentException("Token salt must contain at least seven bytes", nameof(data));
        }

        var length = 57 + data.Length;
        var tailLength = data.Length - 7;
        var zeroCount = (55 - length) % 64;
        if (zeroCount < 0)
        {
            zeroCount += 64;
        }

        var padded = new byte[tailLength + 1 + zeroCount + 8];
        Buffer.BlockCopy(data, 7, padded, 0, tailLength);
        padded[tailLength] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(padded.Length - 8), (ulong)length * 8);

        var state = Sha256Init(data.AsSpan(0, 7));
        var words = new uint[64];

        for (var offset = 0; offset < padded.Length; offset += 64)
        {
            for (var i = 0; i < 16; i++)
            {
                words[i] = BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(offset + (i * 4), 4));
            }

            ExpandSchedule(words, 16);
            var working = Sha256Rounds(state, words, Sha256RoundConstants, 0);

            for (var i = 0; i < 8; i++)
            {
                state[i] = unchecked(state[i] + working[i]);
            }
        }

        var digest = new byte[32];
        for (var i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i * 4, 4), state[i]);
        }

        return digest;
    }

    private static uint[] Sha256Init(ReadOnlySpan<byte> first7)
    {
        var words = new uint[64];
        words[14] = (uint)((first7[0] << 16) | (first7[1] << 8) | first7[2]);
        words[15] = (uint)((first7[3] << 24) | (first7[4] << 16) | (first7[5] << 8) | first7[6]);

        for (var index = 16; index < 30; index++)
        {
            var value = Sha256Schedule[index - 16];

            if (index - 2 >= 14)
            {
                var sigma = Sigma1(words[index - 2]);
                value = unchecked(value + (index == 16 ? sigma ^ Sigma1Adjustment : sigma));
            }

            if (index - 7 >= 14)
            {
                value = unchecked(value + words[index - 7]);
            }

            if (index - 15 >= 14)
            {
                var sigma = Sigma0(words[index - 15]);
                value = unchecked(value + (index == 29 ? sigma ^ Sigma0Adjustment : sigma));
            }

            words[index] = value;
        }

        words[30] = unchecked(
            Sigma1(words[28]) + words[23] + Sigma0(words[15]) + words[14] +
            Sha256Adjustment - Sha256RoundConstants[14]);

        ExpandSchedule(words, 31);

        var working = Sha256Rounds(Sha256State, words, Sha256BlockConstants, 14);
        var result = new uint[8];
        for (var i = 0; i < 8; i++)
        {
            result[i] = unchecked(Sha256Initial[i] + working[i]);
        }

        return result;
    }

    private static uint[] Sha256Rounds(uint[] state, uint[] words, uint[] constants, int firstRound)
    {
        unchecked
        {
            uint a = state[0], b = state[1], c = state[2], d = state[3];
            uint e = state[4], f = state[5], g = state[6], h = state[7];

            for (var i = firstRound; i < 64; i++)
            {
                var s1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                var ch = (e & f) ^ (~e & g);
                var temp1 = h + s1 + ch + constants[i] + words[i];

                var s0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                var maj = (a & b) ^ (a & c) ^ (b & c);
                var temp2 = s0 + maj;

                h = g;
                g = f;
                f = e;
                e = d + temp1;
                d = c;
                c = b;
                b = a;
                a = temp1 + temp2;
            }

            return new[] { a, b, c, d, e, f, g, h };
        }
    }

    private static void ExpandSchedule(uint[] words, int firstWord)
    {
        unchecked
        {
            for (var i = firstWord; i < 64; i++)
            {
                var x = words[i - 2];
                var y = words[i - 15];
                words[i] = Sigma1(x) + words[i - 7] + Sigma0(y) + words[i - 16];
            }
        }
    }

    private static uint Sigma0(uint value)
    {
        unchecked
        {
            return RotateRight(value, 7) ^ RotateRight(value, 18) ^ (value >> 3);
        }
    }

    private static uint Sigma1(uint value)
    {
        unchecked
        {
            return RotateRight(value, 17) ^ RotateRight(value, 19) ^ (value >> 10);
        }
    }

    private static uint RotateRight(uint value, int bits) => (value >> bits) | (value << (32 - bits));

    private static string ComputeCs64Hex(byte[] data, byte[] digest)
    {
        var (swapLast, swapAcc) = ComputeCs64(data, digest);

        var output = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), swapLast);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), swapAcc);
        return Convert.ToHexString(output);
    }

    private static (uint SwapLast, uint SwapAcc) ComputeCs64(byte[] data, byte[] digest)
    {
        unchecked
        {
            const uint mask = 0xFFFFFFFF;

            var keyA = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(0, 4)) | 1u;
            var keyB = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(4, 4)) | 1u;

            var factors = new[] { (keyA * Cs64Factor) & mask, (keyB * Cs64Factor) & mask };

            var terms = new List<uint>(Cs64Coefficients.Length + 1);
            var offsets = new List<uint>(Cs64Coefficients.Length + 1);

            for (var i = 0; i < Cs64Coefficients.Length; i++)
            {
                terms.Add((Cs64Coefficients[i].Coefficient * factors[i % 2]) & mask);
                offsets.Add(Cs64Coefficients[i].Offset);
            }

            var words = ToLittleEndianWordsWithLeadingZero(data);
            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var term = word * (i % 2 == 0 ? keyA : keyB);
                var offset = RotateLeft16(word) * (i % 2 == 0 ? 0xEF0569FBu : 0xC31713DBu);

                if (i == 0)
                {
                    term += Cs64Adjustment0 * factors[0];
                    offset += Cs64Adjustment1;
                }

                terms.Add(term & mask);
                offsets.Add(offset & mask);
            }

            uint swapAcc = 0;
            uint swapLast = 0;
            uint state = 0;
            uint acc = 0;

            for (var i = 0; i < terms.Count; i += 2)
            {
                var x = terms[i];
                var y = terms[i + 1];

                var t = RotateLeft16((offsets[i] + x) & mask);
                var q = (t * 0x76F50F34u) & mask;
                swapAcc += (RotateLeft16(q) * 0xC3EFEA97u + q) & mask;

                t = RotateLeft16((offsets[i + 1] + y) & mask);
                q = (t * 0x1881CE3Cu) & mask;
                swapLast = (RotateLeft16(q) * 0x35BD1EC9u + q) & mask;
                swapAcc = (swapAcc + swapLast) & mask;

                state = (state * keyA + x) & mask;
                state = (RotateLeft16(state) * 0xCF98B111u) & mask;
                state = (RotateLeft16(state) * 0x87085B9Fu) & mask;
                state = (RotateLeft16(state) * 0x12CEB96Du) & mask;
                state = (RotateLeft16(state) * 0x257E1D83u) & mask;
                acc += state;

                t = (state * keyB + y) & mask;
                t = (RotateLeft16(t) * 0xA27416F5u) & mask;
                t = (RotateLeft16(t) * 0xD38396FFu) & mask;
                state = (RotateLeft16((RotateLeft16(t) * 0x7C932B89u) & mask) * 0xBFA49F69u) & mask;
                acc = (acc + state) & mask;
            }

            return (swapLast ^ state, swapAcc ^ acc);
        }
    }

    private static List<uint> ToLittleEndianWordsWithLeadingZero(byte[] data)
    {
        var padded = new byte[data.Length + 1];
        Buffer.BlockCopy(data, 0, padded, 1, data.Length);

        var remainder = padded.Length % 4;
        if (remainder != 0)
        {
            Array.Resize(ref padded, padded.Length + (4 - remainder));
        }

        var words = new List<uint>(padded.Length / 4);
        for (var i = 0; i < padded.Length; i += 4)
        {
            words.Add(BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(i, 4)));
        }

        if (words.Count % 2 != 0)
        {
            words.Add(0);
        }

        return words;
    }

    private static uint RotateLeft16(uint value)
    {
        unchecked
        {
            return (value << 16) | (value >> 16);
        }
    }
}
