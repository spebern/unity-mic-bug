using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
#if UNITY_ANDROID
using UnityEngine.Android; // Required for Android permissions
#endif

public class SimpleAudioRecorder : MonoBehaviour
{
    private bool isRecording = false;
    private string deviceName;
    private string savePath;

    private string[] microphoneDevices = new string[0]; // Initialize as empty
    private int currentMicDeviceIndex = 0;
    private Button micDeviceButton;
    private Text micDeviceText;

    private int currentSampleRateIndex = 1;
    private int[] sampleRates = new int[] { 8000, 16000, 22050, 44100, 48000 };
    private string[] sampleRateLabels = new string[] { "8 kHz", "16 kHz", "22.05 kHz", "44.1 kHz", "48 kHz" };

    private AudioClip recordingClip;
    private List<float> recordedSamples;
    private int micPosition = 0;

    private Canvas canvas;
    private Button recordButton;
    private Text recordButtonText;
    private Button sampleRateButton;
    private Text sampleRateText;
    private Button playButton;
    private Text playButtonText;
    private Text infoText;
    private Text recordingTimeText;

    private int maxRecordingSeconds = 10;

    private AudioSource audioSource;
    private string lastSavedFilePath;
    private Coroutine playCoroutine;

    void Start()
    {
        savePath = Path.Combine(Application.persistentDataPath, "Recordings");
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        recordedSamples = new List<float>();

        // Create UI structure first. It will show "Initializing Mic..." initially.
        CreateUI();

        // Start the initialization sequence which includes permission handling.
        StartCoroutine(InitializeWithPermissions());

        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }

