using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class movement : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] GameObject m_object;
    bool isattack = false;
    void Start()
    {
        m_object = GetComponent<GameObject>();
        
    }

    // Update is called once per frame
    void Update()
    {
         if (Input.GetKeyDown(KeyCode.W)&&!isattack)
        {
             isattack = true;
            StartCoroutine(AttackRoutine());
        }
        
    }
    IEnumerator AttackRoutine()
    {
        yield return new WaitForSeconds(0.3f);
        Debug.Log("命中");
        isattack= false;
    }
    private void FixedUpdate()
    {
       
    }
}
