// A message that synchronizes a NetworkEntity's position+rotation (=transform)
using Unity.Mathematics;

namespace DOTSNET
{
    public struct TransformMessage : NetworkMessage
    {
        // client needs to identify the entity by netId
        public ulong netId;

        // position & rotation
        public float3 position;
        public quaternion rotation;

        public byte GetID() => 0x25;

        public TransformMessage(ulong netId, float3 position, quaternion rotation)
        {
            this.netId = netId;
            this.position = position;
            this.rotation = rotation;
        }

        public bool Serialize(ref BitWriter writer) =>
            // rotation is compressed from 16 bytes quaternion into 4 bytes
            //   100,000 messages * 16 byte = 1562 KB
            //   100,000 messages *  4 byte =  391 KB
            // => DOTSNET is bandwidth limited, so this is a great idea.
            writer.WriteULong(netId) &&
            writer.WriteFloat(position.x) &&
            writer.WriteFloat(position.y) &&
            writer.WriteFloat(position.z) &&
            writer.WriteQuaternionSmallestThree(rotation);

        public bool Deserialize(ref BitReader reader) =>
            reader.ReadULong(out netId) &&
            reader.ReadFloat(out position.x) &&
            reader.ReadFloat(out position.y) &&
            reader.ReadFloat(out position.z) &&
            reader.ReadQuaternionSmallestThree(out rotation);
    }
}
