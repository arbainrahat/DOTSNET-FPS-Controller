// a simple packets & bandwidth GUI
using System;
using UnityEngine;

namespace DOTSNET
{
    public class NetworkClientStatisticsAuthoring : MonoBehaviour, SelectiveSystemAuthoring
    {
        // find NetworkClientSystem in ECS world
        NetworkClientStatisticsSystem statistics =>
            Bootstrap.ClientWorld.GetExistingSystem<NetworkClientStatisticsSystem>();

        // add system if Authoring is used
        public Type GetSystemType() { return typeof(NetworkClientStatisticsSystem); }

        void OnGUI()
        {
            // create GUI area
            GUILayout.BeginArea(new Rect(15, 165, 220, 300));

            // background
            GUILayout.BeginVertical("Box");
            GUILayout.Label("<b>Client Statistics</b>");

            // sending
            GUILayout.Label($"Send: {statistics.SentPacketsPerSecond} packets @ {Utils.PrettyBytes(statistics.SentBytesPerSecond)}/s");

            // receiving
            GUILayout.Label($"Recv: {statistics.ReceivedPacketsPerSecond} packets @ {Utils.PrettyBytes(statistics.ReceivedBytesPerSecond)}/s");

            // end background
            GUILayout.EndVertical();

            // end of GUI area
            GUILayout.EndArea();
        }
    }
}