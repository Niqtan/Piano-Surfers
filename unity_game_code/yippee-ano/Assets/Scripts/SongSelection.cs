using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SongSelection : MonoBehaviour
{
    public AudioSource audioSource;
    string folderPath = "Excerpts";

    public Button leftButton;
    public Button rightButton;
    public Button selectButton;
    public TextMeshProUGUI songTitleText;
    public static string songTitle;

    private AudioClip[] songs;
    private int currentIndex = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Debug.Log(Directory.GetCurrentDirectory()+folderPath);
        songs = Resources.LoadAll<AudioClip>(folderPath);
        PlaySong(currentIndex);
        foreach(AudioClip song in songs)
        {
            Debug.Log(song);
        }
        leftButton.onClick.AddListener(PlayPrevSong);
        rightButton.onClick.AddListener(PlayNextSong);
        selectButton.onClick.AddListener(SelectSong);
        audioSource.loop = true;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void PlayNextSong()
    {
        currentIndex = (currentIndex + 1) % songs.Length;
        PlaySong(currentIndex);
    }
    void PlayPrevSong()
    {
        currentIndex = (currentIndex - 1 + songs.Length) % songs.Length;
        PlaySong(currentIndex);
    }

    void PlaySong(int index)
    {
        audioSource.Stop();
        audioSource.clip = songs[index];
        audioSource.Play();
        songTitleText.text = songs[index].name;
        songTitle = songTitleText.text;
    }

    void SelectSong()
    {
        SceneManager.LoadScene(2); //Game screen
    }
}
