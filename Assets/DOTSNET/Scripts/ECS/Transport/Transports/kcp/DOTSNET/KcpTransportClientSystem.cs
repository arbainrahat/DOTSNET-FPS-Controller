using System;
using Unity.Entities;
using UnityEngine;
using kcp2k;

namespace DOTSNET.kcp2k
{
    [ClientWorld]
    [AlwaysUpdateSystem]
    // use SelectiveSystemAuthoring to create it selectively
    [DisableAutoCreation]
    public class KcpTransportClientSystem : TransportClientSystem
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

        // kcp
        internal KcpClient client;

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

        public override bool IsConnected() => client.connected;

        public override void Connect(string hostname)
        {
            client.Connect(hostname, Port, NoDelay, Interval, FastResend, CongestionWindow, SendWindowSize, ReceiveWindowSize);
        }

        public override bool Send(ArraySegment<byte> segment, Channel channel)
        {
            // switch to kcp channel.
            // unreliable or reliable.
            // default to reliable just to be sure.
            switch (channel)
            {
                case Channel.Unreliable:
                    client.Send(segment, KcpChannel.Unreliable);
                    break;
                default:
                    client.Send(segment, KcpChannel.Reliable);
                    break;
            }
            OnSend?.Invoke(segment); // statistics etc.
            return true;
        }

        public override void Disconnect()
        {
            client?.Disconnect();
        }

        // statistics
        public uint GetMaxSendRate() =>
            client.connection.MaxSendRate;
        public uint GetMaxReceiveRate() =>
            client.connection.MaxReceiveRate;
        public int GetSendQueueCount() =>
            client.connection.kcp.snd_queue.Count;
        public int GetReceiveQueueCount() =>
            client.connection.kcp.rcv_queue.Count;
        public int GetSendBufferCount() =>
            client.connection.kcp.snd_buf.Count;
        public int GetReceiveBufferCount() =>
            client.connection.kcp.rcv_buf.Count;

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

            // client
            client = new KcpClient(
                // other systems hook into transport events in OnCreate or
                // OnStartRunning in no particular order. the only way to avoid
                // race conditions where kcp uses OnConnected before another
                // system's hook (e.g. statistics OnData) was added is to wrap
                // them all in a lambda and always call the latest hook.
                // (= lazy call)
                () => OnConnected.Invoke(),
                (segment) => OnData.Invoke(segment),
                () => OnDisconnected.Invoke()
            );
            Debug.Log("KCP client created");
        }

        protected override void OnUpdate()
        {
            client.Tick();
        }

        protected override void OnDestroy()
        {
            client = null;
        }
    }
}