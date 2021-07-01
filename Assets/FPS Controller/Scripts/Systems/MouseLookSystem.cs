using Unity.Entities;
using UnityEngine;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

public class MouseLookSystem : SystemBase
{
    protected override void OnUpdate()
    {
        float mouseX;
        
        float deltaTime = Time.DeltaTime;

        mouseX = Input.GetAxis("Mouse X");

        Entities.ForEach((ref MouseLookData mouseLookData, ref Rotation rotation) =>
        {

            //Get angle on Y-axis
            quaternion yRot = quaternion.RotateY(mouseX * mouseLookData.mouseSenstivity * deltaTime * Mathf.Deg2Rad);

            //matrix multiplication
            rotation.Value = math.mul(rotation.Value, yRot);

            
        }).ScheduleParallel();
    }
}
