using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Records a recorded-footage clip (see <see cref="RecordedBodySource"/> and
/// Docs/Recorded-Footage.md) to a frame-exact PNG sequence with Unity Recorder, then encodes an
/// H.264 MP4 with ffmpeg.
///
/// In Play Mode it switches the clip to its frame sequence, starts the Recorder (pre-roll: the
/// Recorder takes a few frames to start capturing), restarts the clip, and stops exactly when
/// the last pass ends, so the output is N whole passes. Game time steps 1/30 s per captured
/// frame, however slowly the editor renders.
///
/// Gotchas this avoids (found the hard way):
/// - The Recorder's Cap FPS option stalls the editor at ~1 fps: left off.
/// - Its direct H.264 MP4 encoder backs up to ~1 fps after its buffer fills at 1080x1920:
///   PNGs are written instead (~6 fps) and ffmpeg encodes them.
/// - The VideoPlayer can't keep up with the Recorder's fixed clock (slow motion, repeats):
///   the clip plays from its extracted frame sequence instead.
/// </summary>
public class RecordedFootageRecorder : EditorWindow
{
    private const string PrefPrefix = "EnergyBall.RecordedFootageRecorder.";

    private int passes = 3;
    private int outputWidth = 1080;
    private bool waitForBall = true;
    private bool encodeMp4 = true;
    private int crf = 14;
    private string ffmpegPath = "ffmpeg";

    // Recording state (Play Mode only; lost when play ends).
    private enum Stage
    {
        Idle,
        WaitingForBall,
        PreRoll,
        Recording,
    }

    private Stage stage = Stage.Idle;
    private RecorderController controller;
    private RecordedBodySource source;
    private string outputFolder;
    private int startLoop;
    private bool restartIssued;
    private int firstFrameIndex = -1;
    private int preRollFrames;
    private int lastFrameCount;
    private double waitStarted;
    private string status = "";

    [MenuItem("EnergyBall/Recorded Footage Recorder")]
    private static void Open()
    {
        GetWindow<RecordedFootageRecorder>("Footage Recorder");
    }

    private void OnEnable()
    {
        passes = EditorPrefs.GetInt(PrefPrefix + "passes", 3);
        outputWidth = EditorPrefs.GetInt(PrefPrefix + "width", 1080);
        waitForBall = EditorPrefs.GetBool(PrefPrefix + "waitForBall", true);
        encodeMp4 = EditorPrefs.GetBool(PrefPrefix + "encode", true);
        crf = EditorPrefs.GetInt(PrefPrefix + "crf", 14);
        ffmpegPath = EditorPrefs.GetString(PrefPrefix + "ffmpeg", "ffmpeg");
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Recorded Footage → PNG frames → MP4", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Press Play with a recorded scene (RecordedBodySource enabled), tune it, then Record. "
                + "Needs <video>_frames next to the clip (see Docs/Recorded-Footage.md). Output "
                + "goes to Recordings/ in the project (gitignored).",
            MessageType.Info
        );

        using (new EditorGUI.DisabledScope(stage != Stage.Idle))
        {
            EditorGUI.BeginChangeCheck();
            passes = Mathf.Max(1, EditorGUILayout.IntField("Passes through the clip", passes));
            outputWidth = Mathf.Max(
                64,
                EditorGUILayout.IntField("Output width (px)", outputWidth) & ~1
            );
            waitForBall = EditorGUILayout.Toggle(
                new GUIContent(
                    "Wait for the ball",
                    "Start only once a player's ball has formed, so every pass shows it."
                ),
                waitForBall
            );
            encodeMp4 = EditorGUILayout.Toggle("Encode MP4 (ffmpeg)", encodeMp4);
            using (new EditorGUI.DisabledScope(!encodeMp4))
            {
                crf = EditorGUILayout.IntSlider(
                    new GUIContent("Quality (CRF)", "Lower = better. 14 is visually lossless."),
                    crf,
                    0,
                    30
                );
                ffmpegPath = EditorGUILayout.TextField("ffmpeg", ffmpegPath);
            }
            if (EditorGUI.EndChangeCheck())
                SavePrefs();
        }

        EditorGUILayout.Space();
        if (stage == Stage.Idle)
        {
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button(EditorApplication.isPlaying ? "Record" : "Record (enter Play Mode first)", GUILayout.Height(30)))
                    StartRecording();
            }
        }
        else if (GUILayout.Button("Cancel", GUILayout.Height(30)))
        {
            Cancel("Cancelled.");
        }

        if (!string.IsNullOrEmpty(status))
            EditorGUILayout.HelpBox(status, MessageType.None);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetInt(PrefPrefix + "passes", passes);
        EditorPrefs.SetInt(PrefPrefix + "width", outputWidth);
        EditorPrefs.SetBool(PrefPrefix + "waitForBall", waitForBall);
        EditorPrefs.SetBool(PrefPrefix + "encode", encodeMp4);
        EditorPrefs.SetInt(PrefPrefix + "crf", crf);
        EditorPrefs.SetString(PrefPrefix + "ffmpeg", ffmpegPath);
    }

    private void StartRecording()
    {
        source = FindFirstObjectByType<RecordedBodySource>();
        if (source == null || !source.enabled || SceneController.Instance == null)
        {
            SetStatus("No enabled RecordedBodySource in the playing scene.");
            return;
        }
        if (source.ClipFrameCount == 0)
        {
            SetStatus("The clip hasn't loaded (see the RecordedBodySource status / console).");
            return;
        }
        if (!source.SwitchToFrameSequence())
        {
            SetStatus(
                $"Couldn't load the frame sequence {source.FrameSequenceFolderName}. Extract it "
                    + "first (Docs/Recorded-Footage.md)."
            );
            return;
        }

        Vector2Int size = source.ClipSize;
        int height = Mathf.RoundToInt(outputWidth * (float)size.y / size.x) & ~1;
        string clipName = Path.GetFileNameWithoutExtension(source.videoFile);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        outputFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Recordings",
                $"EnergyBall_{clipName}_{passes}loops_{stamp}"
            )
            .Replace('\\', '/');

        var settings = CreateInstance<RecorderControllerSettings>();
        var images = CreateInstance<ImageRecorderSettings>();
        images.name = "Recorded Footage Frames";
        images.Enabled = true;
        images.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.PNG;
        images.imageInputSettings = new GameViewInputSettings
        {
            OutputWidth = outputWidth,
            OutputHeight = height,
        };
        images.OutputFile = outputFolder + "/frame_" + DefaultWildcard.Frame;
        settings.AddRecorderSettings(images);
        settings.SetRecordModeToManual();
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = 30f;
        settings.CapFrameRate = false; // Cap FPS stalls the editor at ~1 fps.
        controller = new RecorderController(settings);

        // Switching to the sequence restarts the clip too; passes are counted from the
        // deliberate restart after the pre-roll instead (see Tick).
        firstFrameIndex = -1;
        restartIssued = false;
        source.Looped += OnLooped;

        if (waitForBall && !BallFormed())
        {
            stage = Stage.WaitingForBall;
            waitStarted = EditorApplication.timeSinceStartup;
            SetStatus("Waiting for a player's ball to form…");
        }
        else
        {
            BeginPreRoll();
        }
    }

    private void BeginPreRoll()
    {
        controller.PrepareRecording();
        controller.StartRecording();
        stage = Stage.PreRoll;
        preRollFrames = 0;
        lastFrameCount = Time.frameCount;
        SetStatus($"Recording to {outputFolder}");
    }

    private void Tick()
    {
        if (stage == Stage.Idle || !EditorApplication.isPlaying)
            return;
        if (Time.frameCount == lastFrameCount && stage != Stage.WaitingForBall)
            return;
        lastFrameCount = Time.frameCount;

        switch (stage)
        {
            case Stage.WaitingForBall:
                if (BallFormed())
                    BeginPreRoll();
                else if (EditorApplication.timeSinceStartup - waitStarted > 20)
                    Cancel("No ball formed within 20 s (are the hands opening in the clip?).");
                break;
            case Stage.PreRoll:
                // The Recorder needs a few frames before it captures; restart the clip after.
                if (++preRollFrames == 15)
                {
                    startLoop = source.LoopCount;
                    restartIssued = true;
                    source.Restart();
                }
                break;
            case Stage.Recording:
                int done = source.LoopCount - startLoop - 1;
                SetStatus(
                    $"Recording pass {Mathf.Min(done + 1, passes)} / {passes} → {outputFolder}"
                );
                break;
        }
    }

    private void OnLooped(int count)
    {
        if (!restartIssued)
            return;
        if (count == startLoop + 1)
        {
            // The restart: this frame is the clip's frame 0.
            firstFrameIndex = RecorderFrameIndex();
            stage = Stage.Recording;
        }
        else if (count == startLoop + 1 + passes)
        {
            int endIndex = RecorderFrameIndex();
            Finish(firstFrameIndex, endIndex);
        }
    }

    private int RecorderFrameIndex()
    {
        var sessions = (IList)
            typeof(RecorderController)
                .GetField(
                    "m_RecordingSessions",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )
                .GetValue(controller);
        object session = sessions[0];
        return (int)session.GetType().GetProperty("frameIndex").GetValue(session);
    }

    private void Finish(int first, int end)
    {
        controller.StopRecording();
        source.Looped -= OnLooped;
        int frames = end - first;
        File.WriteAllText(Path.Combine(outputFolder, "range.txt"), $"{first} {end}");
        string message =
            $"Recorded {passes} pass(es): frames {first}..{end - 1} = {frames} "
            + $"(clip {source.ClipFrameCount} x {passes} = {source.ClipFrameCount * passes}).";
        Debug.Log("[Footage Recorder] " + message);
        stage = Stage.Idle;
        controller = null;

        if (encodeMp4)
            Encode(first, frames, message);
        else
            SetStatus(message + $"\nPNG frames: {outputFolder}");
    }

    private void Encode(int first, int frames, string recordedMessage)
    {
        string mp4 = outputFolder + ".mp4";
        string args =
            $"-hide_banner -loglevel error -y -framerate 30 -start_number {first} "
            + $"-i \"{outputFolder}/frame_%04d.png\" -frames:v {frames} -c:v libx264 -preset slow "
            + $"-crf {crf} -profile:v high -pix_fmt yuv420p -color_primaries bt709 "
            + $"-color_trc bt709 -colorspace bt709 -movflags +faststart \"{mp4}\"";
        SetStatus(recordedMessage + "\nEncoding MP4…");
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpegPath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };
            string errors = "";
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    errors += e.Data + "\n";
            };
            process.Exited += (_, _) =>
            {
                int code = process.ExitCode;
                EditorApplication.delayCall += () =>
                {
                    string result =
                        code == 0
                            ? $"MP4: {mp4}\nPNG frames (delete when done): {outputFolder}"
                            : $"ffmpeg failed ({code}): {errors}\nPNG frames: {outputFolder}";
                    Debug.Log("[Footage Recorder] " + result);
                    SetStatus(recordedMessage + "\n" + result);
                };
            };
            process.Start();
            process.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            SetStatus(
                recordedMessage
                    + $"\nCouldn't run ffmpeg ({e.Message}). Set its path above, or encode by hand:\n"
                    + $"ffmpeg {args}"
            );
        }
    }

    private bool BallFormed()
    {
        foreach (var player in SceneController.Instance.Players.Values)
        {
            if (player != null && player.GetComponent<PlayerConstructor>().beginInitialization)
                return true;
        }
        return false;
    }

    private void Cancel(string message)
    {
        if (controller != null && controller.IsRecording())
            controller.StopRecording();
        if (source != null)
            source.Looped -= OnLooped;
        controller = null;
        stage = Stage.Idle;
        SetStatus(message + (string.IsNullOrEmpty(outputFolder) ? "" : $"\nPartial frames: {outputFolder}"));
    }

    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode && stage != Stage.Idle)
            Cancel("Play Mode ended before the recording finished.");
    }

    private void SetStatus(string message)
    {
        status = message;
        Repaint();
    }
}
