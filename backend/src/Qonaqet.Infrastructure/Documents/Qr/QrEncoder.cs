using System.Text;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Documents.Qr;

/// <summary>
/// ISO/IEC 18004 QR Model 2 encoder: yalnız byte mode və ECC M (QONAQET payload-ları qısa base64url sətirləridir).
/// Versiya (1–40) payload uzunluğuna görə avtomatik seçilir, maska 8 variant arasından cərimə qaydaları
/// (N1–N4) ilə seçilir. Xarici kitabxana yoxdur.
/// </summary>
public sealed class QrEncoder : IQrCodeEncoder
{
    public const int MinVersion = 1;
    public const int MaxVersion = 40;

    /// <summary>ECC M üçün format bitlərindəki səviyyə kodu (L=01, M=00, Q=11, H=10).</summary>
    private const int EccLevelBits = 0;

    /// <summary>ECC M: (blok başına EC kod sözləri, qrup 1 blok sayı, qrup 1 data kod sözləri, qrup 2 blok sayı, qrup 2 data kod sözləri).</summary>
    private static readonly (int Ec, int Blocks1, int Data1, int Blocks2, int Data2)[] EccM =
    [
        default,
        (10, 1, 16, 0, 0), (16, 1, 28, 0, 0), (26, 1, 44, 0, 0), (18, 2, 32, 0, 0), (24, 2, 43, 0, 0),
        (16, 4, 27, 0, 0), (18, 4, 31, 0, 0), (22, 2, 38, 2, 39), (22, 3, 36, 2, 37), (26, 4, 43, 1, 44),
        (30, 1, 50, 4, 51), (22, 6, 36, 2, 37), (22, 8, 37, 1, 38), (24, 4, 40, 5, 41), (24, 5, 41, 5, 42),
        (28, 7, 45, 3, 46), (28, 10, 46, 1, 47), (26, 9, 43, 4, 44), (26, 3, 44, 11, 45), (26, 3, 41, 13, 42),
        (26, 17, 42, 0, 0), (28, 17, 46, 0, 0), (28, 4, 47, 14, 48), (28, 6, 45, 14, 46), (28, 8, 47, 13, 48),
        (28, 19, 46, 4, 47), (28, 22, 45, 3, 46), (28, 3, 45, 23, 46), (28, 21, 45, 7, 46), (28, 19, 47, 10, 48),
        (28, 2, 46, 29, 47), (28, 10, 46, 23, 47), (28, 14, 46, 21, 47), (28, 14, 46, 23, 47), (28, 12, 47, 26, 48),
        (28, 6, 47, 34, 48), (28, 29, 46, 14, 47), (28, 13, 46, 32, 47), (28, 40, 47, 7, 48), (28, 18, 47, 31, 48),
    ];

