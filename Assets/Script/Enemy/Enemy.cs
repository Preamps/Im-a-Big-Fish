using System;
using System.Xml.Linq;
using UnityEngine;

public class Enemy : Character
{
    public float damage;
    protected Transform targetPlayer; // เก็บพิกัดของ Player ที่จะไล่ตาม

    // ฟังก์ชันหา Player ที่ใกล้ที่สุด (ตัวอย่างเบื้องต้น)
    protected void FindTarget()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            targetPlayer = playerObj.transform;
        }
    }

    public override void Move()
    {
        if (targetPlayer == null)
        {
            FindTarget();
            return;
        }

        // คำนวณทิศทางไปหา Player (เฉพาะแกน X สำหรับเกม 2D เดินซ้ายขวา)
        float direction = (targetPlayer.position.x > transform.position.x) ? 1 : -1;

        // เคลื่อนที่เข้าหา
        transform.position += new Vector3(direction * Speed * Time.deltaTime, 0, 0);

        // หมุนหน้าศัตรู
        transform.localScale = new Vector3(direction, 1, 1);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
