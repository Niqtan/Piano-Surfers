using System;
using UnityEngine;

public class FishSinusoidal : MonoBehaviour
{
    float startY;
    float amplitude = 3.0f;
    float nextAmplitude;
    float startFrequency = 0.2f;
    float endFrequency;
    float currentFrequency;
    float period;
    float angle;
    float lastWaveStartTime = 0;
    float homeThreshold = 0.05f;

    float swishSpeedMod = 0.4f;
    float swishAmplitudeMod;
    float startZRotation = 90;
    float endZRotation = -90;
    float currentZRotation;
    float currentSlope;
    private Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startY = transform.position.y;
        period = 1/startFrequency;
        endFrequency = UnityEngine.Random.Range(0.2f, 0.4f);
        nextAmplitude = UnityEngine.Random.Range(0.7f, 2f);
        angle = startFrequency;
        animator = GetComponent<Animator>();
        
        currentZRotation = transform.rotation.z;
        currentSlope = 90/(period/2);
        currentFrequency = startFrequency;

    }

    // Update is called once per frame
    void Update()
    {
        swishAmplitudeMod = Mathf.Lerp(amplitude, nextAmplitude, Mathf.Clamp01(Time.time - lastWaveStartTime)/period);
            
        animator.SetFloat("swishSpeed", currentFrequency*swishSpeedMod*swishAmplitudeMod);
        
        if (ParseMIDI.canStartMusic)
        {        
            currentFrequency = Mathf.Lerp(startFrequency, endFrequency, Mathf.Clamp01(Time.time - lastWaveStartTime)/period);
            angle += currentFrequency* Time.deltaTime*2*MathF.PI;
            angle %= 2*MathF.PI;
            float newY = Mathf.Sin(angle) * amplitude;

            
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
            // Debug.Log("Start Frequency: " + startFrequency + " End Frequency: "+ endFrequency + " Current Freq: " + currentFrequency + "Amplitude: " + amplitude + "Time: " + Time.time + "Delta Time: " + (Time.time - lastWaveStartTime) + "Period: " + period + " Swish Speed: " + swishSpeedMod*amplitude*currentFrequency);
            if (Math.Abs(newY  -amplitude) <= homeThreshold)
            {
                currentSlope = -90/(period/2);
            }
            else if (Math.Abs(newY + amplitude) <= homeThreshold)
            {
                currentSlope = 90/(period/2);
            }

            float newZRot = currentSlope*Math.Abs(Time.time - lastWaveStartTime)%90;
            Quaternion targetRotation = Quaternion.Euler(0, 0, newZRot);

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime);
            // Debug.Log("new Z Rot: "+ newZRot + " actual new rot: " + transform.rotation.z + " slope: "+currentSlope );

            if (Time.time - lastWaveStartTime >= period && Mathf.Abs(transform.position.y - startY) <= 0.05)
            {
                nextAmplitude = UnityEngine.Random.Range(0.7f, 2f);
                startFrequency = endFrequency;
                endFrequency = UnityEngine.Random.Range(0.2f, 0.5f);
                period = 1/startFrequency;
                lastWaveStartTime = Time.time;
            }
        }
    }
}
