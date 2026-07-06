using UnityEngine;
using System;
using System.IO.Ports;
using System.Linq;
using System.Collections.Generic;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using System.Threading;

public class ParseMIDI : MonoBehaviour
{
    MidiFile midiFile;
    ICollection<Note> notes;
    long totalTicks;

    static (int offset, int width)[] octaveMap;

    TimeSignature firstTimeSignature;
    int countOffBeats;
    double beatDurationInMs;

    short ticksPerQuarterNote;
    TempoMap tempoMap;

    // Buffer stores 10 bytes per frame (8 bytes mask + 2 bytes duration)
    List<byte> masterBuffer = new List<byte>();

    int keyboardRangeStartNote = 60;
    int keyboardRange = 25;

    string portName = "COM10";
    int baudRate = 115200;
    private SerialPort serialPort;

    public static bool canStartMusic = false;
    bool triggerMainThreadPlayback = false;

    private Thread workerThread;
    private bool isThreadProcessing = false;
    private bool isPlaybackAlreadyScheduled = false;
    private readonly object serialPortLock = new object();

    private volatile float finalThreadCalculatedDelay = 0f; 

    void Start()
    {
        Debug.Log("=== INITIALIZING SYSTEM ===");
        
        serialPort = new SerialPort(portName, baudRate);
        serialPort.ReadTimeout = 1; // Prevent Update thread lockups
        try
        {
            serialPort.Open();

            serialPort.DtrEnable = true;
            serialPort.RtsEnable = true;
            Debug.Log("Serial port opened");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Can't open serial port: " + e.Message);
        }

        isThreadProcessing = true;
        workerThread = new Thread(ProcessMIDI);
        workerThread.IsBackground = true;
        workerThread.Start();
    }

void ProcessMIDI()
    {
        try
        {
            string filePath = System.IO.Path.Combine(Application.streamingAssetsPath, SongSelection.songTitle + ".mid");
            midiFile = MidiFile.Read(filePath);
            
            // 💥 FIX 1: Sort notes explicitly by their start time once to allow high-speed index streaming
            notes = midiFile.GetNotes().OrderBy(n => n.Time).ToList();
            totalTicks = midiFile.GetTimedEvents().LastOrDefault()?.Time ?? 0;

            octaveMap = new (int, int)[]
            {
                (0,  2), (2,  1), (3,  2), (5,  1), (6,  2), (8,  2),
                (10, 1), (11, 2), (13, 1), (14, 2), (16, 1), (17, 2)
            };

            ticksPerQuarterNote = ((TicksPerQuarterNoteTimeDivision)midiFile.TimeDivision).TicksPerQuarterNote;
            tempoMap = midiFile.GetTempoMap();
            firstTimeSignature = tempoMap.GetTimeSignatureChanges().FirstOrDefault()?.Value ?? new TimeSignature(4, 4);
            countOffBeats = firstTimeSignature.Numerator;

            Tempo startingTempo = tempoMap.GetTempoAtTime(new MidiTimeSpan(0));
            beatDurationInMs = startingTempo.MicrosecondsPerQuarterNote / 1000.0;
            ushort flashDuration = (ushort)Math.Round(beatDurationInMs * 0.25);
            ushort silenceDuration = (ushort)Math.Round(beatDurationInMs * 0.75);

            finalThreadCalculatedDelay = (float)((countOffBeats * beatDurationInMs) / 1000.0);

            long countOffMask = ((1L << 39) - 1) | (1L << 63);
            long silenceMask = (1L << 63);

            for (int beat = 0; beat < countOffBeats; beat++)
            {
                Add64BitFrameToBuffer(countOffMask, flashDuration);
                Add64BitFrameToBuffer(silenceMask, silenceDuration);
            }

            SortedSet<long> criticalTicks = new SortedSet<long> { 0 }; 
            foreach (Note note in notes)
            {
                criticalTicks.Add(note.Time);
                criticalTicks.Add(note.Time + note.Length);
            }
            foreach (var tempoChange in tempoMap.GetTempoChanges())
            {
                criticalTicks.Add(tempoChange.Time);
            }
            
            var tempoChanges = tempoMap.GetTempoChanges().OrderBy(tc => tc.Time).ToList();
            List<long> timelinePoints = criticalTicks.ToList();
            int totalFramesGenerated = countOffBeats * 2;
            double cumulativePreciseMs = 0.0;

            int currentTempoIndex = 0;
            long currentMicrosecondsPerQuarterNote = startingTempo.MicrosecondsPerQuarterNote;

            // 💥 FIX 2: Create a high-speed tracking list for notes that are actively playing
            List<Note> activeNotes = new List<Note>();
            int noteInputIndex = 0;
            var sortedNoteList = (List<Note>)notes;

            for (int i = 0; i < timelinePoints.Count - 1; i++)
            {
                long currentTick = timelinePoints[i];
                long nextTick = timelinePoints[i + 1];
                long durationInTicks = nextTick - currentTick;

                if (durationInTicks <= 0) continue;

                // A. Add new notes that have started playing since the last slice
                while (noteInputIndex < sortedNoteList.Count && sortedNoteList[noteInputIndex].Time <= currentTick)
                {
                    activeNotes.Add(sortedNoteList[noteInputIndex]);
                    noteInputIndex++;
                }

                // B. Remove notes that have completely finished playing by this slice point
                activeNotes.RemoveAll(n => (n.Time + n.Length) <= currentTick);

                long frameMask = 0;
                
                // C. ONLY loop through the tiny handful of currently active notes
                foreach (Note note in activeNotes)
                {
                    int relativeNote = ((note.NoteNumber - keyboardRangeStartNote) % keyboardRange + keyboardRange) % keyboardRange;
                    int noteInOctave = relativeNote % 12;
                    int targetNoteOffset = relativeNote / 12;

                    int startColumn = octaveMap[noteInOctave].offset + (targetNoteOffset * 19);
                    int columnWidth = octaveMap[noteInOctave].width;

                    for (int width = 0; width < columnWidth; width++)
                    {
                        int targetColumn = startColumn + width;
                        frameMask |= (1L << targetColumn); 
                    }
                }

                while (currentTempoIndex < tempoChanges.Count && currentTick >= tempoChanges[currentTempoIndex].Time)
                {
                    currentMicrosecondsPerQuarterNote = tempoChanges[currentTempoIndex].Value.MicrosecondsPerQuarterNote;
                    currentTempoIndex++;
                }

                double msPerTick = (double)currentMicrosecondsPerQuarterNote / (ticksPerQuarterNote * 1000.0);
                double exactNextTimelinePointMs = cumulativePreciseMs + (durationInTicks * msPerTick);
                ushort durationMs = (ushort)(Math.Round(exactNextTimelinePointMs) - Math.Round(cumulativePreciseMs));
                cumulativePreciseMs = exactNextTimelinePointMs;

                if (durationMs == 0) durationMs = 1; 

                Add64BitFrameToBuffer(frameMask, durationMs);
                totalFramesGenerated++;
            }

            Debug.Log($"Parsing finished. Total frames: {totalFramesGenerated}");

            byte[] header = BitConverter.GetBytes(totalFramesGenerated);
            byte[] finalPayload = masterBuffer.ToArray();

            lock (serialPortLock)
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    serialPort.Write(header, 0, 4);
                    serialPort.Write(finalPayload, 0, finalPayload.Length); 
                    Debug.Log("Data payload buffer written over serial stream.");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Thread exception crash: " + e.Message);
        }
        finally
        {
            isThreadProcessing = false;
        }
    }

