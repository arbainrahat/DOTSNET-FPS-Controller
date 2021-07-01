// Bitpacking by vis2k for drastic bandwidth savings.
// See also: https://gafferongames.com/post/reading_and_writing_packets/
//           https://gafferongames.com/post/serialization_strategies/
//
// Why Bitpacking:
// + huge bandwidth savings possible
// + word aligned copying to buffer is fast
// + always little endian, no worrying about byte order on different systems
//
// BitReader is ATOMIC! either all is read successfully, or nothing is.
//
// BitReader does aligned reads, but still supports buffer sizes that are NOT
// multiple of 4. This is necessary because we might only read a 3 bytes message
// from a socket.
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace DOTSNET
{
    public struct BitReader
    {
        // scratch bits are twice the size of maximum allowed write size.
        // maximum is 32 bit, so we need 64 bit scratch for cases where
        // scratch is filled by 10 bits, and we writer another 32 into it.
        ulong scratch;
        int scratchBits;
        int wordIndex;
        ArraySegment<byte> buffer;

        // calculate remaining data to read, in bits
        // how many bits can we read:
        // -> scratchBits + what's left in buffer * 8 (= to bits)
        public int RemainingBits =>
            scratchBits + ((buffer.Count - wordIndex) * 8);

        // position is useful sometimes. read-only for now.
        // => wordIndex converted to bits - scratch remaining
        public int BitPosition => wordIndex * 8 - scratchBits;

        public BitReader(ArraySegment<byte> buffer)
        {
            this.buffer = buffer;
            scratch = 0;
            scratchBits = 0;
            wordIndex = 0;
        }

        // helpers /////////////////////////////////////////////////////////////
        // helper function to copy 32 bits from buffer to scratch.
        unsafe void Copy32BufferBitsToScratch()
        {
            // bitpacking works on aligned writes, but we want to support buffer
            // sizes that aren't multiple of 4 as well. it's more convenient.
            // => we want to copy 4 buffer bytes to scratch, or fewer if not
            //    enough to read.
            int remaining = buffer.Count - wordIndex;
            int copy = Math.Min(4, remaining);

            // since we don't always copy exactly 4 bytes, we can't use the
            // uint* pointer assignment trick unless we want to have a special
            // case for '4 aligned' and 'not 4 aligned'. the most simple
            // solution is to use MemCopy with the variable amount of bytes.
            // => it's usually 4
            // => except for the last read which is 1,2,3 if buffer sizes is not
            //    a multiple of 4.
            uint word;
            fixed (byte* ptr = &buffer.Array[buffer.Offset + wordIndex])
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
                    word = reinterpeted[0];
                }
                // otherwise use the safe method to only copy what is left.
                // Unity doesn't have Buffer.MemoryCopy yet, so we need an #ifdef
                // note: we could use a for loop too, but that's slower.
                else
                {
                    byte* wordPtr = (byte*)&word;
#if UNITY_2017_1_OR_NEWER
                    UnsafeUtility.MemCpy(wordPtr, ptr, copy);
#else
                    Buffer.MemoryCopy(ptr, wordPtr, copy);
#endif
                }
            }

            // fast copy word into buffer at word position
            // we use little endian order so that flushing
            // 0x2211 then 0x4433 becomes 0x11223344 in buffer
            // -> need to inverse if not on little endian
            // -> we do this with the uint, not with the buffer, so it's all
            //    still word aligned and fast!
            if (!BitConverter.IsLittleEndian)
            {
                word = BitWriter.SwapBytes(word);
            }
            //UnityEngine.Debug.Log("word: 0x" + word.ToString("X8"));

            // move the extracted part into scratch at scratch position
            // so for scratch 0x000000FF it becomes 0xDDDDDDFF
            ulong shifted = (ulong)word << scratchBits;
            scratch |= shifted;
            //UnityEngine.Debug.Log("shifted: 0x" + shifted.ToString("X16"));
            //UnityEngine.Debug.Log("scratch: 0x" + scratch.ToString("X16"));

            // update word index and scratch bits
            // usually +4 bytes and +32 bits.
            // unless we only had < 4 bytes left to copy in the buffer
            wordIndex += copy;
            scratchBits += copy * 8; // to bits
        }

        // ReadBITS ////////////////////////////////////////////////////////////
        // read 'n' bits of an uint
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
        // => named ReadUIntBITS so it's obvious that it's bits, not range!
        // => parameters as result, bits for consistency with WriteUIntBits!
        public bool ReadUIntBits(out uint value, int bits)
        {
            value = 0;

            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // => throws exception because the developer should fix it immediately
            //
            // NOTE: we allow 0 bits so that ReadULongBits is easier to
            //       implement where we simply do two ReadUInts and the second
            //       one might pass '0' for bits but still succeeds.
            if (bits < 0 || bits > 32)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadUIntBits)} supports between 0 and 32 bits");

            // 0 bits is valid, simply do nothing
            if (bits == 0)
                return true;

            // make sure there are enough remaining in buffer
            if (RemainingBits < bits)
                return false;

            // copy another word from buffer if we don't have enough scratch bits
            if (scratchBits < bits)
            {
                Copy32BufferBitsToScratch();
            }

            // create the mask
            // for example, for 8 bits it is 0x000000FF
            // need ulong so we can shift left 32 for 32 bits
            // (would be too much for uint)
            ulong mask = (1ul << bits) - 1;
            //UnityEngine.Debug.Log("mask: 0x" + mask.ToString("X"));

            // extract the 'n' bits out of scratch
            // need ulong so we can shift left far enough in the step below!
            // so for 0xAABBCCDD if we extract 8 bits we extract the last 0xDD
            value = (uint)(scratch & mask);
            //UnityEngine.Debug.Log("extracted: 0x" + value.ToString("X"));

            // shift scratch to the right by 'n' bits
            scratch >>= bits;

            // subtract 'n' from scratchBits
            scratchBits -= bits;

            // done
            return true;
        }

        // peek is sometimes useful for atomic reads.
        // note: wordIndex may have been modified if we needed to copy to
        //       scratch, but the overall RemainingBits are still the same
        public bool PeekUIntBits(out uint value, int bits)
        {
            // reuse ReadUIntBits so we only have the complex code in 1 function
            int wordIndexBackup = wordIndex;
            int scratchBitsBackup = scratchBits;
            ulong scratchBackup = scratch;

            bool result = ReadUIntBits(out value, bits);

            wordIndex = wordIndexBackup;
            scratchBits = scratchBitsBackup;
            scratch = scratchBackup;

            return result;
        }

        // read ulong as two uints
        // bits can be between 0 and 64.
        public bool ReadULongBits(out ulong value, int bits)
        {
            value = 0;

            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // => throws exception because the developer should fix it immediately
            //
            // NOTE: we allow 0 bits just like all other functions
            if (bits < 0 || bits > 64)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadULongBits)} supports between 0 and 64 bits");

            // 0 bits is valid, simply do nothing
            if (bits == 0)
                return true;

            // make sure there is enough remaining in buffer
            // => we do two ReadUInts below. checking size first makes it atomic!
            if (RemainingBits < bits)
                return false;

            // read both halves as uint
            // => first one up to 32 bits
            // => second one the remainder. ReadUInt does nothing if bits is 0.
            int lowerBits = Math.Min(bits, 32);
            int upperBits = Math.Max(bits - 32, 0);
            if (ReadUIntBits(out uint lower, lowerBits) &&
                ReadUIntBits(out uint upper, upperBits))
            {
                value = (ulong)upper << 32;
                value |= lower;
                return true;
            }
            return false;
        }

        // read 'n' bits of an ushort
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
        // reuses ReadUInt for now. inline if needed.
        public bool ReadUShortBits(out ushort value, int bits)
        {
            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // => throws exception because the developer should fix it immediately
            if (bits < 0 || bits > 16)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadUShortBits)} supports between 0 and 16 bits");

            bool result = ReadUIntBits(out uint temp, bits);
            value = (ushort)temp;
            return result;
        }

        // read 'n' bits of a byte
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
        // reuses ReadUInt for now. inline if needed.
        public bool ReadByteBits(out byte value, int bits)
        {
            // make sure user passed valid amount of bits.
            // anything else was by accident, so throw an exception.
            // => throws exception because the developer should fix it immediately
            if (bits < 0 || bits > 8)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadByteBits)} supports between 0 and 8 bits");

            bool result = ReadUIntBits(out uint temp, bits);
            value = (byte)temp;
            return result;
        }

        // read 1 bit as bool
        // reuses ReadUInt for now. inline if needed.
        public bool ReadBool(out bool value)
        {
            bool result = ReadUIntBits(out uint temp, 1);
            value = temp != 0;
            return result;
        }

        // Read RANGE //////////////////////////////////////////////////////////
        // ReadUInt within a known range, packed into minimum amount of bits.
        public bool ReadUInt(out uint value, uint min = uint.MinValue, uint max = uint.MaxValue)
        {
            value = 0;

            // make sure the range is valid
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadUInt)} min={min} needs to be <= max={max}");

            // calculate bits required for value range
            int bits = BitWriter.BitsRequired(min, max);

            // read the normalized value for that range
            // for example
            //   originally written value was '5' for range [2..9]
            //   normalized range when was max-min => [0..7]
            //   normalized value was value-min = '3'
            //   we read the normalized value '3'
            //   and add min back to it => '5'
            if (ReadUIntBits(out uint normalized, bits))
            {
                value = normalized + min;
                return true;
            }
            return false;
        }

        // ReadInt within a known range, packed into minimum amount of bits
        // by shifting to uint (which doesn't need the high order bit)
        public bool ReadInt(out int value, int min = int.MinValue, int max = int.MaxValue)
        {
            value = 0;

            // make sure the range is valid
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadInt)} min={min} needs to be <= max={max}");

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
            int bits = BitWriter.BitsRequired(0, (uint)(max - min));

            // read the normalized value for that range. need to add min.
            if (ReadUIntBits(out uint normalized, bits))
            {
                value = (int)(normalized + min);
                return true;
            }
            return false;
        }

        // ReadULong within a known range, packed into minimum amount of bits.
        public bool ReadULong(out ulong value, ulong min = ulong.MinValue, ulong max = ulong.MaxValue)
        {
            value = 0;

            // make sure the range is valid
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadULong)} min={min} needs to be <= max={max}");

            // calculate bits required for value range
            int bits = BitWriter.BitsRequired(min, max);

            // read the normalized value for that range
            // for example
            //   originally written value was '5' for range [2..9]
            //   normalized range when was max-min => [0..7]
            //   normalized value was value-min = '3'
            //   we read the normalized value '3'
            //   and add min back to it => '5'
            if (ReadULongBits(out ulong normalized, bits))
            {
                value = normalized + min;
                return true;
            }
            return false;
        }

        // ReadLong within a known range, packed into minimum amount of bits
        // by shifting to uint (which doesn't need the high order bit)
        public bool ReadLong(out long value, long min = long.MinValue, long max = long.MaxValue)
        {
            value = 0;

            // make sure the range is valid
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadLong)} min={min} needs to be <= max={max}");

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
            int bits = BitWriter.BitsRequired(0, (ulong)(max - min));

            // read the normalized value for that range. need to add min.
            if (ReadULongBits(out ulong normalized, bits))
            {
                value = (long)normalized + min;
                return true;
            }
            return false;
        }

        // ReadUShort within a known range, packed into minimum amount of bits.
        public bool ReadUShort(out ushort value, ushort min = ushort.MinValue, ushort max = ushort.MaxValue)
        {
            value = 0;

            // make sure the range is valid
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadUShort)} min={min} needs to be <= max={max}");

            // calculate bits required for value range
            int bits = BitWriter.BitsRequired(min, max);

            // read the normalized value for that range
            // for example
            //   originally written value was '5' for range [2..9]
            //   normalized range when was max-min => [0..7]
            //   normalized value was value-min = '3'
            //   we read the normalized value '3'
            //   and add min back to it => '5'
            if (ReadUShortBits(out ushort normalized, bits))
            {
                value = (ushort)(normalized + min);
                return true;
            }
            return false;
        }

        // ReadShort within a known range, packed into minimum amount of bits
        // by shifting to uint (which doesn't need the high order bit)
        public bool ReadShort(out short value, short min = short.MinValue, short max = short.MaxValue)
        {
            value = 0;

            // make sure the range is valid
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadShort)} min={min} needs to be <= max={max}");

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
            int bits = BitWriter.BitsRequired(0, (ushort)(max - min));

            // read the normalized value for that range. need to add min.
            if (ReadUShortBits(out ushort normalized, bits))
            {
                value = (short)(normalized + min);
                return true;
            }
            return false;
        }

        // ReadByte within a known range, packed into minimum amount of bits.
        public bool ReadByte(out byte value, byte min = byte.MinValue, byte max = byte.MaxValue)
        {
            value = 0;

            // make sure the range is valid
            // => throws exception because the developer should fix it immediately
            if (min > max)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadByte)} min={min} needs to be <= max={max}");

            // calculate bits required for value range
            int bits = BitWriter.BitsRequired(min, max);

            // read the normalized value for that range
            // for example
            //   originally written value was '5' for range [2..9]
            //   normalized range when was max-min => [0..7]
            //   normalized value was value-min = '3'
            //   we read the normalized value '3'
            //   and add min back to it => '5'
            if (ReadByteBits(out byte normalized, bits))
            {
                value = (byte)(normalized + min);
                return true;
            }
            return false;
        }

        // Read Uncompressed ///////////////////////////////////////////////////
        // read bytes into a passed byte[] to avoid allocations
        // note: BitReader can't read ArraySegments because we use scratch.
        public bool ReadBytes(byte[] bytes, int size)
        {
            // make sure size is valid
            // => throws exception because the developer should fix it immediately
            if (size < 0 || size > bytes.Length)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadBytes)} size {size} needs to be between 0 and {bytes.Length}");

            // make sure there is enough remaining in scratch + buffer
            if (RemainingBits < size * 8)
                return false;

            // size = 0 is valid, simply do nothing
            if (size == 0)
                return true;

            // simply reuse ReadByte for now.
            // => copy in up to 32 bit chunks later for performance!
            // (copying scratch then memcpy bytes would insert placeholders if
            //  scratch isn't a multiple of 8 bits)
            for (int i = 0; i < size; ++i)
            {
                if (!ReadByteBits(out bytes[i], 8))
                    return false;
            }
            return true;
        }

        // read a byte[] into a passed byte[] to avoid allocations
        // => with size in BITS, not BYTES
        // => writer has WriteBytesBitSize too, so this is the counter-part for
        //    reading without any filler bits.
        public bool ReadBytesBitSize(byte[] bytes, int sizeInBits)
        {
            // make sure size is valid
            // => throws exception because the developer should fix it immediately
            if (sizeInBits < 0 || sizeInBits > bytes.Length * 8)
                throw new ArgumentOutOfRangeException($"BitReader {nameof(ReadBytesBitSize)} sizeInBits {sizeInBits} needs to be between 0 and {bytes.Length} * 8");

            // make sure there is enough remaining in scratch + buffer
            if (RemainingBits < sizeInBits)
                return false;

            // size = 0 is valid, simply do nothing
            if (sizeInBits == 0)
                return true;

            // simply reuse ReadByte for now.
            // => copy in up to 32 bit chunks later for performance!
            // (copying scratch then memcpy bytes would insert placeholders if
            //  scratch isn't a multiple of 8 bits)
            //
            // size is in bits, so / 8 to get amount of FULL BYTES we can read
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
            {
                if (!ReadByteBits(out bytes[i], 8))
                    return false;
            }

            // now read the final partial byte (missing bits) if any
            int missingBits = sizeInBits - (fullBytes * 8);
            if (missingBits > 0)
            {
                //UnityEngine.Debug.Log("reading " + missingBits + " bits"));
                if (!ReadByteBits(out bytes[fullBytes], missingBits))
                    return false;
            }

            return true;
        }

        // read 32 bits uncompressed float
        // uses FloatUInt like in the article
        public bool ReadFloat(out float value)
        {
            bool result = ReadUIntBits(out uint temp, 32);
            value = new BitWriter.FloatUInt{uintValue = temp}.floatValue;
            return result;
        }

        // read compressed float with given range and precision.
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
        public bool ReadFloat(out float value, float min, float max, float precision)
        {
            value = 0;

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
            float minScaled = min / precision;
            float maxScaled = max / precision;

            // check bounds before converting to int
            if (minScaled < int.MinValue || minScaled > int.MaxValue ||
                maxScaled < int.MinValue || maxScaled > int.MaxValue)
                return false;

            // Convert.ToInt32 so we don't need to depend on Unity.Mathf!
            int minRounded = Convert.ToInt32(minScaled);
            int maxRounded = Convert.ToInt32(maxScaled);

            // read the scaled value
            if (ReadInt(out int temp, minRounded, maxRounded))
            {
                // scale back
                value = temp * precision;
                return true;
            }
            return false;
        }

        // read compressed double with given range and precision.
        // see also: https://gafferongames.com/post/serialization_strategies/
        //
        // for example:
        //   value = 12.3 in range [0..100]
        //   precision = 0.1
        //   we divide by precision. or in other words, for 0.1 we multiply by 10
        //     => value = 123 in range [0..1000] (rounded to long)
        //     => fits into 10 bits (0..1023) instead of 64 bits
        //
        // to avoid exploits, it returns false if int overflows would happen.
        public bool ReadDouble(out double value, double min, double max, double precision)
        {
            value = 0;

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
            double minScaled = min / precision;
            double maxScaled = max / precision;

            // check bounds before converting to long
            if (minScaled < long.MinValue || minScaled > long.MaxValue ||
                maxScaled < long.MinValue || maxScaled > long.MaxValue)
                return false;

            // Convert.ToInt64 so we don't need to depend on Unity.Mathf!
            long minRounded = Convert.ToInt64(minScaled);
            long maxRounded = Convert.ToInt64(maxScaled);

            // read the scaled value
            if (ReadLong(out long temp, minRounded, maxRounded))
            {
                // scale back
                value = temp * precision;
                return true;
            }
            return false;
        }

        // read 64 bits uncompressed double
        // uses DoubleULong like in the article
        public bool ReadDouble(out double value)
        {
            bool result = ReadULongBits(out ulong temp, 64);
            value = new BitWriter.DoubleULong{ulongValue = temp}.doubleValue;
            return result;
        }

        // ECS types ///////////////////////////////////////////////////////////
        // read quaternion, uncompressed
        public bool ReadQuaternion(out quaternion value)
        {
            value = default;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 16 bytes, converted to bits)
            if (RemainingBits < 16 * 8)
                return false;

            // read 4 floats
            return ReadFloat(out value.value.x) &&
                   ReadFloat(out value.value.y) &&
                   ReadFloat(out value.value.z) &&
                   ReadFloat(out value.value.w);
        }

        // read quaternion with smallest-three compression
        // see also: https://gafferongames.com/post/snapshot_compression/
        //
        // reuses our smallest three compression for quaternion->uint 32 bit.
        // maybe make this 29 bits later.
        //
        // note: normalizes when decompressing.
        public bool ReadQuaternionSmallestThree(out quaternion value)
        {
            value = default;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (our compression uses 32 bit. maybe use 29 bit later.)
            if (RemainingBits < 32)
                return false;

            // read and decompress
            if (ReadUIntBits(out uint compressed, 32))
            {
                value = Compression.DecompressQuaternion(compressed);
                return true;
            }
            return false;
        }

        public bool ReadBytes16(out Bytes16 value)
        {
            value = default;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 16 bytes, converted to bits)
            if (RemainingBits < 16 * 8)
                return false;

            // read the 16 bytes
            // note: could read in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return ReadByteBits(out value.byte0000, 8) &&
                   ReadByteBits(out value.byte0001, 8) &&
                   ReadByteBits(out value.byte0002, 8) &&
                   ReadByteBits(out value.byte0003, 8) &&
                   ReadByteBits(out value.byte0004, 8) &&
                   ReadByteBits(out value.byte0005, 8) &&
                   ReadByteBits(out value.byte0006, 8) &&
                   ReadByteBits(out value.byte0007, 8) &&
                   ReadByteBits(out value.byte0008, 8) &&
                   ReadByteBits(out value.byte0009, 8) &&
                   ReadByteBits(out value.byte0010, 8) &&
                   ReadByteBits(out value.byte0011, 8) &&
                   ReadByteBits(out value.byte0012, 8) &&
                   ReadByteBits(out value.byte0013, 8) &&
                   ReadByteBits(out value.byte0014, 8) &&
                   ReadByteBits(out value.byte0015, 8);
        }

        public bool ReadBytes30(out Bytes30 value)
        {
            value = default;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 30 bytes, converted to bits)
            if (RemainingBits < 30 * 8)
                return false;

            // read the 30 bytes
            // note: could read in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return ReadByteBits(out value.byte0000, 8) &&
                   ReadByteBits(out value.byte0001, 8) &&
                   ReadByteBits(out value.byte0002, 8) &&
                   ReadByteBits(out value.byte0003, 8) &&
                   ReadByteBits(out value.byte0004, 8) &&
                   ReadByteBits(out value.byte0005, 8) &&
                   ReadByteBits(out value.byte0006, 8) &&
                   ReadByteBits(out value.byte0007, 8) &&
                   ReadByteBits(out value.byte0008, 8) &&
                   ReadByteBits(out value.byte0009, 8) &&
                   ReadByteBits(out value.byte0010, 8) &&
                   ReadByteBits(out value.byte0011, 8) &&
                   ReadByteBits(out value.byte0012, 8) &&
                   ReadByteBits(out value.byte0013, 8) &&
                   ReadBytes16(out value.byte0014);
        }

        public bool ReadBytes62(out Bytes62 value)
        {
            value = default;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 62 bytes, converted to bits)
            if (RemainingBits < 62 * 8)
                return false;

            // read the 62 bytes
            // note: could read in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return ReadByteBits(out value.byte0000, 8) &&
                   ReadByteBits(out value.byte0001, 8) &&
                   ReadByteBits(out value.byte0002, 8) &&
                   ReadByteBits(out value.byte0003, 8) &&
                   ReadByteBits(out value.byte0004, 8) &&
                   ReadByteBits(out value.byte0005, 8) &&
                   ReadByteBits(out value.byte0006, 8) &&
                   ReadByteBits(out value.byte0007, 8) &&
                   ReadByteBits(out value.byte0008, 8) &&
                   ReadByteBits(out value.byte0009, 8) &&
                   ReadByteBits(out value.byte0010, 8) &&
                   ReadByteBits(out value.byte0011, 8) &&
                   ReadByteBits(out value.byte0012, 8) &&
                   ReadByteBits(out value.byte0013, 8) &&
                   ReadBytes16(out value.byte0014) &&
                   ReadBytes16(out value.byte0030) &&
                   ReadBytes16(out value.byte0046);
        }

        public bool ReadBytes126(out Bytes126 value)
        {
            value = default;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 126 bytes, converted to bits)
            if (RemainingBits < 126 * 8)
                return false;

            // read the 126 bytes
            // note: could read in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return ReadByteBits(out value.byte0000, 8) &&
                   ReadByteBits(out value.byte0001, 8) &&
                   ReadByteBits(out value.byte0002, 8) &&
                   ReadByteBits(out value.byte0003, 8) &&
                   ReadByteBits(out value.byte0004, 8) &&
                   ReadByteBits(out value.byte0005, 8) &&
                   ReadByteBits(out value.byte0006, 8) &&
                   ReadByteBits(out value.byte0007, 8) &&
                   ReadByteBits(out value.byte0008, 8) &&
                   ReadByteBits(out value.byte0009, 8) &&
                   ReadByteBits(out value.byte0010, 8) &&
                   ReadByteBits(out value.byte0011, 8) &&
                   ReadByteBits(out value.byte0012, 8) &&
                   ReadByteBits(out value.byte0013, 8) &&
                   ReadBytes16(out value.byte0014) &&
                   ReadBytes16(out value.byte0030) &&
                   ReadBytes16(out value.byte0046) &&
                   ReadBytes16(out value.byte0062) &&
                   ReadBytes16(out value.byte0078) &&
                   ReadBytes16(out value.byte0094) &&
                   ReadBytes16(out value.byte0110);
        }

        public bool ReadBytes510(out Bytes510 value)
        {
            value = default;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // (we need 510 bytes, converted to bits)
            if (RemainingBits < 510 * 8)
                return false;

            // read the 510 bytes
            // note: could read in 32 bit uint chunks if we shift+or 4 at a
            //       time into uint
            return ReadByteBits(out value.byte0000, 8) &&
                   ReadByteBits(out value.byte0001, 8) &&
                   ReadByteBits(out value.byte0002, 8) &&
                   ReadByteBits(out value.byte0003, 8) &&
                   ReadByteBits(out value.byte0004, 8) &&
                   ReadByteBits(out value.byte0005, 8) &&
                   ReadByteBits(out value.byte0006, 8) &&
                   ReadByteBits(out value.byte0007, 8) &&
                   ReadByteBits(out value.byte0008, 8) &&
                   ReadByteBits(out value.byte0009, 8) &&
                   ReadByteBits(out value.byte0010, 8) &&
                   ReadByteBits(out value.byte0011, 8) &&
                   ReadByteBits(out value.byte0012, 8) &&
                   ReadByteBits(out value.byte0013, 8) &&
                   ReadBytes16(out value.byte0014) &&
                   ReadBytes16(out value.byte0030) &&
                   ReadBytes16(out value.byte0046) &&
                   ReadBytes16(out value.byte0062) &&
                   ReadBytes16(out value.byte0078) &&
                   ReadBytes16(out value.byte0094) &&
                   ReadBytes16(out value.byte0110) &&
                   ReadBytes16(out value.byte0126) &&
                   ReadBytes16(out value.byte0142) &&
                   ReadBytes16(out value.byte0158) &&
                   ReadBytes16(out value.byte0174) &&
                   ReadBytes16(out value.byte0190) &&
                   ReadBytes16(out value.byte0206) &&
                   ReadBytes16(out value.byte0222) &&
                   ReadBytes16(out value.byte0238) &&
                   ReadBytes16(out value.byte0254) &&
                   ReadBytes16(out value.byte0270) &&
                   ReadBytes16(out value.byte0286) &&
                   ReadBytes16(out value.byte0302) &&
                   ReadBytes16(out value.byte0318) &&
                   ReadBytes16(out value.byte0334) &&
                   ReadBytes16(out value.byte0350) &&
                   ReadBytes16(out value.byte0366) &&
                   ReadBytes16(out value.byte0382) &&
                   ReadBytes16(out value.byte0398) &&
                   ReadBytes16(out value.byte0414) &&
                   ReadBytes16(out value.byte0430) &&
                   ReadBytes16(out value.byte0446) &&
                   ReadBytes16(out value.byte0462) &&
                   ReadBytes16(out value.byte0478) &&
                   ReadBytes16(out value.byte0494);
        }

        public bool ReadFixedString32(out FixedString32 value)
        {
            value = default;

            // peek 2 bytes length first.
            // we need it to be atomic, so we peek, then check total size before
            // reading anything
            if (!PeekUIntBits(out uint length, 16))
                return false;

            // length should be in a valid range.
            // FixedString32 has 2 bytes for length, 29 bytes for content.
            // we read an unsigned, so need to check <0!
            if (length > FixedString32.UTF8MaxLengthInBytes)
                return false;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // we need 2 bytes for length + length bytes for content, in bits
            if (RemainingBits < (2 + length) * 8)
                return false;

            // skip the 2 bytes content that we already peeked
            ReadUIntBits(out uint _, 16);

            // read the content bytes
            value = new FixedString32();
            value.Length = (ushort)length;
            for (int i = 0; i < length; ++i)
            {
                if (ReadByteBits(out byte element, 8))
                {
                    value[i] = element;
                }
                else return false;
            }
            return true;
        }

        public bool ReadFixedString64(out FixedString64 value)
        {
            value = default;

            // peek 2 bytes length first.
            // we need it to be atomic, so we peek, then check total size before
            // reading anything
            if (!PeekUIntBits(out uint length, 16))
                return false;

            // length should be in a valid range.
            // FixedString64 has 2 bytes for length, 61 bytes for content.
            // we read an unsigned, so need to check <0!
            if (length > FixedString64.UTF8MaxLengthInBytes)
                return false;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // we need 2 bytes for length + length bytes for content, in bits
            if (RemainingBits < (2 + length) * 8)
                return false;

            // skip the 2 bytes content that we already peeked
            ReadUIntBits(out uint _, 16);

            // read the content bytes
            value = new FixedString64();
            value.Length = (ushort)length;
            for (int i = 0; i < length; ++i)
            {
                if (ReadByteBits(out byte element, 8))
                {
                    value[i] = element;
                }
                else return false;
            }
            return true;
        }

        public bool ReadFixedString128(out FixedString128 value)
        {
            value = default;

            // peek 2 bytes length first.
            // we need it to be atomic, so we peek, then check total size before
            // reading anything
            if (!PeekUIntBits(out uint length, 16))
                return false;

            // length should be in a valid range.
            // FixedString128 has 2 bytes for length, 125 bytes for content.
            // we read an unsigned, so need to check <0!
            if (length > FixedString128.UTF8MaxLengthInBytes)
                return false;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // we need 2 bytes for length + length bytes for content, in bits
            if (RemainingBits < (2 + length) * 8)
                return false;

            // skip the 2 bytes content that we already peeked
            ReadUIntBits(out uint _, 16);

            // read the content bytes
            value = new FixedString128();
            value.Length = (ushort)length;
            for (int i = 0; i < length; ++i)
            {
                if (ReadByteBits(out byte element, 8))
                {
                    value[i] = element;
                }
                else return false;
            }
            return true;
        }

        public bool ReadFixedString512(out FixedString512 value)
        {
            value = default;

            // peek 2 bytes length first.
            // we need it to be atomic, so we peek, then check total size before
            // reading anything
            if (!PeekUIntBits(out uint length, 16))
                return false;

            // length should be in a valid range.
            // FixedString512 has 2 bytes for length, 509 bytes for content.
            // we read an unsigned, so need to check <0!
            if (length > FixedString512.UTF8MaxLengthInBytes)
                return false;

            // make sure there is enough space in scratch + buffer.
            // the write should be atomic.
            // we need 2 bytes for length + length bytes for content, in bits
            if (RemainingBits < (2 + length) * 8)
                return false;

            // skip the 2 bytes content that we already peeked
            ReadUIntBits(out uint _, 16);

            // read the content bytes
            value = new FixedString512();
            value.Length = (ushort)length;
            for (int i = 0; i < length; ++i)
            {
                if (ReadByteBits(out byte element, 8))
                {
                    value[i] = element;
                }
                else return false;
            }
            return true;
        }
    }
}