    private IEnumerator InitializeWithPermissions()
    {
        // Set initial UI states that don't depend on permissions yet
        UpdateInfoText(); // Shows "Initializing" or similar based on current deviceName
        if(sampleRateButton != null) sampleRateButton.interactable = false; // Disable until mics confirmed
        UpdateMicrophoneButtonState(); // Will show "Initializing" or "No Mics" based on current microphoneDevices

        #if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("Requesting microphone permission...");
            if (infoText != null) { infoText.text = "Requesting Mic Permission..."; infoText.color = Color.yellow;}
            Permission.RequestUserPermission(Permission.Microphone);

            float timeWaited = 0f;
            // Wait for up to 10 seconds for the user to respond or for permission to be granted.
            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) && timeWaited < 10.0f)
            {
                yield return null; // Wait for the next frame
                timeWaited += Time.deltaTime;
            }
        }
        #endif

        // Now, re-check/populate microphone devices
        microphoneDevices = Microphone.devices ?? new string[0]; // Ensure it's not null

        if (microphoneDevices.Length > 0)
        {
            // If deviceName was previously null or isn't in the new list, reset it.
            if (string.IsNullOrEmpty(deviceName) || !microphoneDevices.Contains(deviceName))
            {
                currentMicDeviceIndex = 0;
                deviceName = microphoneDevices[currentMicDeviceIndex];
            } else { // deviceName was already set and is still valid, find its index
                currentMicDeviceIndex = Array.IndexOf(microphoneDevices, deviceName);
                if(currentMicDeviceIndex == -1) { // Should not happen if contains check passed, but defensive
                    currentMicDeviceIndex = 0;
                    deviceName = microphoneDevices[currentMicDeviceIndex];
                }
            }
            Debug.Log("Microphones initialized. Count: " + microphoneDevices.Length + ". Selected: " + deviceName);
            LogDeviceCaps(deviceName);
        }
        else
        {
            Debug.LogError("No microphone found or permission denied!");
            deviceName = null;
        }

        // Update UI elements that depend on the microphone list
        UpdateMicrophoneButtonState();
        if(sampleRateButton != null) sampleRateButton.interactable = (microphoneDevices.Length > 0 && !string.IsNullOrEmpty(deviceName));
        UpdateInfoText(); // Update status based on new mic info
    }


    void Update()
    {
        if (isRecording)
        {
            ProcessMicrophoneData();

            int samplesSoFar = recordedSamples.Count;
            int maxExpectedSamples = (sampleRates.Length > currentSampleRateIndex && currentSampleRateIndex >=0) ?
                                     sampleRates[currentSampleRateIndex] * maxRecordingSeconds : 0;
            if (samplesSoFar > maxExpectedSamples && maxExpectedSamples > 0) samplesSoFar = maxExpectedSamples;

            int recordingTimeInSeconds = (samplesSoFar > 0 && sampleRates.Length > currentSampleRateIndex && currentSampleRateIndex >=0 && sampleRates[currentSampleRateIndex] > 0) ?
                                         samplesSoFar / sampleRates[currentSampleRateIndex] : 0;
            int recordingTimeMinutes = recordingTimeInSeconds / 60;
            int recordingTimeSecondsValue = recordingTimeInSeconds % 60;
            if (recordingTimeText != null)
            {
                recordingTimeText.text = $"Rec: {recordingTimeMinutes:00}:{recordingTimeSecondsValue:00}";
            }
        }

        if (audioSource != null && audioSource.clip != null && !audioSource.isPlaying && playButtonText != null && playButtonText.text == "Stop")
        {
            if (playCoroutine == null)
            {
                 playButtonText.text = "Play Last";
                 UpdateInfoText();
            }
        }
    }
    private void ProcessMicrophoneData()
    {
        if (string.IsNullOrEmpty(deviceName) || !Microphone.IsRecording(deviceName) || recordingClip == null)
        {
            return;
        }
        int currentPosition = Microphone.GetPosition(deviceName);
        if (currentPosition == micPosition || recordingClip.samples == 0) return;

        int samplesToRead;
        if (currentPosition < micPosition) {
            samplesToRead = (recordingClip.samples - micPosition);
            if (samplesToRead > 0) {
                float[] tempBuffer = new float[samplesToRead];
                recordingClip.GetData(tempBuffer, micPosition);
                recordedSamples.AddRange(tempBuffer);
            }
            if (currentPosition > 0) {
                float[] tempBuffer2 = new float[currentPosition];
                recordingClip.GetData(tempBuffer2, 0);
                recordedSamples.AddRange(tempBuffer2);
            }
        } else {
            samplesToRead = currentPosition - micPosition;
            if (samplesToRead > 0) {
                float[] tempBuffer = new float[samplesToRead];
                recordingClip.GetData(tempBuffer, micPosition);
                recordedSamples.AddRange(tempBuffer);
            }
        }
        micPosition = currentPosition;
        if (sampleRates.Length > currentSampleRateIndex && currentSampleRateIndex >= 0 &&
            recordedSamples.Count >= sampleRates[currentSampleRateIndex] * maxRecordingSeconds) {
             StopRecording();
        }
    }

    private static void LogDeviceCaps(string specificDeviceName = null)
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0) { /*Debug.Log("No microphone devices to log caps for.");*/ return; }
        if (!string.IsNullOrEmpty(specificDeviceName) && Microphone.devices.Contains(specificDeviceName)) {
            Microphone.GetDeviceCaps(specificDeviceName, out var minFreq, out var maxFreq);
            Debug.Log($"Caps for '{specificDeviceName}': SRates: {minFreq}-{maxFreq}Hz");
        }
    }

    private void CreateUI()
    {
        // ... (CreateUI method with adjusted sizes from previous response) ...
        // Panel and element sizes are from the previous iteration where we made them more compact.
        GameObject canvasObj = new GameObject("RecorderCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(360, 600);
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(canvas.transform, false);
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(320, 370);
        panelRect.anchoredPosition = Vector2.zero;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) Debug.LogWarning("Default font 'LegacyRuntime.ttf' not found.");

        float currentY = -10f;
        float rowHeight = 30f;
        float buttonRowHeight = 40f;
        float infoRowHeight = 25f;
        float rowSpacing = 8f;
        float sideMarginRatio = 0.05f;
        float labelWidthRatio = 0.25f;

        Text titleText = CreateTextElement(panelObj.transform, "Title", "Audio Recorder", 20, TextAnchor.MiddleCenter, font, Color.white);
        SetRectTransformAnchoredTop(titleText.GetComponent<RectTransform>(), currentY, rowHeight, sideMarginRatio, 1f - sideMarginRatio);
        currentY -= (rowHeight + rowSpacing);

        Text micLabel = CreateTextElement(panelObj.transform, "MicLabel", "Mic:", 16, TextAnchor.MiddleLeft, font, Color.white);
        SetRectTransformAnchoredTop(micLabel.GetComponent<RectTransform>(), currentY, rowHeight, sideMarginRatio, sideMarginRatio + labelWidthRatio);

        micDeviceButton = CreateButtonElement(panelObj.transform, "MicCycleButton", CycleMicrophoneDevice, new Color(0.25f, 0.25f, 0.3f));
        SetRectTransformAnchoredTop(micDeviceButton.GetComponent<RectTransform>(), currentY, rowHeight, sideMarginRatio + labelWidthRatio + 0.01f, 1f - sideMarginRatio);
        micDeviceText = CreateTextElement(micDeviceButton.transform, "MicNameText", "Initializing Mic...", 14, TextAnchor.MiddleLeft, font, Color.white);
        RectTransform micTextRect = micDeviceText.GetComponent<RectTransform>();
        FitRectToParent(micTextRect);
        micTextRect.offsetMin = new Vector2(5, micTextRect.offsetMin.y);
        micTextRect.offsetMax = new Vector2(-5, micTextRect.offsetMax.y);
        currentY -= (rowHeight + rowSpacing);

        Text rateLabel = CreateTextElement(panelObj.transform, "RateLabel", "Rate:", 16, TextAnchor.MiddleLeft, font, Color.white);
        SetRectTransformAnchoredTop(rateLabel.GetComponent<RectTransform>(), currentY, rowHeight, sideMarginRatio, sideMarginRatio + labelWidthRatio);

        sampleRateButton = CreateButtonElement(panelObj.transform, "RateCycleButton", CycleSampleRate, new Color(0.3f, 0.3f, 0.3f));
        SetRectTransformAnchoredTop(sampleRateButton.GetComponent<RectTransform>(), currentY, rowHeight, sideMarginRatio + labelWidthRatio + 0.01f, 1f - sideMarginRatio);
        sampleRateText = CreateTextElement(sampleRateButton.transform, "RateText", "N/A", 16, TextAnchor.MiddleCenter, font, Color.white);
        FitRectToParent(sampleRateText.GetComponent<RectTransform>());
        if (sampleRateLabels.Length > currentSampleRateIndex && currentSampleRateIndex >= 0)
            sampleRateText.text = sampleRateLabels[currentSampleRateIndex];
        currentY -= (rowHeight + rowSpacing);

        infoText = CreateTextElement(panelObj.transform, "InfoText", "", 14, TextAnchor.MiddleCenter, font, Color.yellow);
        SetRectTransformAnchoredTop(infoText.GetComponent<RectTransform>(), currentY, infoRowHeight, sideMarginRatio, 1f-sideMarginRatio);
        currentY -= (infoRowHeight + rowSpacing);

        recordingTimeText = CreateTextElement(panelObj.transform, "RecordingTimeText", "Rec: 00:00", 14, TextAnchor.MiddleCenter, font, new Color(0.9f, 0.4f, 0.4f));
        SetRectTransformAnchoredTop(recordingTimeText.GetComponent<RectTransform>(), currentY, infoRowHeight, sideMarginRatio, 1f-sideMarginRatio);
        currentY -= (infoRowHeight + rowSpacing + 2f);

        recordButton = CreateButtonElement(panelObj.transform, "RecordButton", ToggleRecording, new Color(0.7f, 0.7f, 0.7f));
        SetRectTransformAnchoredTop(recordButton.GetComponent<RectTransform>(), currentY, buttonRowHeight, sideMarginRatio, 1f-sideMarginRatio);
        recordButtonText = CreateTextElement(recordButton.transform, "RecordText", "Start Rec", 18, TextAnchor.MiddleCenter, font, Color.black);
        FitRectToParent(recordButtonText.GetComponent<RectTransform>());
        currentY -= (buttonRowHeight + rowSpacing);

        playButton = CreateButtonElement(panelObj.transform, "PlayButton", PlayLastRecording, new Color(0.5f, 0.7f, 0.5f));
        SetRectTransformAnchoredTop(playButton.GetComponent<RectTransform>(), currentY, buttonRowHeight, sideMarginRatio, 1f-sideMarginRatio);
        playButtonText = CreateTextElement(playButton.transform, "PlayText", "Play Last", 18, TextAnchor.MiddleCenter, font, Color.black);
        FitRectToParent(playButtonText.GetComponent<RectTransform>());
        if(playButton != null) playButton.interactable = false;
    }

    private void SetRectTransformAnchoredTop(RectTransform rt, float yPosFromTop, float height, float xAnchorMin, float xAnchorMax)
    {
        rt.anchorMin = new Vector2(xAnchorMin, 1);
        rt.anchorMax = new Vector2(xAnchorMax, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, yPosFromTop);
        rt.sizeDelta = new Vector2(0, height);
    }

    private Text CreateTextElement(Transform parent, string name, string text, int fontSize, TextAnchor alignment, Font font, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Text txt = obj.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.alignment = alignment;
        if (font != null) txt.font = font;
        txt.color = color;
        return txt;
    }

    private Button CreateButtonElement(Transform parent, string name, UnityEngine.Events.UnityAction onClickAction, Color bgColor)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = bgColor;
        Button btn = obj.AddComponent<Button>();
        btn.onClick.AddListener(onClickAction);
        return btn;
    }

    private void FitRectToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    private void UpdateMicrophoneButtonState()
    {
        if (micDeviceButton != null)
        {
            bool hasMics = microphoneDevices != null && microphoneDevices.Length > 0;
            micDeviceButton.interactable = hasMics && !isRecording;
            if (micDeviceText != null)
            {
                if (hasMics && currentMicDeviceIndex < microphoneDevices.Length && currentMicDeviceIndex >= 0)
                {
                    micDeviceText.text = microphoneDevices[currentMicDeviceIndex];
                }
                else if (Application.platform == RuntimePlatform.Android && !Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                     micDeviceText.text = "Mic Permission Needed";
                }
                 else
                {
                    micDeviceText.text = "No Microphones";
                }
            }
        }
    }

    private void UpdateInfoText()
    {
        if (infoText == null) return;
        bool hasMics = microphoneDevices != null && microphoneDevices.Length > 0;
        bool micPermissionGranted = true; // Assume true for non-Android or if already checked
        #if UNITY_ANDROID
        micPermissionGranted = Permission.HasUserAuthorizedPermission(Permission.Microphone);
        #endif

        if (!micPermissionGranted && Application.platform == RuntimePlatform.Android) {
            infoText.text = "Tap Mic button to grant permission."; infoText.color = Color.yellow;
        } else if (!hasMics) {
            infoText.text = "Error: No Microphones Found!"; infoText.color = Color.red;
        } else if (string.IsNullOrEmpty(deviceName)) {
            infoText.text = "Initializing Mic..."; infoText.color = Color.yellow; // Changed from "Select Microphone"
        } else {
            infoText.text = "Ready";  infoText.color = Color.green;
        }
    }

    private void CycleMicrophoneDevice()
    {
        // On Android, if permission is not granted, this button click can also trigger the permission request.
        #if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) {
            StartCoroutine(InitializeWithPermissions()); // Re-trigger permission flow
            return;
        }
        #endif

        if (isRecording || microphoneDevices == null || microphoneDevices.Length == 0) return;

        currentMicDeviceIndex = (currentMicDeviceIndex + 1) % microphoneDevices.Length;
        deviceName = microphoneDevices[currentMicDeviceIndex];
        UpdateMicrophoneButtonState();

        Debug.Log("Switched to microphone: " + deviceName);
        LogDeviceCaps(deviceName);
        UpdateInfoText();
    }

    private void CycleSampleRate()
    {
        if (isRecording || microphoneDevices == null || microphoneDevices.Length == 0) return;
        currentSampleRateIndex = (currentSampleRateIndex + 1) % sampleRates.Length;
        if (sampleRateText != null) sampleRateText.text = sampleRateLabels[currentSampleRateIndex];
        UpdateInfoText();
        Debug.Log("Sample rate: " + sampleRates[currentSampleRateIndex] + " Hz");
    }

    private void ToggleRecording()
    {
        bool hasMics = microphoneDevices != null && microphoneDevices.Length > 0;
        if (!hasMics || string.IsNullOrEmpty(deviceName)) {
            Debug.LogError("Cannot record: No mic or device not selected.");
            if (recordButtonText != null) recordButtonText.text = "No Mic";
            UpdateInfoText();
            // If no mic due to permission, prompt again by simulating mic button click logic
            #if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) {
                 StartCoroutine(InitializeWithPermissions());
            }
            #endif
            return;
        }
        if (!isRecording) StartRecording(); else StopRecording();
    }

    private void StartRecording()
    {
        if (audioSource != null && audioSource.isPlaying) {
            audioSource.Stop();
            if (playCoroutine != null) { StopCoroutine(playCoroutine); playCoroutine = null; }
            if (playButtonText != null) playButtonText.text = "Play Last";
        }
        int sampleRate = sampleRates[currentSampleRateIndex];
        recordedSamples.Clear(); micPosition = 0;
        LogDeviceCaps(deviceName);
        recordingClip = Microphone.Start(deviceName, true, maxRecordingSeconds, sampleRate);
        if (recordingClip == null) {
            Debug.LogError($"Failed to start mic '{deviceName}' at {sampleRate}Hz.");
            if (recordButtonText != null) recordButtonText.text = "Mic Error";
            UpdateInfoText(); return;
        }
        isRecording = true;
        if (recordButtonText != null) recordButtonText.text = "Stop Rec";
        if (recordButton != null) recordButton.GetComponent<Image>().color = new Color(1f, 0.5f, 0.5f);
        if (recordingTimeText != null) recordingTimeText.text = "Rec: 00:00";
        if (sampleRateButton != null) sampleRateButton.interactable = false;
        UpdateMicrophoneButtonState();
        if (playButton != null) playButton.interactable = false;
        if(infoText != null) { infoText.text = "Recording..."; infoText.color = Color.cyan; }
    }

    private void StopRecording()
    {
        if (!isRecording) return;
        if (!string.IsNullOrEmpty(deviceName) && Microphone.IsRecording(deviceName)) {
            ProcessMicrophoneData(); Microphone.End(deviceName);
        } else if (recordingClip != null && !string.IsNullOrEmpty(deviceName)) { ProcessMicrophoneData(); }
        isRecording = false; Debug.Log($"Stopped. Samples: {recordedSamples.Count}");
        int maxExpectedSamples = (sampleRates.Length > currentSampleRateIndex && currentSampleRateIndex >=0) ?
                                 sampleRates[currentSampleRateIndex] * maxRecordingSeconds : 0;
        if (recordedSamples.Count > maxExpectedSamples && maxExpectedSamples > 0) {
             recordedSamples.RemoveRange(maxExpectedSamples, recordedSamples.Count - maxExpectedSamples);
        }
        if (recordButtonText != null) recordButtonText.text = "Start Rec";
        if (recordButton != null) recordButton.GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.7f);
        bool canInteract = (microphoneDevices != null && microphoneDevices.Length > 0);
        if (sampleRateButton != null) sampleRateButton.interactable = canInteract;
        UpdateMicrophoneButtonState(); UpdateInfoText();
        if (recordedSamples.Count > 0) SaveRecording();
        else {
            if (playButtonText != null) playButtonText.text = "Play Last";
            if (playButton != null) playButton.interactable = false;
        }
    }

    private void SaveRecording()
    {
        if (recordedSamples == null || recordedSamples.Count == 0) return;
        try {
            string safeDeviceNamePart = "UnknownMic";
            if (!string.IsNullOrEmpty(deviceName)) {
                char[] invalidChars = Path.GetInvalidFileNameChars();
                string tempDeviceName = new string(deviceName.Where(c => !invalidChars.Contains(c)).ToArray());
                safeDeviceNamePart = tempDeviceName.Length > 10 ? tempDeviceName.Substring(0, 10) : tempDeviceName;
                if (string.IsNullOrWhiteSpace(safeDeviceNamePart)) safeDeviceNamePart = "Mic";
            }
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"Rec_{timestamp}_{safeDeviceNamePart}_{sampleRates[currentSampleRateIndex] / 1000}k.wav";
            lastSavedFilePath = Path.Combine(savePath, filename);
            Debug.Log($"Saving to {lastSavedFilePath}");
            using (FileStream fs = new FileStream(lastSavedFilePath, FileMode.Create)) {
                fs.Write(new byte[44], 0, 44);
                WriteWavData(fs, recordedSamples.ToArray());
                WriteWavHeader(fs, 1, sampleRates[currentSampleRateIndex], recordedSamples.Count);
            }
            Debug.Log("Saved successfully.");
            if (recordButtonText != null) recordButtonText.text = "Saved!";
            Invoke("ResetButtonTextAfterSave", 1.5f);
            if (playButton != null) playButton.interactable = true;
        } catch (Exception e) {
            Debug.LogError($"Save Error: {e.Message}\n{e.StackTrace}");
            if (infoText != null) { infoText.text = "Save Error!"; infoText.color = Color.red; }
        }
    }

    private void ResetButtonTextAfterSave()
    {
        if (!isRecording && recordButtonText != null) recordButtonText.text = "Start Rec";
        UpdateInfoText();
    }

    private void PlayLastRecording()
    {
        if (string.IsNullOrEmpty(lastSavedFilePath) || !File.Exists(lastSavedFilePath)) {
            if (infoText != null) { infoText.text = "No file to play."; infoText.color = Color.yellow; }
            return;
        }
        if (audioSource != null && audioSource.isPlaying) {
            audioSource.Stop();
            if (playCoroutine != null) { StopCoroutine(playCoroutine); playCoroutine = null; }
            if (playButtonText != null) playButtonText.text = "Play Last";
            UpdateInfoText();
        } else {
            if (playCoroutine != null) StopCoroutine(playCoroutine);
            playCoroutine = StartCoroutine(LoadAndPlayAudio(lastSavedFilePath));
        }
    }

    private IEnumerator LoadAndPlayAudio(string path)
    {
        if (audioSource == null) {
            Debug.LogWarning("AudioSource was null. Re-fetching.");
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            if (audioSource == null) {
                Debug.LogError("CRITICAL: AudioSource null & could not be initialized. Playback aborted.");
                if (playButtonText != null) playButtonText.text = "Play Error";
                if (infoText != null) { infoText.text = "Playback System Error!"; infoText.color = Color.red; }
                playCoroutine = null; yield break;
            }
        }

        // Use Uri class to correctly format the file path for UnityWebRequest
        string url = new Uri(path).AbsoluteUri;
        Debug.Log("Attempting to load audio from URL: " + url);

        if (infoText != null) { infoText.text = "Loading audio..."; infoText.color = Color.white; }
        if (playButtonText != null) playButtonText.text = "Loading...";

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV)) {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success) { // Check result for newer Unity versions
                Debug.LogError($"Load Error ({www.result}): {www.error} from URL {url}"); // Log URL
                if (playButtonText != null) playButtonText.text = "Load Error";
                if (infoText != null) { infoText.text = "Audio Load Error!"; infoText.color = Color.red; }
            } else {
                AudioClip clipToPlay = DownloadHandlerAudioClip.GetContent(www);
                if (clipToPlay != null) {
                    float loadStartTime = Time.unscaledTime;
                    while (clipToPlay.loadState == AudioDataLoadState.Loading && (Time.unscaledTime - loadStartTime) < 5f) {
                        yield return null;
                    }
                    if (clipToPlay.loadState == AudioDataLoadState.Loaded) {
                        audioSource.clip = clipToPlay; audioSource.Play();
                        if (playButtonText != null) playButtonText.text = "Stop";
                        if (infoText != null) { infoText.text = "Playing..."; infoText.color = Color.cyan; }
                    } else {
                        Debug.LogError($"Clip load state error: {clipToPlay.loadState} for URL {url}");
                        if (playButtonText != null) playButtonText.text = "Play Error";
                        if (infoText != null) { infoText.text = "Audio Data Error!"; infoText.color = Color.red; }
                    }
                } else {
                    Debug.LogError($"GetContent returned null for URL {url}.");
                    if (playButtonText != null) playButtonText.text = "Play Error";
                    if (infoText != null) { infoText.text = "Audio Content Error!"; infoText.color = Color.red; }
                }
            }
        }
        playCoroutine = null;
        if (audioSource == null || !audioSource.isPlaying) {
            if (playButtonText != null && (playButtonText.text == "Stop" || playButtonText.text == "Loading...")) playButtonText.text = "Play Last";
            UpdateInfoText();
        }
    }

    private void WriteWavData(FileStream fs, float[] audioData) {
        short[] intData = new short[audioData.Length];
        for (int i = 0; i < audioData.Length; i++) {
            intData[i] = (short)(Mathf.Clamp(audioData[i], -1f, 1f) * 32767.0f);
        }
        byte[] byteData = new byte[intData.Length * 2];
        Buffer.BlockCopy(intData, 0, byteData, 0, byteData.Length);
        fs.Write(byteData, 0, byteData.Length);
    }
    private void WriteWavHeader(FileStream fs, int channels, int currentSampleRate, int samples) {
        fs.Seek(0, SeekOrigin.Begin);
        fs.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"), 0, 4);
        int subchunk2Size = samples * channels * 2;
        int chunkSize = 36 + subchunk2Size;
        fs.Write(BitConverter.GetBytes(chunkSize), 0, 4);
        fs.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"), 0, 4);
        fs.Write(System.Text.Encoding.UTF8.GetBytes("fmt "), 0, 4);
        fs.Write(BitConverter.GetBytes(16), 0, 4);
        fs.Write(BitConverter.GetBytes((ushort)1), 0, 2);
        fs.Write(BitConverter.GetBytes((ushort)channels), 0, 2);
        fs.Write(BitConverter.GetBytes(currentSampleRate), 0, 4);
        fs.Write(BitConverter.GetBytes(currentSampleRate * channels * 2), 0, 4);
        fs.Write(BitConverter.GetBytes((ushort)(channels * 2)), 0, 2);
        fs.Write(BitConverter.GetBytes((ushort)16), 0, 2);
        fs.Write(System.Text.Encoding.UTF8.GetBytes("data"), 0, 4);
        fs.Write(BitConverter.GetBytes(subchunk2Size), 0, 4);
    }
    void OnDestroy() {
        if (isRecording && !string.IsNullOrEmpty(deviceName) && Microphone.IsRecording(deviceName)) {
            Microphone.End(deviceName);
        }
        if (audioSource != null && audioSource.isPlaying) { audioSource.Stop(); }
        if (playCoroutine != null) { StopCoroutine(playCoroutine); playCoroutine = null; }
    }
}