using System;
using UnityEngine;

namespace DOTSNET.TelepathyTransport
{
    public class TelepathyTransportServerAuthoring : MonoBehaviour, SelectiveSystemAuthoring
    {
        // find NetworkServerSystem in ECS world
        TelepathyTransportServerSystem server =>
            Bootstrap.ServerWorld.GetExistingSystem<TelepathyTransportServerSystem>();

        public ushort Port = 7777;

        [Tooltip("Nagle Algorithm can be disabled by enabling NoDelay")]
        public bool NoDelay = true;

        [Tooltip("Send timeout in milliseconds.")]
        public int SendTimeout = 5000;

        [Tooltip("Receive timeout in milliseconds.")]
        public int ReceiveTimeout = 5000;

        [Tooltip("Protect against allocation attacks by keeping the max message size small. Otherwise an attacker host might send multiple fake packets with 2GB headers, causing the connected clients to run out of memory after allocating multiple large packets.")]
        public int MaxMessageSize = 16 * 1024;

        [Tooltip("Client processes a limit amount of messages per tick to avoid a deadlock where it might end up processing forever if messages come in faster than we can process them.")]
        public int MaxReceivesPerTick = 1000;

        [Tooltip("Server send queue limit per connection for pending messages. Telepathy will disconnect a connection's queues reach that limit for load balancing. Better to kick one slow client than slowing down the whole server.")]
        public int SendQueueLimitPerConnection = 10000;

        [Tooltip("Server receive queue limit per connection for pending messages. Telepathy will disconnect a connection's queues reach that limit for load balancing. Better to kick one slow client than slowing down the whole server.")]
        public int ReceiveQueueLimitPerConnection = 10000;

        // add to selectively created systems before Bootstrap is called
        public Type GetSystemType() => typeof(TelepathyTransportServerSystem);

        // apply configuration in awake
        void Awake()
        {
            server.Port = Port;
            server.NoDelay = NoDelay;
            server.MaxMessageSize = MaxMessageSize;
            server.MaxReceivesPerTick = MaxReceivesPerTick;
            server.SendTimeout = SendTimeout;
            server.ReceiveTimeout = ReceiveTimeout;
            server.SendQueueLimitPerConnection = SendQueueLimitPerConnection;
            server.ReceiveQueueLimitPerConnection = ReceiveQueueLimitPerConnection;
        }
    }
}