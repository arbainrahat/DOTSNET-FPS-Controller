namespace DOTSNET.Examples.Chat
{
    public struct JoinedMessage : NetworkMessage
    {
        public byte GetID() => 0x32;
        public bool Serialize(ref BitWriter writer) => true;
        public bool Deserialize(ref BitReader reader) => true;
    }
}