    // FIXED: Correctly extracts all 8 bytes (64-bits) from mask and 2 bytes from duration
    private void Add64BitFrameToBuffer(long mask, ushort duration)
    {
        masterBuffer.Add((byte)(mask & 0xFF));
        masterBuffer.Add((byte)((mask >> 8) & 0xFF));
        masterBuffer.Add((byte)((mask >> 16) & 0xFF));
        masterBuffer.Add((byte)((mask >> 24) & 0xFF));
        masterBuffer.Add((byte)((mask >> 32) & 0xFF));
        masterBuffer.Add((byte)((mask >> 40) & 0xFF));
        masterBuffer.Add((byte)((mask >> 48) & 0xFF));
        masterBuffer.Add((byte)((mask >> 56) & 0xFF));
        
        masterBuffer.Add((byte)(duration & 0xFF));
        masterBuffer.Add((byte)((duration >> 8) & 0xFF));
    }

    // FIXED: Cleaned parameters to match frame count requirements
    private System.Collections.IEnumerator ExecuteSyncedPlayback(float delayTime)
    {
        Debug.Log("Delay time: " + delayTime);
        yield return new WaitForSeconds(delayTime);
        canStartMusic = true;
        Debug.Log(canStartMusic);
    }

    void OnApplicationQuit() 
    {
        isThreadProcessing = false;
        lock (serialPortLock)
        {
            if (serialPort != null && serialPort.IsOpen) 
            {
                Debug.Log("Ending");
                serialPort.WriteLine("End");
                serialPort.BaseStream.Flush();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
                serialPort.Close();
                serialPort.Dispose();
            }
        }
    }

    void OnApplicationPause(bool pauseStatus) 
    {
        if (Application.isMobilePlatform)
        {
            isThreadProcessing = false;
            if (serialPort != null && serialPort.IsOpen && pauseStatus) 
            {
                Debug.Log("Ending");
                serialPort.WriteLine("End");
                serialPort.BaseStream.Flush();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
                serialPort.Close();
                serialPort.Dispose();
            }
        }
        
    }

    // FIXED: Closed out incomplete try/catch syntax blocks safely
    void Update()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                if (serialPort.BytesToRead > 0) 
                {
                    string incoming = serialPort.ReadLine();
                    incoming = incoming.Trim();
                    Debug.Log("Received: " + incoming);
                    if (incoming == "Ready")
                    {
                        Debug.Log("Hardware ready");
                        triggerMainThreadPlayback = true;
                    }
                }
            }
            catch (TimeoutException) {}
            catch (Exception e)
            {
                Debug.LogError("Serial read error: " + e.Message);
            }
        }
        if (triggerMainThreadPlayback)
        {
            triggerMainThreadPlayback = false;
            // if (!isPlaybackAlreadyScheduled)
            // {
            //     isPlaybackAlreadyScheduled = true; // Lock the gate instantly on Frame 1
                
                StartCoroutine(ExecuteSyncedPlayback(finalThreadCalculatedDelay));
            // }
        }
    }
    void OnDisable()
    {
        isPlaybackAlreadyScheduled = false;
        canStartMusic = false;
        if (serialPort != null && serialPort.IsOpen) 
        {
            Debug.Log("Ending");
            serialPort.WriteLine("End");
            serialPort.BaseStream.Flush();
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();
            serialPort.Close();
            serialPort.Dispose();
        }
    }
}
