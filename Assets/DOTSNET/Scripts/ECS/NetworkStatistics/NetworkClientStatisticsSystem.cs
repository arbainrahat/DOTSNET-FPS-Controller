// provide packets/second and bytes/second statistics

using System;
using Unity.Entities;
using UnityEngine;

namespace DOTSNET
{
    // always update, even if no entities around
    [AlwaysUpdateSystem]
    [ClientWorld]
    [UpdateInGroup(typeof(ClientConnectedSimulationSystemGroup))]
    // use SelectiveAuthoring to create/inherit it selectively
    [DisableAutoCreation]
    public class NetworkClientStatisticsSystem : SystemBase
    {
        // dependencies
        TransportClientSystem transport;

        // capture interval
        // long bytes to support >2GB
        double intervalStartTime;
        int intervalReceivedPackets;
        long intervalReceivedBytes;
        int intervalSentPackets;
        long intervalSentBytes;

        // results from last interval
        // long bytes to support >2GB
        public int ReceivedPacketsPerSecond;
        public long ReceivedBytesPerSecond;
        public int SentPacketsPerSecond;
        public long SentBytesPerSecond;

        // hook up to Transport events
        protected override void OnStartRunning()
        {
            // find available client transport
            transport = TransportSystem.FindAvailable(World) as TransportClientSystem;
            if (transport != null)
            {
                transport.OnData += OnReceive;
                transport.OnSend += OnSend;
            }
            else Debug.LogError("NetworkClientStatisticsSystem: no available TransportClientSystem found on this platform: " + Application.platform);
        }

        // remove Transport hooks
        protected override void OnStopRunning()
        {
            if (transport != null)
            {
                transport.OnData -= OnReceive;
                transport.OnSend -= OnSend;
            }
        }

        void OnReceive(ArraySegment<byte> segment)
        {
            ++intervalReceivedPackets;
            intervalReceivedBytes += segment.Count;
        }

        void OnSend(ArraySegment<byte> segment)
        {
            ++intervalSentPackets;
            intervalSentBytes += segment.Count;
        }

        protected override void OnUpdate()
        {
            // calculate results every second
            if (Time.ElapsedTime >= intervalStartTime + 1)
            {
                ReceivedPacketsPerSecond = intervalReceivedPackets;
                ReceivedBytesPerSecond = intervalReceivedBytes;
                SentPacketsPerSecond = intervalSentPackets;
                SentBytesPerSecond = intervalSentBytes;

                intervalReceivedPackets = 0;
                intervalReceivedBytes = 0;
                intervalSentPackets = 0;
                intervalSentBytes = 0;

                intervalStartTime = Time.ElapsedTime;
            }
        }
    }
}
