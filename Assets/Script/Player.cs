using System.Xml.Linq;
using UnityEngine;

public class Player : Character
{
    public Player(string name, float speed)
    {
        Name = name;
        Speed = speed;
        Health = 100;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public override void Move()
    { 
    
    }

}
