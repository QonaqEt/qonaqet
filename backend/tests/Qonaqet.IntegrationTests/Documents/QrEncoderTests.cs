using System.Text;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Documents.Qr;

namespace Qonaqet.IntegrationTests.Documents;

/// <summary>
/// QR encoder: standartın referens vektorları (RS, format/versiya bitləri, hizalama, tutum) və
/// müstəqil yazılmış dekoder ilə uçdan-uca yoxlama.
/// </summary>
public sealed class QrEncoderTests
{
    [Fact]
    public void Reed_solomon_generator_for_7_codewords_matches_the_standard()
    {
        // Standart cədvəl əmsalları α-nın üstləri kimi verir: x⁷ + α⁸⁷x⁶ + α²²⁹x⁵ + α¹⁴⁶x⁴ + α¹⁴⁹x³ + α²³⁸x² + α¹⁰²x + α²¹.
        static byte AlphaPow(int exponent)
        {
            byte value = 1;
            for (var i = 0; i < exponent; i++)
            {
                value = QrEncoder.GfMultiply(value, 2);
            }

            return value;
        }

        var expected = new[] { 87, 229, 146, 149, 238, 102, 21 }.Select(AlphaPow).ToArray();

        Assert.Equal(expected, QrEncoder.ReedSolomonDivisor(7));
    }

    [Fact]
    public void Reed_solomon_matches_iso_annex_example_01234567_1M()
    {
        byte[] data = [16, 32, 12, 86, 97, 128, 236, 17, 236, 17, 236, 17, 236, 17, 236, 17];

        var ecc = QrEncoder.ReedSolomonRemainder(data, QrEncoder.ReedSolomonDivisor(10));

        Assert.Equal(new byte[] { 165, 36, 212, 193, 237, 54, 199, 135, 44, 85 }, ecc);
    }

    [Fact]
    public void Reed_solomon_matches_hello_world_1Q_example()
    {
        byte[] data = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236];

        var ecc = QrEncoder.ReedSolomonRemainder(data, QrEncoder.ReedSolomonDivisor(13));

