using System;
using System.Net.Sockets;
using Unity.Entities;
using UnityEngine;

namespace DOTSNET.TelepathyTransport
{
    [ServerWorld]
    [AlwaysUpdateSystem]
    // use SelectiveSystemAuthoring to create it selectively
    [DisableAutoCreation]
    public class TelepathyTransportServerSystem : TransportServerSystem
    {
        public ushort Port = 7777;

        // Nagle Algorithm can be disabled by enabling NoDelay
        public bool NoDelay = true;

        // timeouts in milliseconds.
        public int SendTimeout = 5000;
        public int ReceiveTimeout = 5000;

        // Protect against allocation attacks by keeping the max message size small. Otherwise an attacker host might send multiple fake packets with 2GB headers, causing the connected clients to run out of memory after allocating multiple large packets.
        public int MaxMessageSize = 16 * 1024;

        // Client processes a limit amount of messages per tick to avoid a deadlock where it might end up processing forever if messages come in faster than we can process them.
        public int MaxReceivesPerTick = 1000;

        // disconnect if message queues get too big.
        public int SendQueueLimitPerConnection = 10000;
        public int ReceiveQueueLimitPerConnection = 10000;

        Telepathy.Server server;

        // C#'s built in TCP sockets run everywhere except on WebGL
        public override bool Available() =>
            Application.platform != RuntimePlatform.WebGLPlayer;

        public override int GetMaxPacketSize() => MaxMessageSize;

        public override bool IsActive() => server.Active;

        public override void Start() => server.Start(Port);

        public override bool Send(int connectionId, ArraySegment<byte> segment, Channel channel)
        {
            server.Send(connectionId, segment);
            return true;
        }

        public override bool Disconnect(int connectionId) => server.Disconnect(connectionId);

        public override string GetAddress(int connectionId)
        {
            try
            {
                return server.GetClientAddress(connectionId);
            }
            catch (SocketException)
            {
                // using server.listener.LocalEndpoint causes an Exception
                // in UWP + Unity 2019:
                //   Exception thrown at 0x00007FF9755DA388 in UWF.exe:
                //   Microsoft C++ exception: Il2CppExceptionWrapper at memory
                //   location 0x000000E15A0FCDD0. SocketException: An address
                //   incompatible with the requested protocol was used at
                //   System.Net.Sockets.Socket.get_LocalEndPoint ()
                // so let's at least catch it and recover
                return "unknown";
            }
        }

        public override void Stop() => server.Stop();

        // ECS /////////////////////////////////////////////////////////////////
        // use OnCreate to set everything up.
        // NetworkServerSystem.OnStartRunning & headless start happen BEFORE
        // Transport.OnStartRunning, so by then it would be too late for
        // headless server starts.
        protected override void OnCreate()
        {
            // create server
            server = new Telepathy.Server(MaxMessageSize);

            // tell Telepathy to use Unity's Debug.Log
            Telepathy.Log.Info = Debug.Log;
            Telepathy.Log.Warning = Debug.LogWarning;
            Telepathy.Log.Error = Debug.LogError;

            // other systems hook into transport events in OnCreate or
            // OnStartRunning in no particular order. the only way to avoid
            // race conditions where telepathy uses OnConnected before another
            // system's hook (e.g. statistics OnData) was added is to wrap
            // them all in a lambda and always call the latest hook.
            // (= lazy call)
            server.OnConnected = (connectionId) => OnConnected.Invoke(connectionId);
            server.OnData = (connectionId, segment) => OnData.Invoke(connectionId, segment);
            server.OnDisconnected = (connectionId) => OnDisconnected.Invoke(connectionId);

            // configure
            server.NoDelay = NoDelay;
            server.SendTimeout = SendTimeout;
            server.ReceiveTimeout = ReceiveTimeout;
            server.SendQueueLimit = SendQueueLimitPerConnection;
            server.ReceiveQueueLimit = ReceiveQueueLimitPerConnection;

            Debug.Log("TelepathyTransportServerSystem initialized!");
        }

        protected override void OnUpdate()
        {
            // process a maximum amount of server messages per tick
            // stops when there are no more messages
            // => tick even while not active to still process disconnects.
            server.Tick(MaxReceivesPerTick);
        }

        protected override void OnDestroy()
        {
            Debug.Log("TelepathyTransportServerSystem Shutdown");
            server.Stop();
        }
    }
}
