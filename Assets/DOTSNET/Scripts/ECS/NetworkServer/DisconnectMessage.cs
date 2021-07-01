// DisconnectMessage is an artificial message.
// It is never sent over the network. It is only used to register a handler.
namespace DOTSNET
{
    public struct DisconnectMessage : NetworkMessage
    {
        public byte GetID() => 0x02;
        public bool Serialize(ref BitWriter writer) => true;
        public bool Deserialize(ref BitReader reader) => true;
    }
}