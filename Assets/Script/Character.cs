using UnityEngine;

public abstract class Character : MonoBehaviour
{
    public string Name { get; set; }
    public float Health { get; set; }
    public float Speed { get; set; }
    public float X { get; set; }
    public float Y { get; set; }

    public abstract void Move(); // ให้ลูกๆ ไปเขียนวิธีเดินเอาเอง
    
}
