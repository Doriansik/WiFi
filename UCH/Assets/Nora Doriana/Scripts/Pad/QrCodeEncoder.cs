using System;
using System.Collections.Generic;
using System.Text;

#region QR Code Encoder
// Minimalny generator kodow QR (tryb bajtowy, korekcja bledow M, wersje 1-6) - wystarcza na adres URL hosta w sieci lokalnej.
// Bez zewnetrznych bibliotek; algorytm wg specyfikacji ISO/IEC 18004.
public static class QrCodeEncoder
{
    private const int MinVersion = 1;
    private const int MaxVersion = 6;  // od wersji 7 dochodza bloki informacji o wersji - adres URL sie w 6 zawsze miesci
    private const int FormatBitsEccM = 0;

    private static readonly int[] EccCodewordsPerBlock = { -1, 10, 16, 26, 18, 24, 16 };
    private static readonly int[] NumErrorCorrectionBlocks = { -1, 1, 1, 1, 2, 2, 4 };

    // Zwraca macierz modulow [y, x]; true = ciemny modul. Bez marginesu (quiet zone) - dodaje go wyswietlajacy.
    public static bool[,] Encode(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        int version = ChooseVersion(data.Length);
        byte[] codewords = AddEccAndInterleave(BuildDataCodewords(data, version), version);

        var qr = new QrMatrix(version);
        qr.DrawFunctionPatterns();
        qr.DrawCodewords(codewords);
        qr.ApplyBestMask();
        return qr.Modules;
    }

    private static int ChooseVersion(int byteCount)
    {
        for (int version = MinVersion; version <= MaxVersion; version++)
        {
            int capacityBits = GetNumDataCodewords(version) * 8;
            int usedBits = 4 + 8 + byteCount * 8;  // tryb + licznik znakow (8 bitow dla wersji 1-9) + dane

            if (usedBits <= capacityBits)
            {
                return version;
            }
        }

        throw new ArgumentException($"Tekst za dlugi na kod QR (max wersja {MaxVersion}): {byteCount} bajtow");
    }

