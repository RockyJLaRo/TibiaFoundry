// LZMA decoder.
//
// Port of the decoder half of the LZMA SDK (public domain), which the original 1.8 program shipped
// verbatim under the SevenZip.* namespaces. The algorithm is unchanged;
// the I/O was adapted to decode from a memory buffer straight into the output array (the
// output itself serves as the dictionary), which removes per-byte Stream calls.

namespace TibiaFoundry.Core.Compression;

/// <summary>Decodes raw LZMA ("LZMA1") streams.</summary>
/// <remarks>Corrupt data raises <see cref="InvalidDataException"/>.</remarks>
public static class LzmaDecoder
{
    /// <summary>
    /// Decodes an LZMA stream.
    /// </summary>
    /// <param name="properties">The 5 property bytes (lc/lp/pb byte + 32-bit dictionary size).</param>
    /// <param name="input">Compressed payload (after the 13-byte "LZMA alone" header, if any).</param>
    /// <param name="outputSize">Exact decompressed size, or -1 when unknown (decode until an end
    /// marker is found or the input is exhausted).</param>
    /// <param name="maxOutputSize">Safety cap used when <paramref name="outputSize"/> is unknown.</param>
    public static byte[] Decode(ReadOnlySpan<byte> properties, ReadOnlySpan<byte> input, long outputSize, int maxOutputSize = 256 * 1024 * 1024)
    {
        if (properties.Length < 5) throw new ArgumentException("LZMA properties must be 5 bytes.", nameof(properties));
        // Never trust a size taken from a file header: a damaged header must not cause a huge allocation.
        if (outputSize > maxOutputSize)
            throw new InvalidDataException($"LZMA: The declared size ({outputSize:N0} bytes) exceeds the limit of {maxOutputSize:N0} bytes; the file is damaged or not a supported format.");
        int d = properties[0];
        if (d >= 9 * 5 * 5) throw new InvalidDataException("LZMA: Invalid LZMA properties byte.");
        int lc = d % 9;
        d /= 9;
        int lp = d % 5;
        int pb = d / 5;
        var state = new DecoderState(lc, lp, pb);
        return state.Run(input, outputSize, maxOutputSize);
    }

    private sealed class DecoderState
    {
        private const int NumStates = 12;
        private const int PosStatesMax = 16;
        private const int NumLenToPosStates = 4;
        private const int StartPosModelIndex = 4;
        private const int EndPosModelIndex = 14;
        private const int NumFullDistances = 128;
        private const int NumAlignBits = 4;

        private readonly int _lc;
        private readonly int _lp;
        private readonly uint _posStateMask;
        private readonly uint _literalPosMask;

        private readonly ushort[] _isMatch = new ushort[NumStates << 4];
        private readonly ushort[] _isRep = new ushort[NumStates];
        private readonly ushort[] _isRepG0 = new ushort[NumStates];
        private readonly ushort[] _isRepG1 = new ushort[NumStates];
        private readonly ushort[] _isRepG2 = new ushort[NumStates];
        private readonly ushort[] _isRep0Long = new ushort[NumStates << 4];
        private readonly ushort[][] _posSlot = new ushort[NumLenToPosStates][];
        private readonly ushort[] _posDecoders = new ushort[NumFullDistances - EndPosModelIndex];
        private readonly ushort[] _posAlign = new ushort[1 << NumAlignBits];
        private readonly LenDecoder _lenDecoder;
        private readonly LenDecoder _repLenDecoder;
        private readonly ushort[] _literals;

        // Range decoder state.
        private uint _range;
        private uint _code;
        private int _inPos;
        private int _overrun; // bytes requested past the end of the input
        private byte[] _input = Array.Empty<byte>();

        public DecoderState(int lc, int lp, int pb)
        {
            if (pb > 4) throw new InvalidDataException("LZMA: Invalid pb.");
            _lc = lc;
            _lp = lp;
            _posStateMask = (1u << pb) - 1;
            _literalPosMask = (1u << lp) - 1;
            for (int i = 0; i < NumLenToPosStates; i++) _posSlot[i] = NewProbs(1 << 6);
            _lenDecoder = new LenDecoder(1 << pb);
            _repLenDecoder = new LenDecoder(1 << pb);
            _literals = NewProbs(0x300 << (lc + lp));
            InitProbs(_isMatch); InitProbs(_isRep); InitProbs(_isRepG0); InitProbs(_isRepG1);
            InitProbs(_isRepG2); InitProbs(_isRep0Long); InitProbs(_posDecoders); InitProbs(_posAlign);
        }

