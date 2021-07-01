// NetworkMessage is an interface so that messages can be structs
// (in order to avoid allocations)
namespace DOTSNET
{
	// interfaces can't contain fields. need a separate static class.
	public static class NetworkMessageMeta
	{
		// systems need to check if messages contain enough bytes for ID header
		public const int IdSize = sizeof(byte);
	}

	// the NetworkMessage interface
	public interface NetworkMessage
	{
		// messages need an id. we assign it manually so that it's easier to
		// debug, instead of hashing the type name by default, which is hard to
		// debug.
		// => this makes it easier to communicate with external applications too
	    // => name hashing is still supported if needed, by returning the hash
	    // => byte to reduce bandwidth. 255 message are enough for every project
	    //    10k Bandwidth ushort->byte reduced from 2.10 MB/s to 2.00 MB/s!
		byte GetID();

		// OnSerialize serializes a message via BitWriter.
		// returns false if buffer was too small for all the data, or if it
		// contained invalid data (e.g. from an attacker).
		// => we need to let the user decide how to serialize. WriteBlittable
		//    isn't enough in all cases, e.g. arrays, compression, bit packing
		// => see also: gafferongames.com/post/reading_and_writing_packets/
		bool Serialize(ref BitWriter writer);

		// OnDeserialize deserializes a message via BitReader.
		// returns false if buffer was too small for all the data, or if it
		// contained invalid data (e.g. from an attacker).
		// => we need to let the user decide how to serialize. WriteBlittable
		//    isn't enough in all cases, e.g. arrays, compression, bit packing
		// => see also: gafferongames.com/post/reading_and_writing_packets/
		bool Deserialize(ref BitReader reader);
	}
}