using Unity.Entities;
using UnityEngine;

    [GenerateAuthoringComponent]
    public struct MoveData : IComponentData
    {
        public float speed;
        public float jumpHieght;
    }
