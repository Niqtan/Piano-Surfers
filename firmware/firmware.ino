#include <Adafruit_NeoPixel.h>
#include <Adafruit_NeoMatrix.h>

// LED Matrices
#define LED_MATRIX_PIN D1
#define TOPLIGHT_PIN D2
#define BACKLIGHT_PIN D3
#define MATRIX_WIDTH 40
#define MATRIX_HEIGHT 16
#define NUM_PIXELS (MATRIX_WIDTH * MATRIX_HEIGHT)

#define PANEL_WIDTH 40
#define PANEL_HEIGHT 8
#define TILES_X 1
#define TILES_Y 2

#define MATRIX_BRIGHTNESS 5

// LED Front & Backlights
#define LED_FRONT_AND_BACK 0

float ledIndex = 0;
float columnIndex = 0;
float rowIndex = 0;
Adafruit_NeoPixel topLight = Adafruit_NeoPixel(MATRIX_WIDTH, TOPLIGHT_PIN, NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel backLight = Adafruit_NeoPixel(MATRIX_WIDTH, BACKLIGHT_PIN, NEO_GRB + NEO_KHZ800);
Adafruit_NeoMatrix matrix = Adafruit_NeoMatrix(
  PANEL_HEIGHT, PANEL_WIDTH, TILES_Y, TILES_X, LED_MATRIX_PIN,
  NEO_MATRIX_BOTTOM + NEO_MATRIX_RIGHT + NEO_MATRIX_ROWS + NEO_MATRIX_ZIGZAG +
  NEO_TILE_BOTTOM + NEO_TILE_RIGHT + NEO_TILE_ROWS + NEO_TILE_PROGRESSIVE,
  NEO_GRB + NEO_KHZ800
);

 uint16_t countOffColor = matrix.Color(150, 150, 150); // Bright Red for count-off flashes
uint16_t columnColors[MATRIX_WIDTH];
  // 💥 NEW: Define the physical key color options
  uint16_t whiteKeyColorMap[7] = {
    matrix.Color(0, 150, 0),   // C (Green)
    matrix.Color(0, 0, 150),   // D (Blue)
    matrix.Color(150, 150, 0), // E (Yellow)
    matrix.Color(0, 150, 150), // F (Cyan)
    matrix.Color(150, 0, 150), // G (Purple)
    matrix.Color(150, 50, 0),  // A (Orange)
    matrix.Color(0, 120, 120)  // B (Teal)
  };
  uint16_t blackKeyColor = matrix.Color(120, 120, 120); // White/Grey for Black keys

  // 💥 NEW: Map colors dynamically to all 40 columns while pairing wide keys
  


// MiDi initialization
struct __attribute__((__packed__)) FramePacket {
  uint64_t frameMask;     // 8 bytes
  uint16_t durationMs;   // 2 bytes
  uint32_t absoluteTimeMs;// 4 bytes <- NEW: Explicitly tracks where this note sits in the song timeline
};

FramePacket* ledBuffer = NULL; // Dynamic array to hold the incoming song
uint64_t totalFrames = 0;
uint64_t currentPlaybackFrame = 0;
bool songLoaded = false;

uint16_t fallingLEDColor = matrix.Color(150, 150, 150);

// Tracks the exact bitmask of notes that are currently expected to be held down on Row 0
uint64_t activeChordTargetMask = 0;

// Tracks individual notes that the player has already successfully hit inside this specific frame window
uint64_t playerSatisfiedNotesMask = 0; 

void setup() {
  Serial.begin(115200);
  matrix.begin();
  matrix.setBrightness(MATRIX_BRIGHTNESS);
  matrix.show();

  for (int col = 0; col < MATRIX_WIDTH; col++) {
    int currentOctave = col / 19;          // Finds if we are in octave 0, 1, or 2
    int colInOctave = col % 19;            // Finds the column slot (0 to 18) within this octave

    // Look up what type of key sits at this column slot index
    if (colInOctave == 0 || colInOctave == 1)       columnColors[col] = whiteKeyColorMap[0]; // C (Wide)
    else if (colInOctave == 2)                      columnColors[col] = blackKeyColor;       // C#
    else if (colInOctave == 3 || colInOctave == 4)  columnColors[col] = whiteKeyColorMap[1]; // D (Wide)
    else if (colInOctave == 5)                      columnColors[col] = blackKeyColor;       // D#
    else if (colInOctave == 6 || colInOctave == 7)  columnColors[col] = whiteKeyColorMap[2]; // E (Wide)
    else if (colInOctave == 8 || colInOctave == 9)  columnColors[col] = whiteKeyColorMap[3]; // F (Wide)
    else if (colInOctave == 10)                     columnColors[col] = blackKeyColor;       // F#
    else if (colInOctave == 11 || colInOctave == 12) columnColors[col] = whiteKeyColorMap[4]; // G (Wide)
    else if (colInOctave == 13)                     columnColors[col] = blackKeyColor;       // G#
    else if (colInOctave == 14 || colInOctave == 15) columnColors[col] = whiteKeyColorMap[5]; // A (Wide)
    else if (colInOctave == 16)                     columnColors[col] = blackKeyColor;       // A#
    else if (colInOctave == 17 || colInOctave == 18) columnColors[col] = whiteKeyColorMap[6]; // B (Wide)
}

}


void fill_matrix() {
  // matrix.fill(matrix.Color(0, 150, 0), 0, NUM_PIXELS);
  // for (int i = 0; i < NUM_PIXELS; i++) {
  // matrix.setPixelColor(i, matrix.Color(0, 150, 0));
  // matrix.show();
  // // matrix.clear();
  // };
}

void customDrawPixel(int rowIndex, int columnIndex, uint16_t color) {
  matrix.drawPixel(MATRIX_HEIGHT - 1 - rowIndex, columnIndex, color);
}

void checkPlayerInputAccuracy(int pressedMidiNoteNumber) {
  if (!songLoaded || currentPlaybackFrame == 0) return;

  // 1. Convert pitch to your 40-column matrix track layout position index
  int relativeNote = ((pressedMidiNoteNumber - 60) % 25 + 25) % 25; 
  int noteInOctave = relativeNote % 12;
  int targetNoteOffset = relativeNote / 12;
  int columnMapOffset[] = {0, 2, 3, 5, 6, 8, 10, 11, 13, 14, 16, 17};
  int playerInputColumn = columnMapOffset[noteInOctave] + (targetNoteOffset * 19);
  uint64_t playerInputBitmask = ((uint64_t)1 << playerInputColumn);

    int keyColumnWidth = 1;
  // Notes 1, 4, 6, 9, 11 in an octave are black keys (C#, D#, F#, G#, A#)
  if (noteInOctave != 1 && noteInOctave != 4 && noteInOctave != 6 && noteInOctave != 9 && noteInOctave != 11) {
    keyColumnWidth = 2; // White key matches width metrics
  }

  // 💥 NEW: Generate a multi-bit mask to cover the whole key width
  for (int w = 0; w < keyColumnWidth; w++) {
    int targetColumn = targetColumn + w;
    if (targetColumn >= 0 && targetColumn < MATRIX_WIDTH) {
      playerInputBitmask |= ((uint64_t)1 << targetColumn);
    }
  }

  // 2. Capture the current absolute song timeline positions
  uint32_t currentPlaybackTimeMs = ledBuffer[currentPlaybackFrame].absoluteTimeMs;

  // 💥 NEW: CHORD HIT EVALUATION
  // Check if the pressed note is inside the current active chord window
  if ((activeChordTargetMask & playerInputBitmask) != 0) {
    
    // Clear the note from the target map so it cannot be double-processed
    activeChordTargetMask &= ~playerInputBitmask;
    playerSatisfiedNotesMask |= playerInputBitmask; // Record that the player struck this note successfully

    // Calculate accuracy errors relative to the active frame's scheduled time anchor point
    int targetScheduledTime = ledBuffer[currentPlaybackFrame].absoluteTimeMs;
    int errorOffset = currentPlaybackTimeMs - targetScheduledTime; 

    uint8_t judgmentCode = 3; // Default to Good
    int absoluteOffset = abs(errorOffset);
    if (absoluteOffset <= 45)       judgmentCode = 1; // Perfect
    else  judgmentCode = 2; // Great

    int8_t compressedErrorByte = (int8_t)constrain(errorOffset, -120, 120);

    // Stream the hit packet to Unity instantly
    Serial.write(0xAA);               
    Serial.write(judgmentCode);       
    Serial.write(compressedErrorByte);
    return;
  }

  // 💥 CHORD LOOKAHEAD FAIL-SAFE: 
  // If the note wasn't in the exact active frame, check the adjacent forward/backward 
  // frames to absorb slight human variations (early or late chord hits)
  int searchRadius = 4;
  int startSearch = (currentPlaybackFrame > searchRadius) ? currentPlaybackFrame - searchRadius : 0;
  int endSearch = ((currentPlaybackFrame + searchRadius) < totalFrames) ? currentPlaybackFrame + searchRadius : totalFrames;

  for (int f = startSearch; f < endSearch; f++) {
    if ((ledBuffer[f].frameMask & playerInputBitmask) != 0) {
      int targetScheduledTime = ledBuffer[f].absoluteTimeMs;
      int errorOffset = currentPlaybackTimeMs - targetScheduledTime;
      int absoluteOffset = abs(errorOffset);

      if (absoluteOffset <= 135) { // Within Good window
        uint8_t judgmentCode = (absoluteOffset <= 45) ? 1 : ((absoluteOffset <= 85) ? 2 : 3);
        int8_t compressedErrorByte = (int8_t)constrain(errorOffset, -120, 120);

        Serial.write(0xAA);               
        Serial.write(judgmentCode);       
        Serial.write(compressedErrorByte);
        return;
      }
    }
  }

  // Pure Pitch Miss (Player hit a note that isn't scheduled anywhere near the playhead)
  Serial.write(0xAA);
  Serial.write(4); // 4 = Miss / Wrong Note
  Serial.write((uint8_t)0x00);
}

void loop() {
  if (Serial.available() > 0) {
    char firstChar = Serial.peek();
    if (firstChar  == 'E') {
      String command = Serial.readStringUntil('\n');
      command.trim();
      if (command == "End") {
        matrix.fill(matrix.Color(0, 0, 0), 0, NUM_PIXELS);
        matrix.show();
        songLoaded = false;
        currentPlaybackFrame = 0;
        totalFrames = 0;
        return;
      }
    }
  }
  if (!songLoaded) {
    if (Serial.available() >= 4) {
      uint8_t h0 = Serial.read(); uint8_t h1 = Serial.read();
      uint8_t h2 = Serial.read(); uint8_t h3 = Serial.read();
      totalFrames = h0 | (h1 << 8) | (h2 << 16) | (h3 << 24);

      uint64_t bytesToRead = totalFrames * 10; // Unity still transmits 10 bytes per packet over the wire
      
      // Allocate the expanded 14-byte per packet array space in Arduino RAM
      ledBuffer = (FramePacket*)malloc(totalFrames * sizeof(FramePacket));

      if (ledBuffer == NULL) { while(1); }

      uint32_t runningTimelineTrackerMs = 0;
      uint64_t framesRead = 0;

      while (framesRead < totalFrames) {
        // Read 8 bytes of Bitmask layout directly into the current packet
        if (Serial.available() >= 10) {
          uint64_t mask = 0;
          for (int i = 0; i < 8; i++) {
            mask |= ((uint64_t)Serial.read() << (i * 8));
          }
          
          uint16_t duration = Serial.read() | (Serial.read() << 8);

          // Populate the expanded internal memory bank elements
          ledBuffer[framesRead].frameMask = mask;
          ledBuffer[framesRead].durationMs = duration;
          
          // 💥 THE TIMELINE FIX: Lock down the exact millisecond placement of this note
          ledBuffer[framesRead].absoluteTimeMs = runningTimelineTrackerMs;
          
          runningTimelineTrackerMs += duration; // Advance the timeline clock tracking index
          framesRead++;
        }
      }
      Serial.println("Ready");
      Serial.flush();
      songLoaded = true;
    }
    return;
  }

  if (currentPlaybackFrame < totalFrames && songLoaded) {
    unsigned long renderStartMicros = micros();
    uint64_t activeNotesMask = ledBuffer[currentPlaybackFrame].frameMask;
    uint32_t waitTime = ledBuffer[currentPlaybackFrame].durationMs * 1000UL;
    bool isCountOffPacket = (activeNotesMask & ((uint64_t)1 << 63)) != 0;
    if (isCountOffPacket){
      if ((activeNotesMask & ((uint64_t)1 << 24 != 0))) {
        topLight.fill(countOffColor, 0, MATRIX_WIDTH);
        backLight.fill(countOffColor, 0, MATRIX_WIDTH);
      } else {
        topLight.fill(matrix.Color(0, 0, 0), 0, MATRIX_WIDTH);
        backLight.fill(matrix.Color(0, 0, 0), 0, MATRIX_WIDTH);
      }
      topLight.show();
      backLight.show();
  //  matrix.fill(matrix.Color(0, 150, 0), 0, NUM_PIXELS);

    } //if count off
    else {
       if (activeChordTargetMask != 0) {
        // Send a multi-note Miss packet up to Unity
        Serial.write(0xAA);
        Serial.write(4);    // 4 = Miss ID
        Serial.write((uint8_t)0x00); // 0ms Error
      }
      for (uint64_t row = 0; row < MATRIX_HEIGHT; row++) {
        uint64_t targetFrameIndex = currentPlaybackFrame + row;
        
        // If we are approaching the final frames of the song, draw blank space instead of breaking out
        if (targetFrameIndex < totalFrames) {
          activeNotesMask = ledBuffer[targetFrameIndex].frameMask;
        } else {
          activeNotesMask = 0; // Out of bounds safety clamp (draws blank black space)
        }

        for (int col = 0; col < MATRIX_WIDTH; col++) {
          if ((activeNotesMask & ((uint64_t)1 << col)) != 0) {
            customDrawPixel(row, col, columnColors[col]);
          } else {
            // FIXED: Natively clear pixels by overwriting with black. 
            // This eliminates the need for the heavy matrix.clear() loop.
            customDrawPixel(row, col, matrix.Color(0, 0, 0)); 
          }
        }
      }
            matrix.show();
            activeChordTargetMask = activeNotesMask & ~((uint64_t)1 << 63);
            playerSatisfiedNotesMask = 0; // Reset player hits tracker for the new frame window
    
          }
          
    currentPlaybackFrame++;
    unsigned long executionOverhead = micros() - renderStartMicros;
    if (waitTime > executionOverhead) {
      waitTime -= executionOverhead;
    } else {
      waitTime = 0;
    }
    unsigned long startMicros = micros();
    while (micros() - startMicros < waitTime) {
      if (Serial.available() > 0) {
      char firstChar = Serial.peek();
      if (firstChar  == 'E') {
        String command = Serial.readStringUntil('\n');
        command.trim();
        if (command == "End") {
          matrix.fill(matrix.Color(0, 0, 0), 0, NUM_PIXELS);
          matrix.show();
          songLoaded = false;
          currentPlaybackFrame = 0;
          totalFrames = 0;
          return;
        }
      }
     }
     for (int trainStart = 0; trainStart < MATRIX_WIDTH; trainStart++) {
    
    topLight.clear(); // Wipe the background black before drawing the new frame
    backLight.clear();
    // Draw the train pixels
    for (int i = 0; i < MATRIX_WIDTH; i++) {
      int pixelIndex = trainStart + i;

      // Only light up pixels that physically fit on the visible topLight length
      if (pixelIndex >= 0 && pixelIndex < MATRIX_WIDTH) {
        
        // Distribute the rainbow colors along the length of the train
        // 65536 is the full range of Adafruit's 16-bit HSV color wheel
        uint16_t hue = (i * 65536) / MATRIX_WIDTH; 
        
        // Convert HSV to standard RGB and write to the individual pixel
        topLight.setPixelColor(pixelIndex, topLight.gamma32(topLight.ColorHSV(hue)));
        backLight.setPixelColor(MATRIX_WIDTH - 1 - pixelIndex, backLight.gamma32(backLight.ColorHSV(hue)));
      }
    }

    topLight.show();
    backLight.show();
  }
    }
  } else {
    free(ledBuffer);
    ledBuffer = NULL;
    songLoaded = false;
    currentPlaybackFrame = 0;
  }
  // fill_matrix();
  
  // customDrawPixel(row, col, matrix.Color(0, 0, 0)); 
  // matrix.setPixelColor(ledIndex, matrix.Color(0, 150, 0));
  // matrix.show();
  // // matrix.setPixelColor(10, 0, 150, 0);
  // // matrix.show();

  // customDrawPixel(rowIndex, columnIndex, matrix.Color(0, 150, 0));
  // Serial.print("Column index: ");
  // Serial.println(columnIndex);
  // Serial.print("Row index: ");
  // Serial.println(rowIndex);
  // if (columnIndex < MATRIX_WIDTH) {
  //   columnIndex++;
  // } else {
  //   columnIndex = 0;
  //   if (rowIndex < MATRIX_HEIGHT) {
  //     rowIndex ++;
  //   }
  //   else {
  //     columnIndex = 0;
  //     rowIndex = 0;
  //     matrix.fillScreen(matrix.Color(0, 150, 0));
  //     matrix.show();
  //     delay(2000);
  //   }
  // }
  // matrix.show();
}

// LED stuff

void front_and_back_leds() {

}