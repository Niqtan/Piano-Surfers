using NUnit.Framework;
using UnityEngine;

public class Parallax : MonoBehaviour
{
    Material mat;
    float distance;
    
    public float speed = 0.2f;
    public float parallaxMod = 1f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        mat = GetComponent<Renderer>().material;
    }

    // Update is called once per frame
    void Update()
    {
        if (ParseMIDI.canStartMusic)
        {
            distance += Time.deltaTime*speed;
        } else distance = 0;
        mat.SetTextureOffset("_MainTex", Vector2.right*distance);
    }

    public float getParallaxMod()
    {
        return parallaxMod;
    }

    public void setParallaxMod(float mod)
    {
        parallaxMod = mod;
    }
}
