using Unity.Entities;
using UnityEngine;

[GenerateAuthoringComponent]
public struct InputData : IComponentData
{
   public KeyCode rightKey;
   public KeyCode leftKey;
   public KeyCode upKey;
   public KeyCode downKey;

    public KeyCode jumpKey;

}
