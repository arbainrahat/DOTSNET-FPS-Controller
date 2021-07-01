using Unity.Mathematics;
using Unity.Entities;
using Unity.Transforms;
using Unity.Jobs;
using Unity.Physics;
using UnityEngine;

public class PlayerMovementSystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = Time.DeltaTime;
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

       float3 dir = new float3(horizontal, 0f, vertical);
       

        Entities.ForEach((ref PhysicsVelocity velocity, ref Translation translation ,ref Rotation rotation, in MoveData moveData) =>
        { 
            
            float w = rotation.Value.value.w;
            float y = rotation.Value.value.y;
            
            quaternion quat = new quaternion(0f,y,0f,w);

            rotation.Value = quat;
            velocity.Linear = velocity.Linear + math.mul(quat, dir) * moveData.speed * deltaTime;

            velocity.Angular.x = 0f;
            velocity.Angular.z = 0f;
            velocity.Angular.y = 0f;

          //  Debug.Log("rotation : " + rotation.Value);
           // Debug.Log("Direction =" + dir);

            //Player Jump
            if (Input.GetKeyDown(KeyCode.Space))
            {
                translation.Value.y = moveData.jumpHieght;
            }


        }).Run();


    }
}