    public QrMatrix Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Encode(Encoding.UTF8.GetBytes(text));
    }

    public static QrMatrix Encode(ReadOnlySpan<byte> payload)
    {
        var version = ChooseVersion(payload.Length);
        var codewords = AddEccAndInterleave(EncodeData(payload, version), version);

        var builder = new MatrixBuilder(version);
        builder.DrawFunctionPatterns();
        builder.DrawCodewords(codewords);

        var bestMask = 0;
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            builder.ApplyMask(mask);
            builder.DrawFormatBits(mask);
            var penalty = builder.Penalty();
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
            }

            builder.ApplyMask(mask); // XOR geri qaytarır
        }

        builder.ApplyMask(bestMask);
        builder.DrawFormatBits(bestMask);
        return builder.ToMatrix();
    }

    /// <summary>Verilən versiyada byte mode-da yerləşən maksimum bayt sayı (ECC M).</summary>
    public static int ByteCapacity(int version)
    {
        var bits = DataCodewords(version) * 8 - 4 - CharCountBits(version);
        return bits / 8;
    }

    public static int DataCodewords(int version)
    {
        var e = EccM[CheckVersion(version)];
        return e.Blocks1 * e.Data1 + e.Blocks2 * e.Data2;
    }

    /// <summary>Funksiya nümunələrindən sonra qalan data modulları (bit sayı) — standartdakı formula.</summary>
    public static int RawDataModules(int version)
    {
        CheckVersion(version);
        var result = ((16 * version) + 128) * version + 64;
        if (version >= 2)
        {
            var numAlign = (version / 7) + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (version >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    /// <summary>Hizalama nümunələrinin mərkəz koordinatları (hər iki ox üçün eyni).</summary>
    public static int[] AlignmentPatternPositions(int version)
    {
        CheckVersion(version);
        if (version == 1)
        {
            return [];
        }

        var numAlign = (version / 7) + 2;
        var step = version == 32 ? 26 : ((version * 4) + (numAlign * 2) + 1) / ((numAlign * 2) - 2) * 2;
        var result = new int[numAlign];
        result[0] = 6;
        for (int i = numAlign - 1, pos = (version * 4) + 17 - 7; i >= 1; i--, pos -= step)
        {
            result[i] = pos;
        }

        return result;
    }

    /// <summary>15 bitlik format məlumatı (BCH(15,5) + 0x5412 maskası).</summary>
    public static int FormatBits(int mask)
    {
        var data = (EccLevelBits << 3) | mask;
        var rem = data;
        for (var i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }

        return ((data << 10) | rem) ^ 0x5412;
    }

    /// <summary>18 bitlik versiya məlumatı (BCH(18,6)), versiya 7+ üçün.</summary>
    public static int VersionBits(int version)
    {
        var rem = version;
        for (var i = 0; i < 12; i++)
        {
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        }

        return (version << 12) | rem;
    }

    /// <summary>Reed–Solomon generator polinomu (yuxarı dərəcə əmsalı 1 buraxılıb), GF(2^8) / 0x11D.</summary>
    public static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        byte root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = GfMultiply(result[j], root);
                if (j + 1 < result.Length)
                {
                    result[j] ^= result[j + 1];
                }
            }

            root = GfMultiply(root, 0x02);
        }

        return result;
    }

    /// <summary>Data kod sözləri üçün EC kod sözlərinin hesablanması (polinom bölməsinin qalığı).</summary>
    public static byte[] ReedSolomonRemainder(ReadOnlySpan<byte> data, ReadOnlySpan<byte> divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] ^= GfMultiply(divisor[i], factor);
            }
        }

        return result;
    }

    public static byte GfMultiply(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }

    private static int CharCountBits(int version) => version <= 9 ? 8 : 16;

    private static int CheckVersion(int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, MinVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(version, MaxVersion);
        return version;
    }

    private static int ChooseVersion(int length)
    {
        for (var version = MinVersion; version <= MaxVersion; version++)
        {
            if (length <= ByteCapacity(version))
            {
                return version;
            }
        }

        throw new ArgumentException($"Payload QR kodu üçün çox uzundur ({length} bayt, maksimum {ByteCapacity(MaxVersion)}).", nameof(length));
    }

    private static byte[] EncodeData(ReadOnlySpan<byte> payload, int version)
    {
        var capacityBits = DataCodewords(version) * 8;
        var bits = new BitBuffer(capacityBits);
        bits.Append(0b0100, 4); // byte mode
        bits.Append(payload.Length, CharCountBits(version));
        foreach (var b in payload)
        {
            bits.Append(b, 8);
        }

        bits.Append(0, Math.Min(4, capacityBits - bits.Length)); // terminator
        bits.Append(0, (8 - (bits.Length % 8)) % 8);
        for (var pad = 0xEC; bits.Length < capacityBits; pad ^= 0xEC ^ 0x11)
        {
            bits.Append(pad, 8);
        }

        return bits.ToBytes();
    }

    private static byte[] AddEccAndInterleave(byte[] data, int version)
    {
        var e = EccM[version];
        var divisor = ReedSolomonDivisor(e.Ec);
        var blocks = new List<(byte[] Data, byte[] Ecc)>();
        var offset = 0;
        foreach (var (count, size) in new[] { (e.Blocks1, e.Data1), (e.Blocks2, e.Data2) })
        {
            for (var i = 0; i < count; i++)
            {
                var block = data.AsSpan(offset, size).ToArray();
                offset += size;
                blocks.Add((block, ReedSolomonRemainder(block, divisor)));
            }
        }

        var result = new List<byte>(RawDataModules(version) / 8);
        var maxData = blocks.Max(b => b.Data.Length);
        for (var i = 0; i < maxData; i++)
        {
            result.AddRange(blocks.Where(b => i < b.Data.Length).Select(b => b.Data[i]));
        }

        for (var i = 0; i < e.Ec; i++)
        {
            result.AddRange(blocks.Select(b => b.Ecc[i]));
        }

        return [.. result];
    }

    private sealed class BitBuffer(int capacity)
    {
        private readonly bool[] _bits = new bool[capacity];

        public int Length { get; private set; }

        public void Append(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                _bits[Length++] = ((value >> i) & 1) == 1;
            }
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[Length / 8];
            for (var i = 0; i < Length; i++)
            {
                if (_bits[i])
                {
                    bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
                }
            }

            return bytes;
        }
    }

    private sealed class MatrixBuilder
    {
        private readonly int _version;
        private readonly int _size;
        private readonly bool[,] _modules;
        private readonly bool[,] _isFunction;

        public MatrixBuilder(int version)
        {
            _version = version;
            _size = (version * 4) + 17;
            _modules = new bool[_size, _size];
            _isFunction = new bool[_size, _size];
        }

        public void DrawFunctionPatterns()
        {
            for (var i = 0; i < _size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }

            DrawFinder(3, 3);
            DrawFinder(_size - 4, 3);
            DrawFinder(3, _size - 4);

            var positions = AlignmentPatternPositions(_version);
            var last = positions.Length - 1;
            for (var i = 0; i < positions.Length; i++)
            {
                for (var j = 0; j < positions.Length; j++)
                {
                    var overlapsFinder = (i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0);
                    if (!overlapsFinder)
                    {
                        DrawAlignment(positions[i], positions[j]);
                    }
                }
            }

            DrawFormatBits(0); // yeri rezerv edilir, sonra əsl maska ilə yenidən yazılır
            DrawVersion();
        }

        public void DrawFormatBits(int mask)
        {
            var bits = FormatBits(mask);

            for (var i = 0; i <= 5; i++)
            {
                SetFunction(8, i, Bit(bits, i));
            }

            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (var i = 9; i < 15; i++)
            {
                SetFunction(14 - i, 8, Bit(bits, i));
            }

            for (var i = 0; i < 8; i++)
            {
                SetFunction(_size - 1 - i, 8, Bit(bits, i));
            }

            for (var i = 8; i < 15; i++)
            {
                SetFunction(8, _size - 15 + i, Bit(bits, i));
            }

            SetFunction(8, _size - 8, true); // qara modul
        }

        public void DrawCodewords(byte[] codewords)
        {
            var i = 0;
            var totalBits = codewords.Length * 8;
            for (var right = _size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;
                }

                for (var vert = 0; vert < _size; vert++)
                {
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var upward = ((right + 1) & 2) == 0;
                        var y = upward ? _size - 1 - vert : vert;
                        if (!_isFunction[y, x] && i < totalBits)
                        {
                            _modules[y, x] = ((codewords[i >> 3] >> (7 - (i & 7))) & 1) == 1;
                            i++;
                        }

                        // Qalan modullar (remainder bits) ağ qalır.
                    }
                }
            }
        }

        public void ApplyMask(int mask)
        {
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    if (!_isFunction[y, x] && MaskCondition(mask, x, y))
                    {
                        _modules[y, x] = !_modules[y, x];
                    }
                }
            }
        }

        public static bool MaskCondition(int mask, int x, int y) => mask switch
        {
            0 => (x + y) % 2 == 0,
            1 => y % 2 == 0,
            2 => x % 3 == 0,
            3 => (x + y) % 3 == 0,
            4 => ((x / 3) + (y / 2)) % 2 == 0,
            5 => ((x * y % 2) + (x * y % 3)) == 0,
            6 => ((x * y % 2) + (x * y % 3)) % 2 == 0,
            7 => (((x + y) % 2) + (x * y % 3)) % 2 == 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mask)),
        };

        /// <summary>ISO/IEC 18004 §7.8.3 cərimə qaydaları.</summary>
        public int Penalty()
        {
            var penalty = 0;

            // N1: eyni rəngli 5+ ardıcıl modul; N3: 1:1:3:1:1 nümunəsi 4 ağ modulla.
            for (var a = 0; a < _size; a++)
            {
                penalty += LinePenalty(i => _modules[a, i]) + LinePenalty(i => _modules[i, a]);
            }

            // N2: 2×2 eyni rəngli bloklar.
            for (var y = 0; y < _size - 1; y++)
            {
                for (var x = 0; x < _size - 1; x++)
                {
                    var c = _modules[y, x];
                    if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1])
                    {
                        penalty += 3;
                    }
                }
            }

            // N4: qara modulların 50%-dən hər 5% kənarlaşması üçün 10.
            var dark = 0;
            foreach (var m in _modules)
            {
                if (m)
                {
                    dark++;
                }
            }

            var total = _size * _size;
            var k = ((Math.Abs((dark * 20) - (total * 10)) + total - 1) / total) - 1;
            penalty += Math.Max(0, k) * 10;
            return penalty;
        }

        public QrMatrix ToMatrix()
        {
            var rows = new string[_size];
            for (var y = 0; y < _size; y++)
            {
                var sb = new StringBuilder(_size);
                for (var x = 0; x < _size; x++)
                {
                    sb.Append(_modules[y, x] ? '1' : '0');
                }

                rows[y] = sb.ToString();
            }

            return new QrMatrix(_version, _size, rows);
        }

        private int LinePenalty(Func<int, bool> module)
        {
            var penalty = 0;
            var run = 1;
            for (var i = 1; i <= _size; i++)
            {
                if (i < _size && module(i) == module(i - 1))
                {
                    run++;
                    continue;
                }

                if (run >= 5)
                {
                    penalty += 3 + (run - 5);
                }

                run = 1;
            }

            // Sərhəd xaricini ağ sayırıq (quiet zone).
            bool At(int i) => i >= 0 && i < _size && module(i);
            for (var i = -4; i < _size; i++)
            {
                var core = At(i) && !At(i + 1) && At(i + 2) && At(i + 3) && At(i + 4) && !At(i + 5) && At(i + 6);
                if (!core)
                {
                    continue;
                }

                var whiteBefore = !At(i - 1) && !At(i - 2) && !At(i - 3) && !At(i - 4);
                var whiteAfter = !At(i + 7) && !At(i + 8) && !At(i + 9) && !At(i + 10);
                if (whiteBefore || whiteAfter)
                {
                    penalty += 40;
                }
            }

            return penalty;
        }

        private void DrawFinder(int cx, int cy)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    var x = cx + dx;
                    var y = cy + dy;
                    if (x >= 0 && x < _size && y >= 0 && y < _size)
                    {
                        SetFunction(x, y, dist != 2 && dist != 4);
                    }
                }
            }
        }

        private void DrawAlignment(int cx, int cy)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    SetFunction(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private void DrawVersion()
        {
            if (_version < 7)
            {
                return;
            }

            var bits = VersionBits(_version);
            for (var i = 0; i < 18; i++)
            {
                var bit = Bit(bits, i);
                var a = _size - 11 + (i % 3);
                var b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private void SetFunction(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _isFunction[y, x] = true;
        }

        private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;
    }
}