        private static ushort[] NewProbs(int count)
        {
            var a = new ushort[count];
            InitProbs(a);
            return a;
        }

        private static void InitProbs(ushort[] probs) => Array.Fill(probs, (ushort)1024);

        private byte NextByte()
        {
            if (_inPos < _input.Length) return _input[_inPos++];
            _overrun++;
            return 0;
        }

        private uint DecodeBit(ushort[] probs, int index)
        {
            uint prob = probs[index];
            uint bound = (_range >> 11) * prob;
            uint bit;
            if (_code < bound)
            {
                _range = bound;
                probs[index] = (ushort)(prob + ((2048 - prob) >> 5));
                bit = 0;
            }
            else
            {
                _range -= bound;
                _code -= bound;
                probs[index] = (ushort)(prob - (prob >> 5));
                bit = 1;
            }
            if (_range < 0x1000000)
            {
                _code = (_code << 8) | NextByte();
                _range <<= 8;
            }
            return bit;
        }

        private uint DecodeDirectBits(int count)
        {
            uint result = 0;
            for (int i = count; i > 0; i--)
            {
                _range >>= 1;
                uint t = (_code - _range) >> 31;
                _code -= _range & (t - 1);
                result = (result << 1) | (1 - t);
                if (_range < 0x1000000)
                {
                    _code = (_code << 8) | NextByte();
                    _range <<= 8;
                }
            }
            return result;
        }

        private uint BitTree(ushort[] probs, int numBits)
        {
            uint m = 1;
            for (int i = numBits; i > 0; i--) m = (m << 1) + DecodeBit(probs, (int)m);
            return m - (1u << numBits);
        }

