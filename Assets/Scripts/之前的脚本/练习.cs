using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class 练习 : MonoBehaviour
{ 
    private PlayerState currentstate;
    [SerializeField] private Transform transformCamera;
    [SerializeField]  private float aircontraoler=0.5f;
    [SerializeField] private float speed = 5f;
    [SerializeField] private float RayDistance = 0.5f;
    [SerializeField] private float jumpforce = 5f;
    [SerializeField] private LayerMask groundlayer;
    [SerializeField] private Collider charactercollider;
    Rigidbody rb;
    private bool isground;
    private bool jumpread;
    private enum PlayerState {
        idle,
        move,
        jump
    
    }
    private void Awake()
    {
        rb= GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        charactercollider= GetComponent<Collider>();

    }
    private void Start()
    {
        currentstate = PlayerState.idle;
        
    }
    private void changestate(PlayerState newState)
    {
        if (currentstate == newState)
        {  return; }
        currentstate = newState;
    }
    

    private void Update()
    {
        getinput();
        
        switch (currentstate)
        {
            case PlayerState.idle:UpdateIdle(); break;
            case PlayerState.move: Updatemove(); break;
            case PlayerState.jump: Updatejump(); break;
        }
    }

    private void getinput()
    {
        if (isground && Input.GetKeyDown(KeyCode.Space)&&currentstate!=PlayerState.jump)
        {
            jumpread= true;
        }
}

    private void FixedUpdate()
    {
        checkGround();
        switch (currentstate)
        {
            case PlayerState.idle: FUpdateIdle(); break;
            case PlayerState.move: FUpdatemove(); break;
            case PlayerState.jump: FUpdatejump(); break;
        }
        ApplyJump();
    }

    private void ApplyJump()
    {
       if(!jumpread)
        {
            return;
        }
        rb.AddForce(jumpforce*Vector3.up,ForceMode.Impulse);
        jumpread = false;
    }

    private void checkGround()
    {
        float halfheight = charactercollider.bounds.extents.y;
        Vector3 origin = transform.position - Vector3.up * (halfheight - 0.05f);
        isground = Physics.Raycast(origin, Vector3.down, RayDistance, groundlayer);
    }

    private void FUpdatejump()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 moveDir = getMoveDirection(h,v);
        rb.velocity = new Vector3(moveDir.x * speed * aircontraoler, rb.velocity.y, moveDir.z * speed * aircontraoler);

    }

    private Vector3 getMoveDirection(float h, float v)
    {
        Vector3 moveDir;
       if (transformCamera!=null)
        {
            Vector3 f = transformCamera.forward;
            Vector3 r = transformCamera.right;
            f.y = 0;
            r.y = 0;
            f.Normalize();
            r.Normalize();
            moveDir = (h * r + v * f).normalized;
            rb.velocity = new Vector3(moveDir.x*speed,rb.velocity.y,moveDir.z*speed);

        }
        else
        {
            moveDir = new Vector3(h, 0, v).normalized;
        }
        return moveDir;


    }

    private void FUpdatemove()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 moveDir=getMoveDirection(h,v);
        rb.velocity = new Vector3(moveDir.x*speed,rb.velocity.y,moveDir.z*speed);
    }

    private void FUpdateIdle()
    {
        rb.velocity= new Vector3(0,rb.velocity.y,0);
    }

    private void Updatejump()
    {
        if(isground)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            changestate(new Vector3(h, 0, v).sqrMagnitude> 0.01f ? PlayerState.move : PlayerState.idle);
        }
        
       
    }

    private void Updatemove()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (new Vector3(h, 0, v).sqrMagnitude < 0.01f)
        {
            changestate(PlayerState.idle);
        }
    }

    private void UpdateIdle()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if(new Vector3(h,0,v).sqrMagnitude>0.01f)
        {
            changestate(PlayerState.move);
        }
    }
}

