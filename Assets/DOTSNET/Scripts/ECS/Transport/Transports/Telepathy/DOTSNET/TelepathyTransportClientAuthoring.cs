using System;
using UnityEngine;

namespace DOTSNET.TelepathyTransport
{
    public class TelepathyTransportClientAuthoring : MonoBehaviour, SelectiveSystemAuthoring
    {
        // find NetworkClientSystem in ECS world
        TelepathyTransportClientSystem client =>
            Bootstrap.ClientWorld.GetExistingSystem<TelepathyTransportClientSystem>();

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

        [Tooltip("Client send queue limit for pending messages. Telepathy will disconnect if the connection's queues reach that limit in order to avoid ever growing latencies.")]
        public int SendQueueLimit = 10000;

        [Tooltip("Client receive queue limit for pending messages. Telepathy will disconnect if the connection's queues reach that limit in order to avoid ever growing latencies.")]
        public int ReceiveQueueLimit = 10000;

        // add to selectively created systems before Bootstrap is called
        public Type GetSystemType() => typeof(TelepathyTransportClientSystem);

        // apply configuration in awake
        void Awake()
        {
            client.Port = Port;
            client.NoDelay = NoDelay;
            client.MaxMessageSize = MaxMessageSize;
            client.MaxReceivesPerTick = MaxReceivesPerTick;
            client.SendTimeout = SendTimeout;
            client.ReceiveTimeout = ReceiveTimeout;
            client.SendQueueLimit = SendQueueLimit;
            client.ReceiveQueueLimit = ReceiveQueueLimit;
        }
    }
}