using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Blood : MonoBehaviour
{
    public Transform followTarge;
    public Vector3 offset;
    public Camera obcamera;
    private void Update()
    {   if(followTarge==null)
        {
            return;
        }
        if(obcamera==null)
        {
            obcamera= Camera.main;
        }
        transform.position = obcamera.WorldToScreenPoint(followTarge.position+offset);


        
    }
}