        Assert.Equal(new byte[] { 168, 72, 22, 82, 217, 54, 156, 0, 46, 15, 180, 122, 16 }, ecc);
    }

    [Theory]
    [InlineData(0, 0b101010000010010)]
    [InlineData(1, 0b101000100100101)]
    [InlineData(2, 0b101111001111100)]
    [InlineData(3, 0b101101101001011)]
    [InlineData(4, 0b100010111111001)]
    [InlineData(5, 0b100000011001110)]
    [InlineData(6, 0b100111110010111)]
    [InlineData(7, 0b100101010100000)]
    public void Format_bits_for_ecc_m_match_the_standard_table(int mask, int expected)
    {
        Assert.Equal(expected, QrEncoder.FormatBits(mask));
    }

    [Theory]
    [InlineData(7, 0b000111110010010100)]
    [InlineData(8, 0b001000010110111100)]
    [InlineData(40, 0b101000110001101001)]
    public void Version_bits_match_the_standard_table(int version, int expected)
    {
        Assert.Equal(expected, QrEncoder.VersionBits(version));
    }

    [Theory]
    [InlineData(1, new int[0])]
    [InlineData(2, new[] { 6, 18 })]
    [InlineData(7, new[] { 6, 22, 38 })]
    [InlineData(14, new[] { 6, 26, 46, 66 })]
    [InlineData(32, new[] { 6, 34, 60, 86, 112, 138 })]
    [InlineData(40, new[] { 6, 30, 58, 86, 114, 142, 170 })]
    public void Alignment_positions_match_the_standard_table(int version, int[] expected)
    {
        Assert.Equal(expected, QrEncoder.AlignmentPatternPositions(version));
    }

    [Theory]
    [InlineData(1, 14)]
    [InlineData(2, 26)]
    [InlineData(3, 42)]
    [InlineData(4, 62)]
    [InlineData(5, 84)]
    [InlineData(9, 180)]
    [InlineData(10, 213)]
    [InlineData(40, 2331)]
    public void Byte_capacity_for_ecc_m_matches_the_standard(int version, int capacity)
    {
        Assert.Equal(capacity, QrEncoder.ByteCapacity(version));
    }

    [Fact]
    public void Ecc_table_is_consistent_with_module_count_for_all_versions()
    {
        for (var version = 1; version <= 40; version++)
        {
            var total = QrEncoder.RawDataModules(version) / 8;
            var data = QrEncoder.DataCodewords(version);
            var ecPerBlock = ReedSolomonBlocks(version).Ec;
            var blocks = ReedSolomonBlocks(version).Blocks;

            Assert.True(data + (ecPerBlock * blocks) == total, $"v{version}: {data} + {ecPerBlock}×{blocks} ≠ {total}");
        }
    }

    [Theory]
    [InlineData(14, 1)]
    [InlineData(15, 2)]
    [InlineData(122, 7)]
    [InlineData(213, 10)]
    [InlineData(2331, 40)]
    public void Version_is_chosen_automatically(int length, int version)
    {
        var matrix = QrEncoder.Encode(new byte[length]);

        Assert.Equal(version, matrix.Version);
        Assert.Equal((version * 4) + 17, matrix.Size);
        Assert.All(matrix.Rows, r => Assert.Equal(matrix.Size, r.Length));
    }

    [Fact]
    public void Too_long_payload_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => QrEncoder.Encode(new byte[2332]));
    }

    [Theory]
    [InlineData("g.0194c7a2-5f1e-7c3a-9b2d-8e4f6a1b2c3d.k3J9xQ")]
    [InlineData("A")]
    [InlineData("https://qonaqet.az/g/Zk3J9xQpL2mN8vR4tY6wB1cD5fG7hJ0kE3sA9uI2oP4")]
    [InlineData("Qonaq et, xoşbəxt et — ə ğ ı ö ü ç ş")]
    public void Encoded_matrix_decodes_back_to_the_payload(string text)
    {
        var matrix = new QrEncoder().Encode(text);

        Assert.Equal(text, Encoding.UTF8.GetString(TestDecoder.Decode(matrix)));
    }

    [Theory]
    [InlineData(100)] // v6
    [InlineData(150)] // v8, iki blok qrupu
    [InlineData(500)] // v16, versiya məlumatı
    [InlineData(2331)] // v40
    public void Large_payloads_decode_back(int length)
    {
        var payload = Enumerable.Range(0, length).Select(i => (byte)((i * 37) + 11)).ToArray();

        var matrix = QrEncoder.Encode(payload);

        Assert.Equal(payload, TestDecoder.Decode(matrix));
    }

    [Fact]
    public void Function_patterns_are_in_place()
    {
        var m = new QrEncoder().Encode("qonaqet");

        foreach (var (cx, cy) in new[] { (3, 3), (m.Size - 4, 3), (3, m.Size - 4) })
        {
            Assert.True(m.IsDark(cx, cy));
            Assert.False(m.IsDark(cx + 2, cy));
            Assert.True(m.IsDark(cx + 3, cy));
        }

        for (var i = 8; i < m.Size - 8; i++)
        {
            Assert.Equal(i % 2 == 0, m.IsDark(i, 6));
            Assert.Equal(i % 2 == 0, m.IsDark(6, i));
        }

        Assert.True(m.IsDark(8, m.Size - 8)); // qara modul
    }

    [Fact]
    public void Encoding_is_deterministic()
    {
        var encoder = new QrEncoder();

        Assert.Equal(encoder.Encode("c.123.abc").Rows, encoder.Encode("c.123.abc").Rows);
    }

    private static (int Ec, int Blocks) ReedSolomonBlocks(int version)
    {
        // Müstəqil yoxlama üçün EC/blok sayı: standart cədvəlin ECC M sütunu.
        int[] ec = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28];
        int[] blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49];
        return (ec[version], blocks[version]);
    }

    /// <summary>Encoder-dən asılı olmayan minimal dekoder (yalnız byte mode, ECC M).</summary>
    private static class TestDecoder
    {
        public static byte[] Decode(QrMatrix m)
        {
            var size = m.Size;
            var version = (size - 17) / 4;
            var function = FunctionMap(version, size);

            var formatA = 0;
            for (var i = 0; i <= 5; i++) formatA |= Bit(m, 8, i) << i;
            formatA |= Bit(m, 8, 7) << 6;
            formatA |= Bit(m, 8, 8) << 7;
            formatA |= Bit(m, 7, 8) << 8;
            for (var i = 9; i < 15; i++) formatA |= Bit(m, 14 - i, 8) << i;

            var formatB = 0;
            for (var i = 0; i < 8; i++) formatB |= Bit(m, size - 1 - i, 8) << i;
            for (var i = 8; i < 15; i++) formatB |= Bit(m, 8, size - 15 + i) << i;

            Assert.Equal(formatA, formatB);
            var mask = Enumerable.Range(0, 8).Single(k => QrEncoder.FormatBits(k) == formatA);

            var bits = new List<int>();
            for (var right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (var vert = 0; vert < size; vert++)
                {
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var y = ((right + 1) & 2) == 0 ? size - 1 - vert : vert;
                        if (!function[y, x])
                        {
                            bits.Add(Bit(m, x, y) ^ (MaskBit(mask, x, y) ? 1 : 0));
                        }
                    }
                }
            }

            var codewords = Enumerable.Range(0, bits.Count / 8).Select(i => (byte)Enumerable.Range(0, 8).Aggregate(0, (acc, b) => (acc << 1) | bits[(i * 8) + b])).ToArray();

            var (ec, blockCount) = ReedSolomonBlocks(version);
            var totalData = QrEncoder.DataCodewords(version);
            var shortLen = totalData / blockCount;
            var longBlocks = totalData % blockCount;
            var lengths = Enumerable.Range(0, blockCount).Select(b => b < blockCount - longBlocks ? shortLen : shortLen + 1).ToArray();

            var blocks = lengths.Select(l => new List<byte>()).ToArray();
            var k = 0;
            for (var i = 0; i < lengths.Max(); i++)
            {
                for (var b = 0; b < blockCount; b++)
                {
                    if (i < lengths[b]) blocks[b].Add(codewords[k++]);
                }
            }

            var eccs = lengths.Select(_ => new List<byte>()).ToArray();
            for (var i = 0; i < ec; i++)
            {
                for (var b = 0; b < blockCount; b++) eccs[b].Add(codewords[k++]);
            }

            var divisor = QrEncoder.ReedSolomonDivisor(ec);
            for (var b = 0; b < blockCount; b++)
            {
                Assert.Equal(QrEncoder.ReedSolomonRemainder(blocks[b].ToArray(), divisor), eccs[b].ToArray());
            }

            var data = blocks.SelectMany(b => b).ToArray();
            var reader = new BitReader(data);
            Assert.Equal(0b0100, reader.Read(4));
            var length = reader.Read(version <= 9 ? 8 : 16);
            return Enumerable.Range(0, length).Select(_ => (byte)reader.Read(8)).ToArray();
        }

        private static bool[,] FunctionMap(int version, int size)
        {
            var f = new bool[size, size];
            void Mark(int x0, int y0, int w, int h)
            {
                for (var y = y0; y < y0 + h; y++)
                    for (var x = x0; x < x0 + w; x++)
                        if (x >= 0 && y >= 0 && x < size && y < size) f[y, x] = true;
            }

            Mark(0, 0, 9, 9);              // finder + separator + format
            Mark(size - 8, 0, 8, 9);
            Mark(0, size - 8, 9, 8);
            Mark(6, 0, 1, size);           // timing
            Mark(0, 6, size, 1);
            var pos = QrEncoder.AlignmentPatternPositions(version);
            foreach (var ax in pos)
            {
                foreach (var ay in pos)
                {
                    var inFinder = (ax < 9 && ay < 9) || (ax > size - 9 && ay < 9) || (ax < 9 && ay > size - 9);
                    if (!inFinder) Mark(ax - 2, ay - 2, 5, 5);
                }
            }

            if (version >= 7)
            {
                Mark(size - 11, 0, 3, 6);
                Mark(0, size - 11, 6, 3);
            }

            return f;
        }

        private static bool MaskBit(int mask, int x, int y) => mask switch
        {
            0 => (x + y) % 2 == 0,
            1 => y % 2 == 0,
            2 => x % 3 == 0,
            3 => (x + y) % 3 == 0,
            4 => ((x / 3) + (y / 2)) % 2 == 0,
            5 => (x * y % 2) + (x * y % 3) == 0,
            6 => ((x * y % 2) + (x * y % 3)) % 2 == 0,
            _ => (((x + y) % 2) + (x * y % 3)) % 2 == 0,
        };

        private static int Bit(QrMatrix m, int x, int y) => m.IsDark(x, y) ? 1 : 0;

        private sealed class BitReader(byte[] data)
        {
            private int _pos;

            public int Read(int count)
            {
                var value = 0;
                for (var i = 0; i < count; i++, _pos++)
                {
                    value = (value << 1) | ((data[_pos >> 3] >> (7 - (_pos & 7))) & 1);
                }

                return value;
            }
        }
    }
}
