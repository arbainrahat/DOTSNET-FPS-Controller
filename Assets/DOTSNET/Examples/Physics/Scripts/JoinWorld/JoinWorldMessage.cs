using Unity.Collections;

namespace DOTSNET.Examples.Physics
{
    public struct JoinWorldMessage : NetworkMessage
    {
        public Bytes16 playerPrefabId;

        public byte GetID() => 0x31;

        public JoinWorldMessage(Bytes16 playerPrefabId)
        {
            this.playerPrefabId = playerPrefabId;
        }

        public bool Serialize(ref BitWriter writer) =>
            writer.WriteBytes16(playerPrefabId);

        public bool Deserialize(ref BitReader reader) =>
            reader.ReadBytes16(out playerPrefabId);
    }
}