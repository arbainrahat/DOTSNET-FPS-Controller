using System;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using kcp2k;

namespace DOTSNET.kcp2k
{
    [ServerWorld]
    [AlwaysUpdateSystem]
    // use SelectiveSystemAuthoring to create it selectively
    [DisableAutoCreation]
    public class KcpTransportServerSystem : TransportServerSystem
    {
        // configuration
        public ushort Port = 7777;
        public bool NoDelay = true;
        public uint Interval = 10;

        // advanced configuration
        public int FastResend = 0;
        public bool CongestionWindow = true; // KCP 'NoCongestionWindow' is false by default. here we negate it for ease of use.
        public uint SendWindowSize = 4096; // Kcp.WND_SND; 32 by default. DOTSNET sends a lot, so we need a lot more.
        public uint ReceiveWindowSize = 4096; // Kcp.WND_RCV; 128 by default. DOTSNET sends a lot, so we need a lot more.

        // debugging
        public bool debugLog;

        // kcp server
        internal KcpServer server;

        // all except WebGL
        public override bool Available() =>
            Application.platform != RuntimePlatform.WebGLPlayer;

        // MTU
        public override int GetMaxPacketSize()
        {
            // TODO switch on channels
            //KcpConnection.MaxMessageSize;

            // return the smallest one that works for all channels for now
            return Math.Min(KcpConnection.UnreliableMaxMessageSize,
                            KcpConnection.ReliableMaxMessageSize);
        }

        public override bool IsActive() => server.IsActive();

        public override void Start()
        {
            server.Start(Port);
        }

        // note: DOTSNET already packs messages. Transports don't need to.
        public override bool Send(int connectionId, ArraySegment<byte> segment, Channel channel)
        {
            // switch to kcp channel.
            // unreliable or reliable.
            // default to reliable just to be sure.
            switch (channel)
            {
                case Channel.Unreliable:
                    server.Send(connectionId, segment, KcpChannel.Unreliable);
                    break;
                default:
                    server.Send(connectionId, segment, KcpChannel.Reliable);
                    break;
            }
            OnSend?.Invoke(connectionId, segment); // statistics etc.
            return true;
        }

        public override bool Disconnect(int connectionId)
        {
            server.Disconnect(connectionId);
            return true;
        }

        public override string GetAddress(int connectionId)
        {
            return server.GetClientAddress(connectionId);
        }

        public override void Stop()
        {
            server?.Stop();
        }

        // statistics
        public int GetAverageMaxSendRate() =>
            server.connections.Count > 0
                ? server.connections.Values.Sum(conn => (int)conn.MaxSendRate) / server.connections.Count
                : 0;
        public int GetAverageMaxReceiveRate() =>
            server.connections.Count > 0
                ? server.connections.Values.Sum(conn => (int)conn.MaxReceiveRate) / server.connections.Count
                : 0;
        public int GetTotalSendQueue() =>
            server.connections.Values.Sum(conn => conn.kcp.snd_queue.Count);
        public int GetTotalReceiveQueue() =>
            server.connections.Values.Sum(conn => conn.kcp.rcv_queue.Count);
        public int GetTotalSendBuffer() =>
            server.connections.Values.Sum(conn => conn.kcp.snd_buf.Count);
        public int GetTotalReceiveBuffer() =>
            server.connections.Values.Sum(conn => conn.kcp.rcv_buf.Count);

        // ECS /////////////////////////////////////////////////////////////////
        // use OnCreate to set everything up.
        // NetworkServerSystem.OnStartRunning & headless start happen BEFORE
        // Transport.OnStartRunning, so by then it would be too late for
        // headless server starts.
        protected override void OnCreate()
        {
            // logging
            //   Log.Info should use Debug.Log if enabled, or nothing otherwise
            //   (don't want to spam the console on headless servers)
            if (debugLog)
                Log.Info = Debug.Log;
            else
                Log.Info = _ => {};
            Log.Warning = Debug.LogWarning;
            Log.Error = Debug.LogError;

            // server
            server = new KcpServer(
                // other systems hook into transport events in OnCreate or
                // OnStartRunning in no particular order. the only way to avoid
                // race conditions where kcp uses OnConnected before another
                // system's hook (e.g. statistics OnData) was added is to wrap
                // them all in a lambda and always call the latest hook.
                // (= lazy call)
                (connectionId) => OnConnected.Invoke(connectionId),
                (connectionId, segment) => OnData.Invoke(connectionId, segment),
                (connectionId) => OnDisconnected.Invoke(connectionId),
                NoDelay,
                Interval,
                FastResend,
                CongestionWindow,
                SendWindowSize,
                ReceiveWindowSize
            );
            Debug.Log("KCP server created");
        }

        protected override void OnUpdate()
        {
            server.Tick();
        }

        protected override void OnDestroy()
        {
            server = null;
        }
    }
}