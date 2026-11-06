// LZMA encoder.
//
// Encodes raw LZMA ("LZMA1") streams that any LZMA decoder accepts. The range coder, probability
// models, literal/length/distance coders and the end marker follow the LZMA SDK (public domain),
// the counterpart of LzmaDecoder.cs. Match finding is simpler than the SDK's optimal parser: a
// hash chain with greedy parsing, one step of lazy evaluation and repeat-distance checks. The
// output is somewhat larger than the SDK's best compression but is produced quickly and is
// verified by decoding it again (see CipLzma.Compress).

namespace TibiaFoundry.Core.Compression;

/// <summary>Encodes raw LZMA streams with an end marker.</summary>
public static class LzmaEncoder
{
    /// <summary>The properties the Tibia client uses for its LZMA files: lc=3, lp=0, pb=2, 32 MB dictionary.</summary>
    public static ReadOnlySpan<byte> TibiaProperties => [0x5D, 0x00, 0x00, 0x00, 0x02];

    /// <summary>Encodes <paramref name="input"/> with lc=3, lp=0, pb=2. Returns the payload (no header).</summary>
    /// <param name="maxChain">Match candidates examined per position: higher compresses better and slower.</param>
    public static byte[] Encode(ReadOnlySpan<byte> input, int maxChain = 48, CancellationToken ct = default)
    {
        var encoder = new Encoder(input.ToArray(), maxChain);
        return encoder.Run(ct);
    }

    private sealed class Encoder
    {
        private const int NumStates = 12;
        private const int PosStatesBitsMax = 4;
        private const int Lc = 3;
        private const int Pb = 2;
        private const uint PosStateMask = (1u << Pb) - 1;
        private const int MatchMinLen = 2;
        private const int MatchMaxLen = 273;
        private const int NumLenToPosStates = 4;
        private const int StartPosModelIndex = 4;
        private const int EndPosModelIndex = 14;
        private const int NumFullDistances = 128;
        private const int NumAlignBits = 4;
        private const int HashBits = 16;
        private const int NoPosition = -1;

        private readonly byte[] _data;
        private readonly int _maxChain;
        private readonly RangeEncoder _rc = new();

        private readonly ushort[] _isMatch = Probs(NumStates << PosStatesBitsMax);
        private readonly ushort[] _isRep = Probs(NumStates);
        private readonly ushort[] _isRepG0 = Probs(NumStates);
        private readonly ushort[] _isRepG1 = Probs(NumStates);
        private readonly ushort[] _isRepG2 = Probs(NumStates);
        private readonly ushort[] _isRep0Long = Probs(NumStates << PosStatesBitsMax);
        private readonly ushort[][] _posSlot = [Probs(64), Probs(64), Probs(64), Probs(64)];
        private readonly ushort[] _posEncoders = Probs(NumFullDistances - EndPosModelIndex);
        private readonly ushort[] _posAlign = Probs(1 << NumAlignBits);
        private readonly LenEncoder _lenEncoder = new();
        private readonly LenEncoder _repLenEncoder = new();
        private readonly ushort[] _literals = Probs(0x300 << Lc);

        private readonly int[] _head = new int[1 << HashBits];
        private readonly int[] _chain;

        private int _state;
        private readonly uint[] _reps = new uint[4];

        public Encoder(byte[] data, int maxChain)
        {
            _data = data;
            _maxChain = Math.Max(1, maxChain);
            _chain = new int[Math.Max(1, data.Length)];
            Array.Fill(_head, NoPosition);
        }

        private static ushort[] Probs(int count)
        {
            var p = new ushort[count];
            Array.Fill(p, (ushort)1024);
            return p;
        }

