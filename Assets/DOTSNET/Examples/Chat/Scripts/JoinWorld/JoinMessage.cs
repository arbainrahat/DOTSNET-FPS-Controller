using Unity.Collections;

namespace DOTSNET.Examples.Chat
{
    public struct JoinMessage : NetworkMessage
    {
        public FixedString32 name;

        public byte GetID() => 0x31;

        public JoinMessage(FixedString32 name)
        {
            this.name = name;
        }

        public bool Serialize(ref BitWriter writer) =>
            writer.WriteFixedString32(name);

        public bool Deserialize(ref BitReader reader) =>
            reader.ReadFixedString32(out name);
    }
}