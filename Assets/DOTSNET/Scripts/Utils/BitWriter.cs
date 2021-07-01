// Bitpacking by vis2k for drastic bandwidth savings.
// See also: https://gafferongames.com/post/reading_and_writing_packets/
//           https://gafferongames.com/post/serialization_strategies/
//
// Why Bitpacking:
// + huge bandwidth savings possible
// + word aligned copying to buffer is fast
// + always little endian, no worrying about byte order on different systems
//
// BitWriter is ATOMIC! either all is written successfully, or nothing is.
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace DOTSNET
{
    public struct BitWriter
    {
        // scratch bits are twice the size of maximum allowed write size.
        // maximum is 32 bit, so we need 64 bit scratch for cases where
        // scratch is filled by 10 bits, and we writer another 32 into it.
        ulong scratch;
        int scratchBits;
        int wordIndex;
        byte[] buffer;

        // calculate space in bits, including scratch bits and buffer
        // (SpaceBits instead of SpaceInBits for consistency with RemainingBits)
        public int SpaceBits
        {
            get
            {
                // calculate space in scratch.
                int scratchBitsSpace = 32 - scratchBits;
                // calculate space in buffer. 4 bytes are needed for scratch.
                int bufferBitsSpace = (buffer.Length - wordIndex - 4) * 8;
                // scratch + buffer
                return scratchBitsSpace + bufferBitsSpace;
            }
        }

        // position is useful sometimes. read-only for now.
        // => wordIndex converted to bits + scratch position
        public int BitPosition => wordIndex * 8 + scratchBits;

        // byte[] constructor.
        // BitWriter will assume that the whole byte[] can be written into,
        // from start to end.
        public BitWriter(byte[] buffer)
        {
            scratch = 0;
            scratchBits = 0;
            wordIndex = 0;
            this.buffer = buffer;
        }

        // helpers /////////////////////////////////////////////////////////////
        // need to round bits to minimum amount of bytes they fit into
        internal static int RoundBitsToFullBytes(int bits)
        {
            // calculation example for up to 9 bits:
            //   0 - 1 = -1 then / 8 = 0 then + 1 = 1
            //   1 - 1 =  0 then / 8 = 0 then + 1 = 1
            //   2 - 1 =  1 then / 8 = 0 then + 1 = 1
            //   3 - 1 =  2 then / 8 = 0 then + 1 = 1
            //   4 - 1 =  3 then / 8 = 0 then + 1 = 1
            //   5 - 1 =  4 then / 8 = 0 then + 1 = 1
            //   6 - 1 =  5 then / 8 = 0 then + 1 = 1
            //   7 - 1 =  6 then / 8 = 0 then + 1 = 1
            //   8 - 1 =  7 then / 8 = 0 then + 1 = 1
            //   9 - 1 =  8 then / 8 = 1 then + 1 = 2
            return ((bits-1) / 8) + 1;
        }

        // helper function to swap int bytes endianness
        internal static uint SwapBytes(uint value)
        {
            return (value & 0x000000FFu) << 24 |
                   (value & 0x0000FF00u) << 8 |
                   (value & 0x00FF0000u) >> 8 |
                   (value & 0xFF000000u) >> 24;
        }

        // RoundToLong for huge double can overflow from long.max to long.min
        // Convert.ToInt64 throws exception if it would overflow.
        // we need one that properly Clamps.
        internal static long RoundAndClampToLong(double value)
        {
            // this would fail the test because rounding a int.max float overflows!
            //return Mathf.RoundToInt(Mathf.Clamp(value, int.MinValue, int.MaxValue));

            // this works perfectly!
            if (value >= long.MaxValue)
                return long.MaxValue;
            if (value <= long.MinValue)
                return long.MinValue;

            // Convert.ToInt64 so we don't need to depend on Unity.Mathf!
            return Convert.ToInt64(value);
        }

        // calculate bits needed for a value range
        // largest type we support is ulong, so use that as parameters
        // min, max are both INCLUSIVE
        //   min=0, max=7 means 0..7 = 8 values in total = 3 bits required
        internal static int BitsRequired(ulong min, ulong max)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"{nameof(BitsRequired)} min={min} needs to be <= max={max}");

            // if min == max then we need 0 bits because it is only ever one value
            if (min == max)
                return 0;

            // normalize from min..max to 0..max-min
            // example:
            //   min = 0, max = 7 => 7-0 = 7 (0..7 = 8 values needed)
            //   min = 4, max = 7 => 7-4 = 3 (0..3 = 4 values needed)
            //
            // CAREFUL: DO NOT ADD ANYTHING TO THIS VALUE.
            //          if min=0 and max=ulong.max then normalized = ulong.max,
            //          adding anything to it would make it overflow!
            //          (see tests!)
            ulong normalized = max - min;
            //UnityEngine.Debug.Log($"min={min} max={max} normalized={normalized}");

            // .Net Core 3.1 has BitOperations.Log2(x)
            // Unity doesn't, so we could use one of a dozen weird tricks:
            // https://stackoverflow.com/questions/15967240/fastest-implementation-of-log2int-and-log2float
            // including lookup tables, float exponent tricks for little endian,
            // etc.
            //
            // ... or we could just hard code!
            if (normalized < 2) return 1;
            if (normalized < 4) return 2;
            if (normalized < 8) return 3;
            if (normalized < 16) return 4;
            if (normalized < 32) return 5;
            if (normalized < 64) return 6;
            if (normalized < 128) return 7;
            if (normalized < 256) return 8;
            if (normalized < 512) return 9;
            if (normalized < 1024) return 10;
            if (normalized < 2048) return 11;
            if (normalized < 4096) return 12;
            if (normalized < 8192) return 13;
            if (normalized < 16384) return 14;
            if (normalized < 32768) return 15;
            if (normalized < 65536) return 16;
            if (normalized < 131072) return 17;
            if (normalized < 262144) return 18;
            if (normalized < 524288) return 19;
            if (normalized < 1048576) return 20;
            if (normalized < 2097152) return 21;
            if (normalized < 4194304) return 22;
            if (normalized < 8388608) return 23;
            if (normalized < 16777216) return 24;
            if (normalized < 33554432) return 25;
            if (normalized < 67108864) return 26;
            if (normalized < 134217728) return 27;
            if (normalized < 268435456) return 28;
            if (normalized < 536870912) return 29;
            if (normalized < 1073741824) return 30;
            if (normalized < 2147483648) return 31;
            if (normalized < 4294967296) return 32;
            if (normalized < 8589934592) return 33;
            if (normalized < 17179869184) return 34;
            if (normalized < 34359738368) return 35;
            if (normalized < 68719476736) return 36;
            if (normalized < 137438953472) return 37;
            if (normalized < 274877906944) return 38;
            if (normalized < 549755813888) return 39;
            if (normalized < 1099511627776) return 40;
            if (normalized < 2199023255552) return 41;
            if (normalized < 4398046511104) return 42;
            if (normalized < 8796093022208) return 43;
            if (normalized < 17592186044416) return 44;
            if (normalized < 35184372088832) return 45;
            if (normalized < 70368744177664) return 46;
            if (normalized < 140737488355328) return 47;
            if (normalized < 281474976710656) return 48;
            if (normalized < 562949953421312) return 49;
            if (normalized < 1125899906842624) return 50;
            if (normalized < 2251799813685248) return 51;
            if (normalized < 4503599627370496) return 52;
            if (normalized < 9007199254740992) return 53;
            if (normalized < 18014398509481984) return 54;
            if (normalized < 36028797018963968) return 55;
            if (normalized < 72057594037927936) return 56;
            if (normalized < 144115188075855872) return 57;
            if (normalized < 288230376151711744) return 58;
            if (normalized < 576460752303423488) return 59;
            if (normalized < 1152921504606846976) return 60;
            if (normalized < 2305843009213693952) return 61;
            if (normalized < 4611686018427387904) return 62;
            if (normalized < 9223372036854775808) return 63;
            return 64;
        }

        // helper function to copy the first 32 bit of scratch to buffer.
        // scratch should never have more than 32 bit of data filled because
        // write functions flush immediately after an overflow.
        // -> note that this function does not modify scratch or wordIndex.
        //    it simply copies scratch to end of buffer.
        unsafe void Copy32BitScratchToBuffer()
        {
            // extract the lower 32 bits word from scratch
            uint word = (uint)scratch & 0xFFFFFFFF;
            //UnityEngine.Debug.Log("scratch extracted word: 0x" + word.ToString("X"));

            // this is the old, slow, not aligned way to copy uint to buffer
            // + respects endianness
            // - is slower
            //buffer[wordIndex + 0] = (byte)(word);
            //buffer[wordIndex + 1] = (byte)(word >> 8);
            //buffer[wordIndex + 2] = (byte)(word >> 16);
            //buffer[wordIndex + 3] = (byte)(word >> 24);

            // fast copy word into buffer at word position
            // we use little endian order so that flushing
            // 0x2211 then 0x4433 becomes 0x11223344 in buffer
            // -> need to inverse if not on little endian
            // -> we do this with the uint, not with the buffer, so it's all
            //    still word aligned and fast!
            if (!BitConverter.IsLittleEndian)
            {
                word = SwapBytes(word);
            }

            // bitpacking works on aligned writes, but we want to support buffer
            // sizes that aren't multiple of 4 as well. it's more convenient.
            // => we want to copy 4 scratch bytes to buffer, or fewer if not
            //    enough space.
            int remaining = buffer.Length - wordIndex;
            int copy = Math.Min(4, remaining);

            // since we don't always copy exactly 4 bytes, we can't use the
            // uint* pointer assignment trick unless we want to have a special
            // case for '4 aligned' and 'not 4 aligned'. the most simple
            // solution is to use MemCopy with the variable amount of bytes.
            // => it's usually 4
            // => except for the last read which is 1,2,3 if buffer sizes is not
            //    a multiple of 4.
            fixed (byte* ptr = &buffer[wordIndex])
            {
                // if we copy >= 4 bytes then use the fast method
                // (99% of the time except for the last reads)
                // => this makes bitreader significantly faster, see benchmark!
                if (copy == 4)
                {
                    // reinterpret as uint* and return first element
                    // note that *ptr dereferencing doesn't seem to work on android:
                    // https://github.com/vis2k/Mirror/issues/2511
                    // => this approach hopefully does
                    // => we do reinterpret & assignment in TWO steps so that it's
                    //    easier to debug in case there are android issues again.
                    uint* reinterpeted = (uint*)ptr;
                    reinterpeted[0] = word;
                }
                // otherwise use the safe method to only copy what is left.
                // Unity doesn't have Buffer.MemoryCopy yet, so we need an #ifdef
                // note: we could use a for loop too, but that's slower.
                else
                {
                    byte* wordPtr = (byte*)&word;
#if UNITY_2017_1_OR_NEWER
                    UnsafeUtility.MemCpy(ptr, wordPtr, copy);
#else
                    Buffer.MemoryCopy(wordPtr, ptr, copy);
#endif
                }
            }
        }

        // segment /////////////////////////////////////////////////////////////
        // generate a segment of written data
        // the challenge is to INCLUDE scratch, with NO SIDE EFFECTs.
        // -> flushing scratch would have side effects and change future writes.
        // -> we need to include scratch in the segment, but not modify scratch
        //    or wordIndex
        // => in other words: WriteInt(); segment(); WriteInt() should
        //    be the same as  WriteInt(); WriteInt();
        //
        // IMPORTANT: this sounds to full bytes so it should only ever be used
        //            before sending it to the socket.
        //            DO NOT pass the segment to another BitWriter. the receiver
        //            would not be able to properly BitRead beause of the filler
        //            bits.
        public ArraySegment<byte> segment
        {
            get
            {
                // any data in scratch that we need to include?
                if (scratchBits > 0)
                {
                    // aligned fast copy the 32 sratch bits into buffer.
                    // this does not modify scratch/scratchBits/wordIndex!
                    // we simply copy it to the end of the buffer.
                    Copy32BitScratchToBuffer();

                    // round the scratchBits to a full byte
                    int scratchBytes = RoundBitsToFullBytes(scratchBits);

                    // return the segment that includes the scratch bytes.
                    // -> we don't include the full 4 bytes word that was copied
                    // -> only the minimum amount of bytes to save bandwidth
                    // => the copy was already fast and aligned.
                    // => creating the segment is same speed for all bytes.
                    return new ArraySegment<byte>(buffer, 0, wordIndex + scratchBytes);
                }
                // otherwise simply return buffer until wordIndex
                else return new ArraySegment<byte>(buffer, 0, wordIndex);
            }
        }

        // WriteBITS ///////////////////////////////////////////////////////////
        // write 'n' bits of an uint
        // bits can be between 0 and 32.
        //
        // for example:
        //   1 bit = 0..1
        //   2 bit = 0..3
        //   3 bit = 0..7
        //   4 bit = 0..15
        //   5 bit = 0..31
        //   6 bit = 0..63
        //   7 bit = 0..127
        //   8 bit = 0..255
        //  16 bit = 0..64k
        //
        // => named WriteUIntBITS so it's obvious that it's bits, not range!
        public bool WriteUIntBits(uint value, int bits)
        {
            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // (at least 1 bit so we do anything. at max 32.)
            // => throws exception because the developer should fix it immediately
            //
            // NOTE: we allow 0 bits so that WriteULongBits is easier to
            //       implement where we simply do two WriteUInts and the second
            //       one might pass '0' for bits but still succeeds.
            if (bits < 0 || bits > 32)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteUIntBits)} supports between 0 and 32 bits");

            // 0 bits is valid, simply do nothing
            if (bits == 0)
                return true;

            // make sure there is enough space in buffer
            if (SpaceBits < bits)
                return false;

            // create the mask
            // for example, for 8 bits it is 0x000000FF
            // need ulong so we can shift left 32 for 32 bits
            // (would be too much for uint)
            ulong mask = (1ul << bits) - 1;
            //UnityEngine.Debug.Log("mask: 0x" + mask.ToString("X"));

            // extract the 'n' bits out of value
            // need ulong so we can shift left far enough in the step below!
            // so for 0xAABBCCDD if we extract 8 bits we extract the last 0xDD
            ulong extracted = value & mask;
            //UnityEngine.Debug.Log("extracted: 0x" + extracted.ToString("X"));

            // move the extracted part into scratch at scratch position
            // so for scratch 0x000000FF it becomes 0x0000DDFF
            scratch |= extracted << scratchBits;
            //UnityEngine.Debug.Log("scratch: 0x" + scratch.ToString("X16"));

            // update scratch position
            scratchBits += bits;

            // if we overflow more than 32 bits, then flush the 32 bits to buffer
            if (scratchBits >= 32)
            {
                // copy 32 bit scratch to buffer
                Copy32BitScratchToBuffer();

                // update word index in buffer
                wordIndex += 4;

                // move the scratch remainder to the beginning
                // (we don't just zero it because a write function might call this
                //  for an overflowed scratch. need to keep the data.)
                scratch >>= 32;
                scratchBits -= 32;
                //UnityEngine.Debug.Log("scratch flushed: 0x" + scratch.ToString("X16"));
            }

            return true;
        }

        // write ulong as two uints
        public bool WriteULongBits(ulong value, int bits)
        {
            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // (at least 1 bit so we do anything. at max 32.)
            // => throws exception because the developer should fix it immediately
            //
            // NOTE: we allow 0 bits just like all other functions
            if (bits < 0 || bits > 64)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteULongBits)} supports between 0 and 64 bits");

            // make sure there is enough space in buffer
            // => we do two WriteUInts below. checking size first makes it atomic!
            if (SpaceBits < bits)
                return false;

            // write both halves as uint
            // => first one up to 32 bits
            // => second one the remainder. WriteUInt does nothing if bits is 0.
            uint lower = (uint)value;
            uint upper = (uint)(value >> 32);
            int lowerBits = Math.Min(bits, 32);
            int upperBits = Math.Max(bits - 32, 0);
            return WriteUIntBits(lower, lowerBits) &&
                   WriteUIntBits(upper, upperBits);
        }

        // write 'n' bits of an ushort
        // bits can be between 0 and 16.
        //
        // for example:
        //   1 bit = 0..1
        //   2 bit = 0..3
        //   3 bit = 0..7
        //   4 bit = 0..15
        //   5 bit = 0..31
        //   6 bit = 0..63
        //   7 bit = 0..127
        //   8 bit = 0..255
        //  16 bit = 0..64k
        //
        // reuses WriteUInt for now. inline if needed.
        public bool WriteUShortBits(ushort value, int bits)
        {
            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // (at least 1 bit so we do anything. at max 16.)
            // => throws exception because the developer should fix it immediately
            //
            // NOTE: we allow 0 bits so that WriteULongBits is easier to
            //       implement where we simply do two WriteUInts and the second
            //       one might pass '0' for bits but still succeeds.
            if (bits < 0 || bits > 16)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteUShortBits)} supports between 0 and 16 bits");

            return WriteUIntBits(value, bits);
        }

        // write 'n' bits of a byte
        // bits can be between 0 and 8.
        //
        // for example:
        //   1 bit = 0..1
        //   2 bit = 0..3
        //   3 bit = 0..7
        //   4 bit = 0..15
        //   5 bit = 0..31
        //   6 bit = 0..63
        //   7 bit = 0..127
        //   8 bit = 0..255
        //
        // reuses WriteUInt for now. inline if needed.
        public bool WriteByteBits(ushort value, int bits)
        {
            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // (at least 1 bit so we do anything. at max 16.)
            // => throws exception because the developer should fix it immediately
            //
            // NOTE: we allow 0 bits so that WriteULongBits is easier to
            //       implement where we simply do two WriteUInts and the second
            //       one might pass '0' for bits but still succeeds.
            if (bits < 0 || bits > 8)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteByteBits)} supports between 0 and 8 bits");

            return WriteUIntBits(value, bits);
        }

        // write bool as 1 bit
        // reuses WriteUInt for now. inline if needed.
        public bool WriteBool(bool value) =>
            WriteUIntBits(value ? 1u : 0u, 1);

        // Write RANGE /////////////////////////////////////////////////////////
        // WriteUInt within a known range, packing into minimum amount of bits.
        public bool WriteUInt(uint value, uint min = uint.MinValue, uint max = uint.MaxValue)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (!(min <= value && value <= max))
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteUInt)} value={value} needs to be within min={min} and max={max}");

            // calculate bits required for value range
            int bits = BitsRequired(min, max);

            // write normalized value with bits required for that value range
            // for example
            //   value = 5 in range [2..9]
            //   normalized range is max-min => [0..7]
            //   value-min => '3' in the range [0..7]
            return WriteUIntBits(value - min, bits);
        }

        // WriteInt within a known range, packing into minimum amount of bits
        // by shifting to uint (which doesn't need the high order bit)
        public bool WriteInt(int value, int min = int.MinValue, int max = int.MaxValue)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (!(min <= value && value <= max))
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteInt)} value={value} needs to be within min={min} and max={max}");

            // negative ints will always have the highest order bit set.
            // we shift to uint (which doesn't need the high order bit) by
            // subtracting 'min' from all values:
            //
            //     min   :=   min - min = 0
            //     max   :=   max - min
            //     value := value - min
            //
            // this works in all cases:
            //
            //   negative..positive example:
            //     value = 2, range = [-2..7]
            //     shift all by -min so by 2
            //     => value = 4, range = [0..9]
            //
            //   negative..negative example:
            //     value = -2, range = [-4..-1]
            //     shift all by -min so by 4
            //     => value = 2, range = [0..3]
            //
            //   positive..positive example:
            //     value = 4, range = [2..9]
            //     shift all by -min so by -2
            //     => value = 2, range = [0..7]
            //
            // note: int fits same range as uint, no risk for overflows.

            // calculate bits required for value range
            int bits = BitsRequired(0, (uint)(max - min));

            // write normalized value with bits required for that value range
            return WriteUIntBits((uint)(value - min), bits);
        }

        // WriteULong within a known range, packing into minimum amount of bits.
        public bool WriteULong(ulong value, ulong min = ulong.MinValue, ulong max = ulong.MaxValue)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (!(min <= value && value <= max))
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteULong)} value={value} needs to be within min={min} and max={max}");

            // calculate bits required for value range
            int bits = BitsRequired(min, max);

            // write normalized value with bits required for that value range
            // for example
            //   value = 5 in range [2..9]
            //   normalized range is max-min => [0..7]
            //   value-min => '3' in the range [0..7]
            return WriteULongBits(value - min, bits);
        }

        // WriteLong within a known range, packing into minimum amount of bits
        // by shifting to ulong (which doesn't need the high order bit)
        public bool WriteLong(long value, long min = long.MinValue, long max = long.MaxValue)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (!(min <= value && value <= max))
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteLong)} value={value} needs to be within min={min} and max={max}");

            // negative ints will always have the highest order bit set.
            // we shift to uint (which doesn't need the high order bit) by
            // subtracting 'min' from all values:
            //
            //     min   :=   min - min = 0
            //     max   :=   max - min
            //     value := value - min
            //
            // this works in all cases:
            //
            //   negative..positive example:
            //     value = 2, range = [-2..7]
            //     shift all by -min so by 2
            //     => value = 4, range = [0..9]
            //
            //   negative..negative example:
            //     value = -2, range = [-4..-1]
            //     shift all by -min so by 4
            //     => value = 2, range = [0..3]
            //
            //   positive..positive example:
            //     value = 4, range = [2..9]
            //     shift all by -min so by -2
            //     => value = 2, range = [0..7]
            //
            // note: long fits same range as ulong, no risk for overflows.

            // calculate bits required for value range
            int bits = BitsRequired(0, (ulong)(max - min));

            // write normalized value with bits required for that value range
            return WriteULongBits((ulong)(value - min), bits);
        }

        // WriteUShort within a known range, packing into minimum amount of bits.
        public bool WriteUShort(ushort value, ushort min = ushort.MinValue, ushort max = ushort.MaxValue)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (!(min <= value && value <= max))
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteUShort)} value={value} needs to be within min={min} and max={max}");

            // calculate bits required for value range
            int bits = BitsRequired(min, max);

            // write normalized value with bits required for that value range
            // for example
            //   value = 5 in range [2..9]
            //   normalized range is max-min => [0..7]
            //   value-min => '3' in the range [0..7]
            return WriteUShortBits((ushort)(value - min), bits);
        }

        // WriteShort within a known range, packing into minimum amount of bits
        // by shifting to uint (which doesn't need the high order bit)
        public bool WriteShort(short value, short min = short.MinValue, short max = short.MaxValue)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (!(min <= value && value <= max))
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteShort)} value={value} needs to be within min={min} and max={max}");

            // negative ints will always have the highest order bit set.
            // we shift to uint (which doesn't need the high order bit) by
            // subtracting 'min' from all values:
            //
            //     min   :=   min - min = 0
            //     max   :=   max - min
            //     value := value - min
            //
            // this works in all cases:
            //
            //   negative..positive example:
            //     value = 2, range = [-2..7]
            //     shift all by -min so by 2
            //     => value = 4, range = [0..9]
            //
            //   negative..negative example:
            //     value = -2, range = [-4..-1]
            //     shift all by -min so by 4
            //     => value = 2, range = [0..3]
            //
            //   positive..positive example:
            //     value = 4, range = [2..9]
            //     shift all by -min so by -2
            //     => value = 2, range = [0..7]
            //
            // note: short fits same range as ushort, no risk for overflows.

            // calculate bits required for value range
            int bits = BitsRequired(0, (ushort)(max - min));

            // write normalized value with bits required for that value range
            return WriteUShortBits((ushort)(value - min), bits);
        }

        // WriteByte within a known range, packing into minimum amount of bits.
        public bool WriteByte(byte value, byte min = byte.MinValue, byte max = byte.MaxValue)
        {
            // make sure value is within range
            // => throws exception because the developer should fix it immediately
            if (!(min <= value && value <= max))
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteByte)} value={value} needs to be within min={min} and max={max}");

            // calculate bits required for value range
            int bits = BitsRequired(min, max);

            // write normalized value with bits required for that value range
            // for example
            //   value = 5 in range [2..9]
            //   normalized range is max-min => [0..7]
            //   value-min => '3' in the range [0..7]
            return WriteByteBits((byte)(value - min), bits);
        }

        // Write Uncompressed //////////////////////////////////////////////////
        // write a byte[]
        // note: BitReader can't read ArraySegments because we use scratch.
        //       for consistency, we also don't write ArraySegments here.
        public bool WriteBytes(byte[] bytes, int offset, int size)
        {
            // make sure offset is valid
            // => throws exception because the developer should fix it immediately
            if (offset < 0 || offset > bytes.Length)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteBytes)} offset {offset} needs to be between 0 and {bytes.Length}");

            // make sure size is valid
            // => throws exception because the developer should fix it immediately
            if (size < 0 || size > bytes.Length - offset)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteBytes)} size {size} needs to be between 0 and {bytes.Length} - {offset}");

            // size = 0 is valid. simply do nothing.
            if (size == 0)
                return true;

            // make sure there is enough space in scratch + buffer
            // size is in bytes. convert to bits.
            if (SpaceBits < size * 8)
                return false;

            // simply reuse WriteByte for now.
            // => copy in up to 32 bit chunks later for performance!
            // (flushing scratch then memcpy bytes would insert placeholders if
            //  scratch isn't a multiple of 8 bits)
            for (int i = 0; i < size; ++i)
                if (!WriteByteBits(bytes[offset + i], 8))
                    return false;
            return true;
        }

        // write a byte[] with size in BITS, not BYTES
        // we might want to pass in another writer's content without filler bits
        // for example:
        //   ArraySegment<byte> segment = other.segment;
        //   WriteBytesBitSize(segment.Array, segment.Offset, other.BitPosition)
        // instead of
        //   WriteBytesBitSize(segment.Array, segment.Offset, segment.Count)
        // which would include filler bits, making it impossible to BitRead more
        // than one writer's content later.
        public bool WriteBytesBitSize(byte[] bytes, int offsetInBytes, int sizeInBits)
        {
            // make sure offset is valid
            // => throws exception because the developer should fix it immediately
            if (offsetInBytes < 0 || offsetInBytes > bytes.Length)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteBytesBitSize)} offsetInBytes {offsetInBytes} needs to be between 0 and {bytes.Length}");

            // make sure size is valid
            // => throws exception because the developer should fix it immediately
            if (sizeInBits < 0 || sizeInBits > (bytes.Length - offsetInBytes) * 8)
                throw new ArgumentOutOfRangeException($"BitWriter {nameof(WriteBytesBitSize)} sizeInBits {sizeInBits} needs to be between 0 and ({bytes.Length} - {offsetInBytes}) * 8");

            // size = 0 is valid. simply do nothing.
            if (sizeInBits == 0)
                return true;

            // make sure there is enough space in scratch + buffer
            if (SpaceBits < sizeInBits)
                return false;

            // simply reuse WriteByte for now.
            // => copy in up to 32 bit chunks later for performance!
            // (flushing scratch then memcpy bytes would insert placeholders if
            //  scratch isn't a multiple of 8 bits)
            //
            // size is in bits, so / 8 to get amount of FULL BYTES we can write
            // and then write the remaining bits at the end (if any)
            // for example:
            //   sizeInBits 0 => 0 fullBytes
            //   sizeInBits 1 => 0 fullBytes
            //   sizeInBits 2 => 0 fullBytes
            //   sizeInBits 3 => 0 fullBytes
            //   sizeInBits 4 => 0 fullBytes
            //   sizeInBits 5 => 0 fullBytes
            //   sizeInBits 6 => 0 fullBytes
            //   sizeInBits 7 => 0 fullBytes
            //   sizeInBits 8 => 1 fullBytes
            //   sizeInBits 9 => 1 fullBytes
            int fullBytes = sizeInBits / 8;
            for (int i = 0; i < fullBytes; ++i)
                if (!WriteByteBits(bytes[offsetInBytes + i], 8))
                    return false;

            // now write the final partial byte (remaining bits) if any
            int remainingBits = sizeInBits - (fullBytes * 8);
            if (remainingBits > 0)
            {
                //UnityEngine.Debug.Log("writing " + remainingBits + " bits from partial byte: " + bytes[offsetInBytes + fullBytes].ToString("X2"));
                if (!WriteByteBits(bytes[offsetInBytes + fullBytes], remainingBits))
                    return false;
            }

            return true;
        }

        // FloatUInt union
        [StructLayout(LayoutKind.Explicit)]
        internal struct FloatUInt
        {
            [FieldOffset(0)] internal float floatValue;
            [FieldOffset(0)] internal uint uintValue;
        }

        // write 32 bit uncompressed float
        // reuses WriteUInt via FloatUInt like in the article
        public bool WriteFloat(float value) =>
            WriteUIntBits(new FloatUInt{floatValue = value}.uintValue, 32);

        // write compressed float with given range and precision.
        // see also: https://gafferongames.com/post/serialization_strategies/
        //
        // for example:
        //   value = 12.3 in range [0..100]
        //   precision = 0.1
        //   we divide by precision. or in other words, for 0.1 we multiply by 10
        //     => value = 123 in range [0..1000] (rounded to int)
        //     => fits into 10 bits (0..1023) instead of 32 bits
        //
        // to avoid exploits, it returns false if int overflows would happen.
        public bool WriteFloat(float value, float min, float max, float precision)
        {
            // divide by precision. example: for 0.1 we multiply by 10.

            // we need to handle the edge case where the float becomes
            // > int.max or < int.min!
            // => we could either clamp to int.max/min, but then the reader part
            //    would be somewhat odd because if we read int.max/min, we would
            //    not know if the original value was bigger than int.max or
            //    exactly int.max.
            // => we could simply let it overflow. this would cause weird cases
            //    where a play might be teleported from int.max to int.min
            //    when overflowing.
            // => we could throw an exception to make it obvious, but then an
            //    attacker might try sending out of range values to the server,
            //    causing the server to throw a runtime exception which might
            //    stop everything unless we handled it somewhere. there is no
            //    guarantee that we do, since unlike Java, C# does not enforce
            //    handling all the underlying exceptions.
            // => the only 100% correct solution is to simply return false to
            //    indicate that this value in this range can not be serialized.
            //    in the case of multiplayer games it's safer to indicate that
            //    serialization failed and then disconnect the connection
            //    instead of potentially opening the door for exploits.
            //    (this is also the most simple solution without clamping
            //     needing a separate ClampAndRoundToInt helper function!)

            // scale at first
            float valueScaled = value / precision;
            float minScaled = min / precision;
            float maxScaled = max / precision;

            // check bounds before converting to int
            if (valueScaled < int.MinValue || valueScaled > int.MaxValue ||
                  minScaled < int.MinValue ||   minScaled > int.MaxValue ||
                  maxScaled < int.MinValue ||   maxScaled > int.MaxValue)
                return false;

            // Convert.ToInt32 so we don't need to depend on Unity.Mathf!
            int valueRounded = Convert.ToInt32(valueScaled);
            int minRounded = Convert.ToInt32(minScaled);
            int maxRounded = Convert.ToInt32(maxScaled);

            // write the int range
            return WriteInt(valueRounded, minRounded, maxRounded);
        }

        // DoubleUInt union
        [StructLayout(LayoutKind.Explicit)]
        internal struct DoubleULong
        {
            [FieldOffset(0)] internal double doubleValue;
            [FieldOffset(0)] internal ulong ulongValue;
        }

        // write 64 bit uncompressed double
        // reuses WriteULong via DoubleULong like in the article
        public bool WriteDouble(double value) =>
            WriteULongBits(new DoubleULong{doubleValue = value}.ulongValue, 64);

        // write compressed double with given range and precision.
        // see also: https://gafferongames.com/post/serialization_strategies/
        //
        // for example:
        //   value = 12.3 in range [0..100]
        //   precision = 0.1
        //   we divide by precision. or in other words, for 0.1 we multiply by 10
        //     => value = 123 in range [0..1000] (rounded to int)
        //     => fits into 10 bits (0..1023) instead of 64 bits
        //
        // to avoid exploits, it returns false if long overflows would happen.
        public bool WriteDouble(double value, double min, double max, double precision)
        {
            // divide by precision. example: for 0.1 we multiply by 10.

            // we need to handle the edge case where the double becomes
            // > long.max or < long.min!
            // => we could either clamp to long.max/min, but then the reader part
            //    would be somewhat odd because if we read long.max/min, we would
            //    not know if the original value was bigger than long.max or
            //    exactly long.max.
            // => we could simply let it overflow. this would cause weird cases
            //    where a play might be teleported from long.max to long.min
            //    when overflowing.
            // => we could throw an exception to make it obvious, but then an
            //    attacker might try sending out of range values to the server,
            //    causing the server to throw a runtime exception which might
            //    stop everything unless we handled it somewhere. there is no
            //    guarantee that we do, since unlike Java, C# does not enforce
            //    handling all the underlying exceptions.
            // => the only 100% correct solution is to simply return false to
            //    indicate that this value in this range can not be serialized.
            //    in the case of multiplayer games it's safer to indicate that
            //    serialization failed and then disconnect the connection
            //    instead of potentially opening the door for exploits.
            //    (this is also the most simple solution without clamping
            //     needing a separate ClampAndRoundToInt helper function!)

            // scale at first
            double valueScaled = value / precision;
            double minScaled = min / precision;
            double maxScaled = max / precision;

            // check bounds before converting to long
            if (valueScaled < long.MinValue || valueScaled > long.MaxValue ||
                  minScaled < long.MinValue ||   minScaled > long.MaxValue ||
                  maxScaled < long.MinValue ||   maxScaled > long.MaxValue)
                return false;

            // Convert.ToInt64 so we don't need to depend on Unity.Mathf!
            long valueRounded = Convert.ToInt64(valueScaled);
            long minRounded = Convert.ToInt64(minScaled);
            long maxRounded = Convert.ToInt64(maxScaled);

            // write the long range
            return WriteLong(valueRounded, minRounded, maxRounded);
        }

        // ECS types ///////////////////////////////////////////////////////////
        // write quaternion, uncompressed
        public bool WriteQuaternion(quaternion value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 4*4 bytes, converted to bits)
            if (SpaceBits < 16 * 8)
                return false;

            // write 4 floats
            return WriteFloat(value.value.x) &&
                   WriteFloat(value.value.y) &&
                   WriteFloat(value.value.z) &&
                   WriteFloat(value.value.w);
        }

        // write quaternion with smallest-three compression
        // see also: https://gafferongames.com/post/snapshot_compression/
        //
        // reuses our smallest three compression for quaternion->uint 32 bit.
        // maybe make this 29 bits later.
        //
        // IMPORTANT: assumes normalized quaternion!
        //            we also normalize when decompressing.
        public bool WriteQuaternionSmallestThree(quaternion value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (our compression uses 32 bit. maybe use 29 bit later.)
            if (SpaceBits < 32)
                return false;

            // compress and write
            uint compressed = Compression.CompressQuaternion(value);
            return WriteUIntBits(compressed, 32);
        }

        public bool WriteBytes16(Bytes16 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 16 bytes, converted to bits)
            if (SpaceBits < 16 * 8)
                return false;

            // write the 16 bytes

            // unsafe:
            // Unity ECS uses UnsafeUtility.AddressOf in FixedString32 etc. too.
            // => shorter code but same performance.
            //
            //unsafe
            //{
            //    byte* ptr = (byte*)UnsafeUtility.AddressOf(ref value);
            //    for (int i = 0; i < 16; ++i)
            //    {
            //        if (!WriteByteBits(ptr[i], 8))
            //            return false;
            //    }
            //    return true;
            //}

            // safe (same performance):
            // note: it would be faster to use the unsafe method and copy 4 byte
            //       uints at a time, but let's keep it safe for now.
            return WriteByteBits(value.byte0000, 8) &&
                   WriteByteBits(value.byte0001, 8) &&
                   WriteByteBits(value.byte0002, 8) &&
                   WriteByteBits(value.byte0003, 8) &&
                   WriteByteBits(value.byte0004, 8) &&
                   WriteByteBits(value.byte0005, 8) &&
                   WriteByteBits(value.byte0006, 8) &&
                   WriteByteBits(value.byte0007, 8) &&
                   WriteByteBits(value.byte0008, 8) &&
                   WriteByteBits(value.byte0009, 8) &&
                   WriteByteBits(value.byte0010, 8) &&
                   WriteByteBits(value.byte0011, 8) &&
                   WriteByteBits(value.byte0012, 8) &&
                   WriteByteBits(value.byte0013, 8) &&
                   WriteByteBits(value.byte0014, 8) &&
                   WriteByteBits(value.byte0015, 8);
        }

        public bool WriteBytes30(Bytes30 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 30 bytes, converted to bits)
            if (SpaceBits < 30 * 8)
                return false;

            // write the 30 bytes
            // note: could write in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return WriteByteBits(value.byte0000, 8) &&
                   WriteByteBits(value.byte0001, 8) &&
                   WriteByteBits(value.byte0002, 8) &&
                   WriteByteBits(value.byte0003, 8) &&
                   WriteByteBits(value.byte0004, 8) &&
                   WriteByteBits(value.byte0005, 8) &&
                   WriteByteBits(value.byte0006, 8) &&
                   WriteByteBits(value.byte0007, 8) &&
                   WriteByteBits(value.byte0008, 8) &&
                   WriteByteBits(value.byte0009, 8) &&
                   WriteByteBits(value.byte0010, 8) &&
                   WriteByteBits(value.byte0011, 8) &&
                   WriteByteBits(value.byte0012, 8) &&
                   WriteByteBits(value.byte0013, 8) &&
                   WriteBytes16(value.byte0014);
        }

        public bool WriteBytes62(Bytes62 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 62 bytes, converted to bits)
            if (SpaceBits < 62 * 8)
                return false;

            // write the 62 bytes
            // note: could write in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return WriteByteBits(value.byte0000, 8) &&
                   WriteByteBits(value.byte0001, 8) &&
                   WriteByteBits(value.byte0002, 8) &&
                   WriteByteBits(value.byte0003, 8) &&
                   WriteByteBits(value.byte0004, 8) &&
                   WriteByteBits(value.byte0005, 8) &&
                   WriteByteBits(value.byte0006, 8) &&
                   WriteByteBits(value.byte0007, 8) &&
                   WriteByteBits(value.byte0008, 8) &&
                   WriteByteBits(value.byte0009, 8) &&
                   WriteByteBits(value.byte0010, 8) &&
                   WriteByteBits(value.byte0011, 8) &&
                   WriteByteBits(value.byte0012, 8) &&
                   WriteByteBits(value.byte0013, 8) &&
                   WriteBytes16(value.byte0014) &&
                   WriteBytes16(value.byte0030) &&
                   WriteBytes16(value.byte0046);
        }

        public bool WriteBytes126(Bytes126 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 126 bytes, converted to bits)
            if (SpaceBits < 126 * 8)
                return false;

            // write the 126 bytes
            // note: could write in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return WriteByteBits(value.byte0000, 8) &&
                   WriteByteBits(value.byte0001, 8) &&
                   WriteByteBits(value.byte0002, 8) &&
                   WriteByteBits(value.byte0003, 8) &&
                   WriteByteBits(value.byte0004, 8) &&
                   WriteByteBits(value.byte0005, 8) &&
                   WriteByteBits(value.byte0006, 8) &&
                   WriteByteBits(value.byte0007, 8) &&
                   WriteByteBits(value.byte0008, 8) &&
                   WriteByteBits(value.byte0009, 8) &&
                   WriteByteBits(value.byte0010, 8) &&
                   WriteByteBits(value.byte0011, 8) &&
                   WriteByteBits(value.byte0012, 8) &&
                   WriteByteBits(value.byte0013, 8) &&
                   WriteBytes16(value.byte0014) &&
                   WriteBytes16(value.byte0030) &&
                   WriteBytes16(value.byte0046) &&
                   WriteBytes16(value.byte0062) &&
                   WriteBytes16(value.byte0078) &&
                   WriteBytes16(value.byte0094) &&
                   WriteBytes16(value.byte0110);
        }

        public bool WriteBytes510(Bytes510 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 510 bytes, converted to bits)
            if (SpaceBits < 510 * 8)
                return false;

            // write the 510 bytes
            // note: could write in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return WriteByteBits(value.byte0000, 8) &&
                   WriteByteBits(value.byte0001, 8) &&
                   WriteByteBits(value.byte0002, 8) &&
                   WriteByteBits(value.byte0003, 8) &&
                   WriteByteBits(value.byte0004, 8) &&
                   WriteByteBits(value.byte0005, 8) &&
                   WriteByteBits(value.byte0006, 8) &&
                   WriteByteBits(value.byte0007, 8) &&
                   WriteByteBits(value.byte0008, 8) &&
                   WriteByteBits(value.byte0009, 8) &&
                   WriteByteBits(value.byte0010, 8) &&
                   WriteByteBits(value.byte0011, 8) &&
                   WriteByteBits(value.byte0012, 8) &&
                   WriteByteBits(value.byte0013, 8) &&
                   WriteBytes16(value.byte0014) &&
                   WriteBytes16(value.byte0030) &&
                   WriteBytes16(value.byte0046) &&
                   WriteBytes16(value.byte0062) &&
                   WriteBytes16(value.byte0078) &&
                   WriteBytes16(value.byte0094) &&
                   WriteBytes16(value.byte0110) &&
                   WriteBytes16(value.byte0126) &&
                   WriteBytes16(value.byte0142) &&
                   WriteBytes16(value.byte0158) &&
                   WriteBytes16(value.byte0174) &&
                   WriteBytes16(value.byte0190) &&
                   WriteBytes16(value.byte0206) &&
                   WriteBytes16(value.byte0222) &&
                   WriteBytes16(value.byte0238) &&
                   WriteBytes16(value.byte0254) &&
                   WriteBytes16(value.byte0270) &&
                   WriteBytes16(value.byte0286) &&
                   WriteBytes16(value.byte0302) &&
                   WriteBytes16(value.byte0318) &&
                   WriteBytes16(value.byte0334) &&
                   WriteBytes16(value.byte0350) &&
                   WriteBytes16(value.byte0366) &&
                   WriteBytes16(value.byte0382) &&
                   WriteBytes16(value.byte0398) &&
                   WriteBytes16(value.byte0414) &&
                   WriteBytes16(value.byte0430) &&
                   WriteBytes16(value.byte0446) &&
                   WriteBytes16(value.byte0462) &&
                   WriteBytes16(value.byte0478) &&
                   WriteBytes16(value.byte0494);
        }

        public bool WriteFixedString32(FixedString32 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 2 bytes for length + 'length' bytes, converted to bits)
            if (SpaceBits < (2 + value.Length) * 8)
                return false;

            // write LengthInBytes (ushort 2 bytes in FixedString),
            // and 'LengthInBytes' bytes
            if (WriteUShortBits((ushort)value.Length, 16))
            {
                for (int i = 0; i < value.Length; ++i)
                    if (!WriteByteBits(value[i], 8))
                        return false;
                return true;
            }
            return false;
        }

        public bool WriteFixedString64(FixedString64 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 2 bytes for length + 'length' bytes, converted to bits)
            if (SpaceBits < (2 + value.Length) * 8)
                return false;

            // write LengthInBytes (ushort 2 bytes in FixedString),
            // and 'LengthInBytes' bytes
            if (WriteUShortBits((ushort)value.Length, 16))
            {
                for (int i = 0; i < value.Length; ++i)
                    if (!WriteByteBits(value[i], 8))
                        return false;
                return true;
            }
            return false;
        }

        public bool WriteFixedString128(FixedString128 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 2 bytes for length + 'length' bytes, converted to bits)
            if (SpaceBits < (2 + value.Length) * 8)
                return false;

            // write LengthInBytes (ushort 2 bytes in FixedString),
            // and 'LengthInBytes' bytes
            if (WriteUShortBits((ushort)value.Length, 16))
            {
                for (int i = 0; i < value.Length; ++i)
                    if (!WriteByteBits(value[i], 8))
                        return false;
                return true;
            }
            return false;
        }

        public bool WriteFixedString512(FixedString512 value)
        {
            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 2 bytes for length + 'length' bytes, converted to bits)
            if (SpaceBits < (2 + value.Length) * 8)
                return false;

            // write LengthInBytes (ushort 2 bytes in FixedString),
            // and 'LengthInBytes' bytes
            if (WriteUShortBits((ushort)value.Length, 16))
            {
                for (int i = 0; i < value.Length; ++i)
                    if (!WriteByteBits(value[i], 8))
                        return false;
                return true;
            }
            return false;
        }
    }
}
