using TMPro;
using UnityEngine;

public class GameMusic : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip audioClip;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Debug.Log(SongSelection.songTitle);
        audioClip = Resources.Load<AudioClip>("Songs/" + SongSelection.songTitle);
    }

    // Update is called once per frame
    void Update()
    {
        if (ParseMIDI.canStartMusic && !audioSource.isPlaying)
        {
            audioSource.PlayOneShot(audioClip);
        }
    }
}
