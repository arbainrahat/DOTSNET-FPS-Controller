using DOTSNET;
using DOTSNET.Examples.Physics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


public class FollowEntity : MonoBehaviour
{
    public Entity entityToFollow;
    public float3 offSet = float3.zero;
    public static bool isPlayerSpwan = false;
    

    private void LateUpdate()
    {       
        
        if (entityToFollow == Entity.Null)
        {
            GetPlayer();           
        }
        else
        {
           Translation trans = MovementClientSystem1.tran;
           Rotation rotation = MovementClientSystem1.rotate;

           transform.position = trans.Value + offSet;
           transform.rotation = rotation.Value;

            isPlayerSpwan = true;
        }

    }
    
    void GetPlayer()
    {
        entityToFollow = AutoJoinWorldSystem.playerPrefabEntity;
     
    }
}
