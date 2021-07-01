using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraLookUpDown : MonoBehaviour
{
    float mouseY;

    float xRotation = 0f;
    public float mouseSenstivity = 100f;

    void Update()
    {
        if (FollowEntity.isPlayerSpwan == true)
        {
            mouseY = Input.GetAxis("Mouse Y") * mouseSenstivity * Time.deltaTime;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 20f);

            transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }
       
    }
}
