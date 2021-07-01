// A message that lets the NetworkClient know that an Entity needs to be
// unspawned.
namespace DOTSNET
{
    public struct UnspawnMessage : NetworkMessage
    {
        // client needs to identify the entity by netId
        public ulong netId;

        public byte GetID() => 0x23;

        public UnspawnMessage(ulong netId)
        {
            this.netId = netId;
        }

        public bool Serialize(ref BitWriter writer) =>
            writer.WriteULong(netId);

        public bool Deserialize(ref BitReader reader) =>
            reader.ReadULong(out netId);
    }
}
