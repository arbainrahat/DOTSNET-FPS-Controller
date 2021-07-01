using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace DOTSNET.Examples.Physics
{
    public class PlayerRotateClientSystem : MonoBehaviour, SelectiveSystemAuthoring
    {
        public Type GetSystemType() => typeof(PlayerRotateClient);
    }

    [ClientWorld]
    [UpdateInGroup(typeof(ClientConnectedSimulationSystemGroup))]
    [DisableAutoCreation]
    public class PlayerRotateClient : SystemBase
    {
        protected override void OnUpdate()
        {
            float mouseX;

            float deltaTime = Time.DeltaTime;

            mouseX = Input.GetAxis("Mouse X");

            Entities.ForEach((ref MouseLookData mouseLookData, ref Rotation rotation, in NetworkEntity networkEntity) =>
            {

                //Get angle on Y-axis
                quaternion yRot = quaternion.RotateY(mouseX * mouseLookData.mouseSenstivity * deltaTime * Mathf.Deg2Rad);

                if (networkEntity.owned)
                {
                    //matrix multiplication
                    rotation.Value = math.mul(rotation.Value, yRot);
                }

            }).ScheduleParallel();

        }

    }
}