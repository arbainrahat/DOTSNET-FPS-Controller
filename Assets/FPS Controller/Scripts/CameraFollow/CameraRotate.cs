using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public class CameraRotate : MonoBehaviour
{
    float mouseX;

    public float mouseSenstivity = 100f;
    
    


    private void Start()
    {
      //  Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        if(FollowEntity.isPlayerSpwan == true)
        {

         mouseX = Input.GetAxis("Mouse X") * mouseSenstivity * Time.deltaTime;
    
         transform.Rotate(Vector3.up * mouseX);

        }

    }

}
