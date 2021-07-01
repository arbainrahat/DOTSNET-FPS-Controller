using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace DOTSNET.Examples.Physics
{
    public class MovementClientSystemPlayer : MonoBehaviour, SelectiveSystemAuthoring
    {
        public Type GetSystemType() => typeof(MovementClientSystem1);
    }

    [ClientWorld]
    [UpdateInGroup(typeof(ClientConnectedSimulationSystemGroup))]
    [DisableAutoCreation]
    public class MovementClientSystem1 : SystemBase
    {
        public static  Translation tran;
        public static  Rotation rotate;
        protected override void OnUpdate()
        {
            float deltaTime = Time.DeltaTime;
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");

            float3 dir = new float3(horizontal, 0f, vertical);

            Translation t = new Translation();
            Rotation r =new Rotation();

            Entities.ForEach((ref PhysicsVelocity velocity, ref Translation translation, ref Rotation rotation, in MoveData moveData,in NetworkEntity networkEntity) =>
            {

                float w = rotation.Value.value.w;
                float y = rotation.Value.value.y;

                quaternion quat = new quaternion(0f, y, 0f, w);

                rotation.Value = quat;
                if (networkEntity.owned)
                {
                    velocity.Linear = velocity.Linear + math.mul(quat, dir) * moveData.speed * deltaTime;
                    t = translation;
                    r = rotation;
                }
                
                velocity.Angular.x = 0f;
                velocity.Angular.z = 0f;
                velocity.Angular.y = 0f;


                //Player Jump
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    translation.Value.y = moveData.jumpHieght;
                }


            }).Run();

            tran = t;
            rotate = r;
        }

    }
}