        private uint ReverseBitTree(ushort[] probs, int offset, int numBits)
        {
            uint m = 1, symbol = 0;
            for (int i = 0; i < numBits; i++)
            {
                uint bit = DecodeBit(probs, offset + (int)m);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }

        public byte[] Run(ReadOnlySpan<byte> input, long outputSize, int maxOutputSize)
        {
            _input = input.ToArray();
            _inPos = 0;
            _range = uint.MaxValue;
            _code = 0;
            for (int i = 0; i < 5; i++) _code = (_code << 8) | NextByte();

            bool unknownSize = outputSize < 0;
            byte[] output = new byte[unknownSize ? Math.Min(maxOutputSize, Math.Max(1 << 16, input.Length * 8)) : outputSize];
            long limit = unknownSize ? maxOutputSize : outputSize;
            int pos = 0;
            uint state = 0, rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;

            while (pos < limit)
            {
                // The range coder legitimately reads up to 4 bytes ahead; beyond that the input is exhausted.
                if (_overrun > 4)
                {
                    if (unknownSize) break;
                    throw new InvalidDataException($"LZMA: The compressed data ended after {pos:N0} of {outputSize:N0} bytes; the file is truncated or damaged.");
                }
                if (pos >= output.Length) Array.Resize(ref output, (int)Math.Min(limit, (long)output.Length * 2));

                uint posState = (uint)pos & _posStateMask;
                if (DecodeBit(_isMatch, (int)((state << 4) + posState)) == 0)
                {
                    byte prevByte = pos > 0 ? output[pos - 1] : (byte)0;
                    int litBase = 0x300 * (int)((((uint)pos & _literalPosMask) << _lc) + ((uint)prevByte >> (8 - _lc)));
                    uint symbol = 1;
                    if (state >= 7)
                    {
                        byte matchByte = output[pos - (int)rep0 - 1];
                        do
                        {
                            uint matchBit = (uint)(matchByte >> 7) & 1;
                            matchByte <<= 1;
                            uint bit = DecodeBit(_literals, litBase + (int)(((1 + matchBit) << 8) + symbol));
                            symbol = (symbol << 1) | bit;
                            if (matchBit != bit)
                            {
                                while (symbol < 0x100) symbol = (symbol << 1) | DecodeBit(_literals, litBase + (int)symbol);
                                break;
                            }
                        }
                        while (symbol < 0x100);
                    }
                    else
                    {
                        do symbol = (symbol << 1) | DecodeBit(_literals, litBase + (int)symbol);
                        while (symbol < 0x100);
                    }
                    output[pos++] = (byte)symbol;
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    continue;
                }

                uint len;
                if (DecodeBit(_isRep, (int)state) == 1)
                {
                    if (pos == 0) throw new InvalidDataException("LZMA: Repeat match at stream start.");
                    if (DecodeBit(_isRepG0, (int)state) == 0)
                    {
                        if (DecodeBit(_isRep0Long, (int)((state << 4) + posState)) == 0)
                        {
                            state = state < 7 ? 9u : 11u;
                            output[pos] = output[pos - (int)rep0 - 1];
                            pos++;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (DecodeBit(_isRepG1, (int)state) == 0)
                        {
                            distance = rep1;
                        }
                        else
                        {
                            if (DecodeBit(_isRepG2, (int)state) == 0)
                            {
                                distance = rep2;
                            }
                            else
                            {
                                distance = rep3;
                                rep3 = rep2;
                            }
                            rep2 = rep1;
                        }
                        rep1 = rep0;
                        rep0 = distance;
                    }
                    len = _repLenDecoder.Decode(this, posState) + 2;
                    state = state < 7 ? 8u : 11u;
                }
                else
                {
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = rep0;
                    len = 2 + _lenDecoder.Decode(this, posState);
                    state = state < 7 ? 7u : 10u;
                    uint lenToPosState = len - 2 < NumLenToPosStates ? len - 2 : NumLenToPosStates - 1;
                    uint posSlot = BitTree(_posSlot[lenToPosState], 6);
                    if (posSlot >= StartPosModelIndex)
                    {
                        int numDirectBits = (int)((posSlot >> 1) - 1);
                        rep0 = (2 | (posSlot & 1)) << numDirectBits;
                        if (posSlot < EndPosModelIndex)
                        {
                            rep0 += ReverseBitTree(_posDecoders, (int)(rep0 - posSlot - 1), numDirectBits);
                        }
                        else
                        {
                            rep0 += DecodeDirectBits(numDirectBits - NumAlignBits) << NumAlignBits;
                            rep0 += ReverseBitTree(_posAlign, 0, NumAlignBits);
                        }
                    }
                    else
                    {
                        rep0 = posSlot;
                    }
                }

                if (rep0 >= pos)
                {
                    if (rep0 == uint.MaxValue) // end marker
                    {
                        if (unknownSize) break;
                        throw new InvalidDataException($"LZMA: End marker after {pos:N0} of {outputSize:N0} declared bytes; the file is damaged.");
                    }
                    throw new InvalidDataException("LZMA: Match distance beyond decoded data.");
                }

                long end = Math.Min(pos + (long)len, limit);
                if (end > output.Length) Array.Resize(ref output, (int)Math.Min(limit, Math.Max(end, (long)output.Length * 2)));
                int src = pos - (int)rep0 - 1;
                while (pos < end) output[pos++] = output[src++];
            }

            if (unknownSize || pos < output.Length) Array.Resize(ref output, pos);
            return output;
        }

        private sealed class LenDecoder
        {
            private readonly ushort[] _choice = NewProbs(2);
            private readonly ushort[][] _low;
            private readonly ushort[][] _mid;
            private readonly ushort[] _high = NewProbs(1 << 8);

            public LenDecoder(int numPosStates)
            {
                _low = new ushort[numPosStates][];
                _mid = new ushort[numPosStates][];
                for (int i = 0; i < numPosStates; i++)
                {
                    _low[i] = NewProbs(1 << 3);
                    _mid[i] = NewProbs(1 << 3);
                }
            }

            public uint Decode(DecoderState rc, uint posState)
            {
                if (rc.DecodeBit(_choice, 0) == 0) return rc.BitTree(_low[posState], 3);
                if (rc.DecodeBit(_choice, 1) == 0) return 8 + rc.BitTree(_mid[posState], 3);
                return 16 + rc.BitTree(_high, 8);
            }
        }
    }
}