    private static byte[] BuildDataCodewords(byte[] data, int version)
    {
        var bits = new List<bool>();
        AppendBits(bits, 0x4, 4);  // tryb bajtowy
        AppendBits(bits, data.Length, 8);

        foreach (byte b in data)
        {
            AppendBits(bits, b, 8);
        }

        int capacityBits = GetNumDataCodewords(version) * 8;
        AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));  // terminator
        AppendBits(bits, 0, (8 - bits.Count % 8) % 8);

        for (byte pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
        {
            AppendBits(bits, pad, 8);
        }

        var result = new byte[bits.Count / 8];

        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            }
        }

        return result;
    }

    private static void AppendBits(List<bool> bits, int value, int length)
    {
        for (int i = length - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    private static byte[] AddEccAndInterleave(byte[] data, int version)
    {
        int numBlocks = NumErrorCorrectionBlocks[version];
        int blockEccLength = EccCodewordsPerBlock[version];
        int rawCodewords = GetNumRawDataModules(version) / 8;
        int numShortBlocks = numBlocks - rawCodewords % numBlocks;
        int shortBlockLength = rawCodewords / numBlocks;

        byte[] divisor = ReedSolomonComputeDivisor(blockEccLength);
        var blocks = new List<byte[]>();

        for (int i = 0, offset = 0; i < numBlocks; i++)
        {
            int dataLength = shortBlockLength - blockEccLength + (i < numShortBlocks ? 0 : 1);
            var block = new byte[shortBlockLength + 1];
            Array.Copy(data, offset, block, 0, dataLength);
            offset += dataLength;

            byte[] ecc = ReedSolomonComputeRemainder(data, offset - dataLength, dataLength, divisor);
            // krotkie bloki maja pusty bajt na koncu danych, zeby ECC wszystkich blokow lezalo na tych samych indeksach
            Array.Copy(ecc, 0, block, shortBlockLength + 1 - blockEccLength, blockEccLength);
            blocks.Add(block);
        }

        var result = new List<byte>(rawCodewords);

        for (int i = 0; i < shortBlockLength + 1; i++)
        {
            for (int j = 0; j < blocks.Count; j++)
            {
                if (i != shortBlockLength - blockEccLength || j >= numShortBlocks)
                {
                    result.Add(blocks[j][i]);
                }
            }
        }

        return result.ToArray();
    }

    private static int GetNumRawDataModules(int version)
    {
        int result = (16 * version + 128) * version + 64;

        if (version >= 2)
        {
            int numAlign = version / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
        }

        return result;
    }

    private static int GetNumDataCodewords(int version)
    {
        return GetNumRawDataModules(version) / 8 - EccCodewordsPerBlock[version] * NumErrorCorrectionBlocks[version];
    }

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;

        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < result.Length; j++)
            {
                result[j] = ReedSolomonMultiply(result[j], root);

                if (j + 1 < result.Length)
                {
                    result[j] ^= result[j + 1];
                }
            }

            root = ReedSolomonMultiply(root, 0x02);
        }

        return result;
    }

    private static byte[] ReedSolomonComputeRemainder(byte[] data, int offset, int length, byte[] divisor)
    {
        var result = new byte[divisor.Length];

        for (int n = 0; n < length; n++)
        {
            int factor = data[offset + n] ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[result.Length - 1] = 0;

            for (int i = 0; i < result.Length; i++)
            {
                result[i] ^= ReedSolomonMultiply(divisor[i], factor);
            }
        }

        return result;
    }

    private static byte ReedSolomonMultiply(int x, int y)
    {
        int z = 0;

        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }

    #region QR Matrix
    private sealed class QrMatrix
    {
        private readonly int _version;
        private readonly int _size;
        private readonly bool[,] _isFunction;

        public bool[,] Modules { get; }

        public QrMatrix(int version)
        {
            _version = version;
            _size = version * 4 + 17;
            Modules = new bool[_size, _size];
            _isFunction = new bool[_size, _size];
        }

        public void DrawFunctionPatterns()
        {
            for (int i = 0; i < _size; i++)
            {
                SetFunctionModule(6, i, i % 2 == 0);
                SetFunctionModule(i, 6, i % 2 == 0);
            }

            DrawFinderPattern(3, 3);
            DrawFinderPattern(_size - 4, 3);
            DrawFinderPattern(3, _size - 4);

            int[] alignPositions = GetAlignmentPatternPositions();
            int numAlign = alignPositions.Length;

            for (int i = 0; i < numAlign; i++)
            {
                for (int j = 0; j < numAlign; j++)
                {
                    bool overlapsFinder = (i == 0 && j == 0) || (i == 0 && j == numAlign - 1) || (i == numAlign - 1 && j == 0);

                    if (!overlapsFinder)
                    {
                        DrawAlignmentPattern(alignPositions[i], alignPositions[j]);
                    }
                }
            }

            DrawFormatBits(0);  // rezerwuje miejsce, wlasciwa maska wpisywana pozniej
        }

        public void DrawCodewords(byte[] data)
        {
            int bitIndex = 0;

            for (int right = _size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;  // kolumna linii czasowej
                }

                for (int vert = 0; vert < _size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? _size - 1 - vert : vert;

                        if (!_isFunction[y, x] && bitIndex < data.Length * 8)
                        {
                            Modules[y, x] = ((data[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                            bitIndex++;
                        }
                    }
                }
            }
        }

        public void ApplyBestMask()
        {
            int bestMask = 0;
            int minPenalty = int.MaxValue;

            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                int penalty = GetPenaltyScore();

                if (penalty < minPenalty)
                {
                    bestMask = mask;
                    minPenalty = penalty;
                }

                ApplyMask(mask);  // XOR - drugie nalozenie cofa maske
            }

            ApplyMask(bestMask);
            DrawFormatBits(bestMask);
        }

        private void SetFunctionModule(int x, int y, bool isDark)
        {
            Modules[y, x] = isDark;
            _isFunction[y, x] = true;
        }

        private void DrawFinderPattern(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx;
                    int yy = y + dy;

                    if (xx >= 0 && xx < _size && yy >= 0 && yy < _size)
                    {
                        SetFunctionModule(xx, yy, distance != 2 && distance != 4);
                    }
                }
            }
        }

        private void DrawAlignmentPattern(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private int[] GetAlignmentPatternPositions()
        {
            if (_version == 1)
            {
                return new int[0];
            }

            int numAlign = _version / 7 + 2;
            int step = (_version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
            var result = new int[numAlign];
            result[0] = 6;

            for (int i = numAlign - 1, position = _size - 7; i >= 1; i--, position -= step)
            {
                result[i] = position;
            }

            return result;
        }

        private void DrawFormatBits(int mask)
        {
            int data = FormatBitsEccM << 3 | mask;
            int remainder = data;

            for (int i = 0; i < 10; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            }

            int bits = (data << 10 | remainder) ^ 0x5412;

            for (int i = 0; i <= 5; i++)
            {
                SetFunctionModule(8, i, GetBit(bits, i));
            }

            SetFunctionModule(8, 7, GetBit(bits, 6));
            SetFunctionModule(8, 8, GetBit(bits, 7));
            SetFunctionModule(7, 8, GetBit(bits, 8));

            for (int i = 9; i < 15; i++)
            {
                SetFunctionModule(14 - i, 8, GetBit(bits, i));
            }

            for (int i = 0; i < 8; i++)
            {
                SetFunctionModule(_size - 1 - i, 8, GetBit(bits, i));
            }

            for (int i = 8; i < 15; i++)
            {
                SetFunctionModule(8, _size - 15 + i, GetBit(bits, i));
            }

            SetFunctionModule(8, _size - 8, true);  // zawsze ciemny modul
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < _size; y++)
            {
                for (int x = 0; x < _size; x++)
                {
                    if (!_isFunction[y, x] && IsMasked(mask, x, y))
                    {
                        Modules[y, x] = !Modules[y, x];
                    }
                }
            }
        }

        private static bool IsMasked(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return x * y % 2 + x * y % 3 == 0;
                case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
            }
        }

        // Uproszczona punktacja (reguly 1, 2 i 4 ze specyfikacji) - wybor maski wplywa tylko na czytelnosc, nie na poprawnosc.
        private int GetPenaltyScore()
        {
            int result = 0;

            for (int y = 0; y < _size; y++)
            {
                result += GetRunPenalty(x => Modules[y, x]);
            }

            for (int x = 0; x < _size; x++)
            {
                result += GetRunPenalty(y => Modules[y, x]);
            }

            for (int y = 0; y < _size - 1; y++)
            {
                for (int x = 0; x < _size - 1; x++)
                {
                    bool color = Modules[y, x];

                    if (color == Modules[y, x + 1] && color == Modules[y + 1, x] && color == Modules[y + 1, x + 1])
                    {
                        result += 3;
                    }
                }
            }

            int dark = 0;

            foreach (bool module in Modules)
            {
                if (module)
                {
                    dark++;
                }
            }

            int total = _size * _size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            result += Math.Max(0, k) * 10;
            return result;
        }

        private int GetRunPenalty(Func<int, bool> moduleAt)
        {
            int result = 0;
            int runLength = 1;

            for (int i = 1; i <= _size; i++)
            {
                if (i < _size && moduleAt(i) == moduleAt(i - 1))
                {
                    runLength++;
                    continue;
                }

                if (runLength >= 5)
                {
                    result += runLength - 2;
                }

                runLength = 1;
            }

            return result;
        }

        private static bool GetBit(int value, int index)
        {
            return ((value >> index) & 1) != 0;
        }
    }
    #endregion
}
#endregion