        public byte[] Run(CancellationToken ct)
        {
            int pos = 0;
            int inserted = 0;
            int length = _data.Length;
            while (pos < length)
            {
                if ((pos & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                while (inserted <= pos) Insert(inserted++);
                uint posState = (uint)pos & PosStateMask;

                int repLen = RepMatchLength(pos, _reps[0]);
                var (len, dist) = FindMatch(pos);

                // Repeat-distance matches are cheaper than new distances: prefer them when nearly as long.
                if (repLen >= MatchMinLen && repLen + 1 >= len)
                {
                    EncodeRep0(repLen, posState);
                    for (int i = 1; i < repLen; i++) Insert(inserted++);
                    pos += repLen;
                    continue;
                }

                if (len >= MatchMinLen && pos + 1 < length)
                {
                    // One step of lazy evaluation: a clearly longer match at the next byte wins.
                    Insert(inserted++);
                    var (nextLen, _) = FindMatch(pos + 1);
                    if (nextLen > len + 1)
                    {
                        EncodeLiteral(pos, posState);
                        pos++;
                        continue;
                    }
                }

                if (len >= MatchMinLen)
                {
                    EncodeMatch(dist, len, posState);
                    while (inserted < pos + len) Insert(inserted++);
                    pos += len;
                    continue;
                }

                if (pos > (int)_reps[0] && _data[pos] == _data[pos - (int)_reps[0] - 1] && _state >= 7)
                    EncodeShortRep(posState);
                else
                    EncodeLiteral(pos, posState);
                pos++;
            }
            WriteEndMarker((uint)pos & PosStateMask);
            _rc.Flush();
            return _rc.ToArray();
        }

        private uint Hash(int pos) => (uint)((_data[pos] | _data[pos + 1] << 8 | _data[pos + 2] << 16) * 2654435761u) >> (32 - HashBits);

        private void Insert(int pos)
        {
            if (pos + 2 >= _data.Length) return;
            uint h = Hash(pos);
            _chain[pos] = _head[h];
            _head[h] = pos;
        }

        /// <summary>Longest earlier occurrence of the bytes at <paramref name="pos"/> (length 0 when none).</summary>
        private (int Length, uint Distance) FindMatch(int pos)
        {
            int limit = Math.Min(MatchMaxLen, _data.Length - pos);
            if (limit < 3) return (0, 0);
            int bestLen = 0;
            uint bestDist = 0;
            uint h = Hash(pos);
            int candidate = _head[h];
            if (candidate == pos) candidate = _chain[pos];
            int chain = _maxChain;
            while (candidate != NoPosition && candidate < pos && chain-- > 0)
            {
                if (_data[candidate + bestLen] == _data[pos + bestLen])
                {
                    int l = 0;
                    while (l < limit && _data[candidate + l] == _data[pos + l]) l++;
                    if (l > bestLen)
                    {
                        bestLen = l;
                        bestDist = (uint)(pos - candidate - 1);
                        if (l == limit) break;
                    }
                }
                candidate = _chain[candidate];
            }
            // Short matches far away cost more than literals.
            if (bestLen == 3 && bestDist >= 1u << 14) return (0, 0);
            return (bestLen, bestDist);
        }

        private int RepMatchLength(int pos, uint rep)
        {
            int src = pos - (int)rep - 1;
            if (src < 0) return 0;
            int limit = Math.Min(MatchMaxLen, _data.Length - pos);
            int l = 0;
            while (l < limit && _data[src + l] == _data[pos + l]) l++;
            return l;
        }

        private void EncodeLiteral(int pos, uint posState)
        {
            _rc.EncodeBit(_isMatch, (_state << PosStatesBitsMax) + (int)posState, 0);
            byte prev = pos > 0 ? _data[pos - 1] : (byte)0;
            int baseIndex = 0x300 * (prev >> (8 - Lc));
            byte symbol = _data[pos];
            if (_state < 7)
            {
                uint context = 1;
                for (int i = 7; i >= 0; i--)
                {
                    uint bit = (uint)(symbol >> i) & 1;
                    _rc.EncodeBit(_literals, baseIndex + (int)context, bit);
                    context = context << 1 | bit;
                }
            }
            else
            {
                byte matchByte = _data[pos - (int)_reps[0] - 1];
                uint context = 1;
                bool same = true;
                for (int i = 7; i >= 0; i--)
                {
                    uint bit = (uint)(symbol >> i) & 1;
                    uint index = context;
                    if (same)
                    {
                        uint matchBit = (uint)(matchByte >> i) & 1;
                        index += (1 + matchBit) << 8;
                        same = matchBit == bit;
                    }
                    _rc.EncodeBit(_literals, baseIndex + (int)index, bit);
                    context = context << 1 | bit;
                }
            }
            _state = _state < 4 ? 0 : _state < 10 ? _state - 3 : _state - 6;
        }

        private void EncodeMatch(uint distance, int len, uint posState)
        {
            _rc.EncodeBit(_isMatch, (_state << PosStatesBitsMax) + (int)posState, 1);
            _rc.EncodeBit(_isRep, _state, 0);
            _lenEncoder.Encode(_rc, (uint)(len - MatchMinLen), posState);
            EncodeDistance(distance, len);
            _reps[3] = _reps[2];
            _reps[2] = _reps[1];
            _reps[1] = _reps[0];
            _reps[0] = distance;
            _state = _state < 7 ? 7 : 10;
        }

        private void EncodeDistance(uint distance, int len)
        {
            uint posSlot = GetPosSlot(distance);
            int lenToPosState = Math.Min(len - MatchMinLen, NumLenToPosStates - 1);
            BitTreeEncode(_posSlot[lenToPosState], 0, 6, posSlot);
            if (posSlot >= StartPosModelIndex)
            {
                int footerBits = (int)((posSlot >> 1) - 1);
                uint baseVal = (2 | (posSlot & 1)) << footerBits;
                uint posReduced = distance - baseVal;
                if (posSlot < EndPosModelIndex)
                {
                    BitTreeReverseEncode(_posEncoders, (int)(baseVal - posSlot - 1), footerBits, posReduced);
                }
                else
                {
                    _rc.EncodeDirectBits(posReduced >> NumAlignBits, footerBits - NumAlignBits);
                    BitTreeReverseEncode(_posAlign, 0, NumAlignBits, posReduced & ((1u << NumAlignBits) - 1));
                }
            }
        }

        private void EncodeRep0(int len, uint posState)
        {
            _rc.EncodeBit(_isMatch, (_state << PosStatesBitsMax) + (int)posState, 1);
            _rc.EncodeBit(_isRep, _state, 1);
            _rc.EncodeBit(_isRepG0, _state, 0);
            _rc.EncodeBit(_isRep0Long, (_state << PosStatesBitsMax) + (int)posState, 1);
            _repLenEncoder.Encode(_rc, (uint)(len - MatchMinLen), posState);
            _state = _state < 7 ? 8 : 11;
        }

        private void EncodeShortRep(uint posState)
        {
            _rc.EncodeBit(_isMatch, (_state << PosStatesBitsMax) + (int)posState, 1);
            _rc.EncodeBit(_isRep, _state, 1);
            _rc.EncodeBit(_isRepG0, _state, 0);
            _rc.EncodeBit(_isRep0Long, (_state << PosStatesBitsMax) + (int)posState, 0);
            _state = _state < 7 ? 9 : 11;
        }

        private void WriteEndMarker(uint posState)
        {
            _rc.EncodeBit(_isMatch, (_state << PosStatesBitsMax) + (int)posState, 1);
            _rc.EncodeBit(_isRep, _state, 0);
            _state = _state < 7 ? 7 : 10;
            _lenEncoder.Encode(_rc, 0, posState);
            const uint posSlot = (1 << 6) - 1;
            BitTreeEncode(_posSlot[0], 0, 6, posSlot);
            const int footerBits = 30;
            const uint posReduced = (1u << footerBits) - 1;
            _rc.EncodeDirectBits(posReduced >> NumAlignBits, footerBits - NumAlignBits);
            BitTreeReverseEncode(_posAlign, 0, NumAlignBits, posReduced & ((1u << NumAlignBits) - 1));
        }

        private static uint GetPosSlot(uint distance)
        {
            if (distance < 4) return distance;
            int n = 31 - System.Numerics.BitOperations.LeadingZeroCount(distance);
            return (uint)(n << 1) | ((distance >> (n - 1)) & 1);
        }

        private void BitTreeEncode(ushort[] probs, int offset, int numBits, uint symbol)
        {
            uint m = 1;
            for (int i = numBits - 1; i >= 0; i--)
            {
                uint bit = (symbol >> i) & 1;
                _rc.EncodeBit(probs, offset + (int)m, bit);
                m = m << 1 | bit;
            }
        }

        private void BitTreeReverseEncode(ushort[] probs, int offset, int numBits, uint symbol)
        {
            uint m = 1;
            for (int i = 0; i < numBits; i++)
            {
                uint bit = symbol & 1;
                _rc.EncodeBit(probs, offset + (int)m, bit);
                m = m << 1 | bit;
                symbol >>= 1;
            }
        }

        private sealed class LenEncoder
        {
            private readonly ushort[] _choice = Probs(2);
            private readonly ushort[] _low = Probs(16 << 3);
            private readonly ushort[] _mid = Probs(16 << 3);
            private readonly ushort[] _high = Probs(1 << 8);

            public void Encode(RangeEncoder rc, uint symbol, uint posState)
            {
                if (symbol < 8)
                {
                    rc.EncodeBit(_choice, 0, 0);
                    Tree(rc, _low, (int)(posState << 3), 3, symbol);
                    return;
                }
                rc.EncodeBit(_choice, 0, 1);
                symbol -= 8;
                if (symbol < 8)
                {
                    rc.EncodeBit(_choice, 1, 0);
                    Tree(rc, _mid, (int)(posState << 3), 3, symbol);
                }
                else
                {
                    rc.EncodeBit(_choice, 1, 1);
                    Tree(rc, _high, 0, 8, symbol - 8);
                }
            }

            private static void Tree(RangeEncoder rc, ushort[] probs, int offset, int numBits, uint symbol)
            {
                uint m = 1;
                for (int i = numBits - 1; i >= 0; i--)
                {
                    uint bit = (symbol >> i) & 1;
                    rc.EncodeBit(probs, offset + (int)m, bit);
                    m = m << 1 | bit;
                }
            }
        }
    }

    private sealed class RangeEncoder
    {
        private const uint TopValue = 1u << 24;
        private byte[] _out = new byte[1 << 16];
        private int _length;
        private ulong _low;
        private uint _range = 0xFFFFFFFF;
        private byte _cache;
        private long _cacheSize = 1;

        public void EncodeBit(ushort[] probs, int index, uint bit)
        {
            uint prob = probs[index];
            uint bound = (_range >> 11) * prob;
            if (bit == 0)
            {
                _range = bound;
                probs[index] = (ushort)(prob + ((2048 - prob) >> 5));
            }
            else
            {
                _low += bound;
                _range -= bound;
                probs[index] = (ushort)(prob - (prob >> 5));
            }
            while (_range < TopValue)
            {
                _range <<= 8;
                ShiftLow();
            }
        }

        public void EncodeDirectBits(uint value, int numBits)
        {
            for (int i = numBits - 1; i >= 0; i--)
            {
                _range >>= 1;
                if (((value >> i) & 1) == 1) _low += _range;
                if (_range < TopValue)
                {
                    _range <<= 8;
                    ShiftLow();
                }
            }
        }

        private void ShiftLow()
        {
            if ((uint)_low < 0xFF000000u || (uint)(_low >> 32) == 1)
            {
                byte temp = _cache;
                do
                {
                    if (_length == _out.Length) Array.Resize(ref _out, _out.Length * 2);
                    _out[_length++] = (byte)(temp + (_low >> 32));
                    temp = 0xFF;
                }
                while (--_cacheSize != 0);
                _cache = (byte)((uint)_low >> 24);
            }
            _cacheSize++;
            _low = (uint)_low << 8;
        }

        public void Flush()
        {
            for (int i = 0; i < 5; i++) ShiftLow();
        }

        public byte[] ToArray() => _out.AsSpan(0, _length).ToArray();
    }
}
