using System;
using UnityEngine;

namespace DOTSNET.kcp2k
{
    public class KcpTransportServerAuthoring : MonoBehaviour, SelectiveSystemAuthoring
    {
        // find NetworkServerSystem in ECS world
        KcpTransportServerSystem server =>
            Bootstrap.ServerWorld.GetExistingSystem<KcpTransportServerSystem>();

        // common
        [Header("Configuration")]
        public ushort Port = 7777;
        [Tooltip("NoDelay is recommended to reduce latency. This also scales better without buffers getting full.")]
        public bool NoDelay = true;
        [Tooltip("KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities.")]
        public uint Interval = 10;

        [Header("Advanced")]
        [Tooltip("KCP fastresend parameter. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode.")]
        public int FastResend = 2;
        [Tooltip("KCP congestion window. Enabled in normal mode, disabled in turbo mode. Disable this for high scale games if connections get chocked regularly.")]
        public bool CongestionWindow = false; // KCP 'NoCongestionWindow' is false by default. here we negate it for ease of use.
        [Tooltip("KCP window size can be modified to support higher loads.")]
        public uint SendWindowSize = 4096; // Kcp.WND_SND; 32 by default. DOTSNET sends a lot, so we need a lot more.
        [Tooltip("KCP window size can be modified to support higher loads.")]
        public uint ReceiveWindowSize = 4096; // Kcp.WND_RCV; 128 by default. DOTSNET sends a lot, so we need a lot more.

        // debugging
        [Header("Debug")]
        public bool debugLog;
        // show statistics in OnGUI
        public bool statisticsGUI;
        // log statistics for headless servers that can't show them in GUI
        public bool statisticsLog;

        // add to selectively created systems before Bootstrap is called
        public Type GetSystemType() => typeof(KcpTransportServerSystem);

        // apply configuration in awake
        void Awake()
        {
            server.debugLog = debugLog;
            server.Port = Port;
            server.NoDelay = NoDelay;
            server.Interval = Interval;
            server.FastResend = FastResend;
            server.CongestionWindow = CongestionWindow;
            server.SendWindowSize = SendWindowSize;
            server.ReceiveWindowSize = ReceiveWindowSize;

            if (statisticsLog)
                InvokeRepeating(nameof(OnLogStatistics), 1, 1);
        }

        void OnGUI()
        {
            if (!statisticsGUI) return;

            GUILayout.BeginArea(new Rect(15, 250, 220, 300));
            if (server.IsActive())
            {
                GUILayout.BeginVertical("Box");
                GUILayout.Label("<b>KCP Server</b>");
                GUILayout.Label($"  connections: {server.server.connections.Count}");
                GUILayout.Label($"  MaxSendRate (avg): {Utils.PrettyBytes(server.GetAverageMaxSendRate())}/s");
                GUILayout.Label($"  MaxRecvRate (avg): {Utils.PrettyBytes(server.GetAverageMaxReceiveRate())}/s");
                GUILayout.Label($"  SendQueue: {server.GetTotalSendQueue()}");
                GUILayout.Label($"  ReceiveQueue: {server.GetTotalReceiveQueue()}");
                GUILayout.Label($"  SendBuffer: {server.GetTotalSendBuffer()}");
                GUILayout.Label($"  ReceiveBuffer: {server.GetTotalReceiveBuffer()}");
                GUILayout.EndVertical();
            }
            GUILayout.EndArea();
        }

        void OnLogStatistics()
        {
            if (server.IsActive())
            {
                string log = "kcp SERVER:\n";
                log += $"  connections: {server.server.connections.Count}\n";
                log += $"  MaxSendRate (avg): {Utils.PrettyBytes(server.GetAverageMaxSendRate())}/s\n";
                log += $"  MaxRecvRate (avg): {Utils.PrettyBytes(server.GetAverageMaxReceiveRate())}/s\n";
                log += $"  SendQueue: {server.GetTotalSendQueue()}\n";
                log += $"  ReceiveQueue: {server.GetTotalReceiveQueue()}\n";
                log += $"  SendBuffer: {server.GetTotalSendBuffer()}\n";
                log += $"  ReceiveBuffer: {server.GetTotalReceiveBuffer()}\n\n";
                Debug.Log(log);
            }
        }
    }
}