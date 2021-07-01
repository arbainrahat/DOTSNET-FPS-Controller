using System;
using DOTSNET;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

public class PlayerMovementServerAuth : MonoBehaviour, SelectiveSystemAuthoring
{
    public Type GetSystemType() => typeof(PlayerMovementServer);
}

[ServerWorld]
[UpdateInGroup(typeof(ServerActiveSimulationSystemGroup))]
[DisableAutoCreation]
public class PlayerMovementServer : SystemBase
{
    protected override void OnUpdate()
    {
        // remove physics components from from spheres on the server,
        // so that we can apply NetworkTransform synchronization.
        Entities.ForEach((in Entity entity, in MoveData move) =>
        {
            EntityManager.RemoveComponent<PhysicsCollider>(entity);
            EntityManager.RemoveComponent<PhysicsDamping>(entity);
            EntityManager.RemoveComponent<PhysicsGravityFactor>(entity);
            EntityManager.RemoveComponent<PhysicsMass>(entity);
            EntityManager.RemoveComponent<PhysicsVelocity>(entity);
        })
        .WithStructuralChanges()
        .Run();
    }
}