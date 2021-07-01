// A message that sends the full state of a NetworkEntity to the client in order
// to spawn it.
using Unity.Collections;
using Unity.Mathematics;

namespace DOTSNET
{
    public struct SpawnMessage : NetworkMessage
    {
        // client needs to know which prefab to spawn
        public Bytes16 prefabId;

        // client needs to know which netId was assigned to this entity
        public ulong netId;

        // flag to indicate if the connection that we send it to owns the entity
        public bool owned;

        // the spawn position
        // unlike StateMessage, we include the position once when spawning so
        // that even without a NetworkTransform system, it's still positioned
        // correctly when spawning.
        public float3 position;

        // the spawn rotation
        // unlike StateMessage, we include the rotation once when spawning so
        // that even without a NetworkTransform system, it's still rotated
        // correctly when spawning.
        public quaternion rotation;

        public byte GetID() => 0x22;

        public SpawnMessage(Bytes16 prefabId, ulong netId, bool owned, float3 position, quaternion rotation)
        {
            this.prefabId = prefabId;
            this.netId = netId;
            this.owned = owned;
            this.position = position;
            this.rotation = rotation;
        }

        public bool Serialize(ref BitWriter writer) =>
            // rotation is compressed from 16 bytes quaternion into 4 bytes
            //   100,000 messages * 16 byte = 1562 KB
            //   100,000 messages *  4 byte =  391 KB
            // => DOTSNET is bandwidth limited, so this is a great idea.
            writer.WriteBytes16(prefabId) &&
            writer.WriteULong(netId) &&
            writer.WriteBool(owned) &&
            writer.WriteFloat(position.x) &&
            writer.WriteFloat(position.y) &&
            writer.WriteFloat(position.z) &&
            writer.WriteQuaternionSmallestThree(rotation);

        public bool Deserialize(ref BitReader reader) =>
            reader.ReadBytes16(out prefabId) &&
            reader.ReadULong(out netId) &&
            reader.ReadBool(out owned) &&
            reader.ReadFloat(out position.x) &&
            reader.ReadFloat(out position.y) &&
            reader.ReadFloat(out position.z) &&
            reader.ReadQuaternionSmallestThree(out rotation);
    }
}
