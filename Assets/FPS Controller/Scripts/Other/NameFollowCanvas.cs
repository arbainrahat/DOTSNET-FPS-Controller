using DOTSNET.Examples.Physics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


public class NameFollowCanvas : MonoBehaviour
{
    public Entity entityToFollow;
    public float3 offSet = float3.zero;
 


    private void LateUpdate()
    {

        if (entityToFollow == Entity.Null)
        {
            GetPlayer();
        }
        else
        {
            Translation trans = MovementClientSystem1.tran;

            transform.position = trans.Value + offSet;
            
        }

    }

    void GetPlayer()
    {
        entityToFollow = AutoJoinWorldSystem.playerPrefabEntity;

    }
}
