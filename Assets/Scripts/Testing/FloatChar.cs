using System;
using UnityEngine;

public class FloatChar : MonoBehaviour
{
    public float speed = 0.5f;
    public float time = 1f;
    // Update is called once per frame
    
    void Update()
    {
        transform.position += Vector3.up * (speed * Time.deltaTime);
        time -= Time.deltaTime;
        if(time <= 0) Destroy(gameObject);
    }
}
