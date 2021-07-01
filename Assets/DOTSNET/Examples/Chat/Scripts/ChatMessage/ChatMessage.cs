using Unity.Collections;

namespace DOTSNET.Examples.Chat
{
    public struct ChatMessage : NetworkMessage
    {
        public FixedString32 sender;
        public FixedString128 text;

        public byte GetID() => 0x33;

        public ChatMessage(FixedString32 sender, FixedString128 text)
        {
            this.sender = sender;
            this.text = text;
        }

        public bool Serialize(ref BitWriter writer) =>
             writer.WriteFixedString32(sender) &&
             writer.WriteFixedString128(text);

        public bool Deserialize(ref BitReader reader) =>
            reader.ReadFixedString32(out sender) &&
            reader.ReadFixedString128(out text);
    }
}