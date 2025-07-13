using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System.Collections;

[RequireComponent(typeof(Animator))]
public class VFXAnimationBlender : MonoBehaviour
{
    [Header("Animation Clips")]
    public AnimationClip emptyClip;
    public AnimationClip initClip;
    public AnimationClip openClip;
    public AnimationClip closeClip;

    [Header("Blending Settings")]
    public AnimationCurve closeToOpenBlendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve openToCloseBlendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Timing Settings")]
    [Range(0f, 2f)]
    public float closeToOpenDelay = 0f;
    [Range(0f, 2f)]
    public float openToCloseDelay = 0f;

    [Header("Transition Settings")]
    [Range(0.1f, 2f)]
    public float blendDuration = 0.5f;
    [Range(0f, 1f)]
    public float initToOpenTransitionPoint = 0.8f; // When to start blending to open (0.8 = 80% through init)

    // Playable Graph components
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mainMixer;
    private AnimationClipPlayable emptyPlayable;
    private AnimationClipPlayable initPlayable;
    private AnimationClipPlayable openPlayable;
    private AnimationClipPlayable closePlayable;

    // State management
    private enum AnimationState
    {
        Empty,
        Init,
        BlendingInitToOpen,
        Open,
        BlendingToClose,
        Close
    }

    private AnimationState currentState = AnimationState.Empty;
    private float lastCloseFinishTime = -10f; // Initialize to old time
    private bool isBlending = false;
    private Coroutine currentBlendCoroutine;

    void Start()
    {
        SetupPlayableGraph();
        SetInitialState();
    }

    void SetupPlayableGraph()
    {
        // Create the playable graph
        playableGraph = PlayableGraph.Create();
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        // Create the main mixer with 4 inputs (empty, init, open, close)
        mainMixer = AnimationMixerPlayable.Create(playableGraph, 4);

        // Create individual clip playables
        emptyPlayable = AnimationClipPlayable.Create(playableGraph, emptyClip);
        initPlayable = AnimationClipPlayable.Create(playableGraph, initClip);
        openPlayable = AnimationClipPlayable.Create(playableGraph, openClip);
        closePlayable = AnimationClipPlayable.Create(playableGraph, closeClip);

        // Connect clip playables to mixer inputs
        playableGraph.Connect(emptyPlayable, 0, mainMixer, 0);  // Input 0: Empty
        playableGraph.Connect(initPlayable, 0, mainMixer, 1);   // Input 1: Init
        playableGraph.Connect(openPlayable, 0, mainMixer, 2);   // Input 2: Open
        playableGraph.Connect(closePlayable, 0, mainMixer, 3);  // Input 3: Close

        // Create output and connect to animator
        var animationOutput = AnimationPlayableOutput.Create(playableGraph, "VFX Animation", GetComponent<Animator>());
        animationOutput.SetSourcePlayable(mainMixer);

        // Start the graph
        playableGraph.Play();
    }

    void SetInitialState()
    {
        // Set empty clip to full weight, others to 0
        mainMixer.SetInputWeight(0, 1f); // Empty
        mainMixer.SetInputWeight(1, 0f); // Init
        mainMixer.SetInputWeight(2, 0f); // Open
        mainMixer.SetInputWeight(3, 0f); // Close

        currentState = AnimationState.Empty;
    }

    // Public function to trigger hand open
    public void TriggerHandOpen()
    {
        if (currentBlendCoroutine != null)
        {
            StopCoroutine(currentBlendCoroutine);
        }

        float timeSinceCloseFinish = Time.time - lastCloseFinishTime;

        // Determine whether to use init or skip directly to open
        if (timeSinceCloseFinish >= 2f || currentState == AnimationState.Empty)
        {
            // Use init -> open transition
            currentBlendCoroutine = StartCoroutine(TransitionToInitThenOpen());
        }
        else
        {
            // Skip init, blend directly from close to open
            currentBlendCoroutine = StartCoroutine(TransitionDirectlyToOpen());
        }
    }

    // Public function to trigger hand close
    public void TriggerHandClose()
    {
        if (currentBlendCoroutine != null)
        {
            StopCoroutine(currentBlendCoroutine);
        }

        currentBlendCoroutine = StartCoroutine(TransitionToClose());
    }

    private IEnumerator TransitionToInitThenOpen()
    {
        // Wait for delay if specified
        if (closeToOpenDelay > 0f)
        {
            yield return new WaitForSeconds(closeToOpenDelay);
        }

        // Immediately switch to init (no blending from empty)
        if (currentState == AnimationState.Empty)
        {
            SwitchToInit();
        }
        else
        {
            // Blend from current state to init
            yield return StartCoroutine(BlendToClip(1, blendDuration, closeToOpenBlendCurve));
        }

        // Wait for init to reach transition point
        yield return StartCoroutine(WaitForInitTransitionPoint());

        // Blend from init to open
        currentState = AnimationState.BlendingInitToOpen;
        yield return StartCoroutine(BlendToClip(2, blendDuration, closeToOpenBlendCurve));

        currentState = AnimationState.Open;
        currentBlendCoroutine = null;
    }

    private IEnumerator TransitionDirectlyToOpen()
    {
        // Wait for delay if specified
        if (closeToOpenDelay > 0f)
        {
            yield return new WaitForSeconds(closeToOpenDelay);
        }

        // Blend directly from close to open
        yield return StartCoroutine(BlendToClip(2, blendDuration, closeToOpenBlendCurve));

        currentState = AnimationState.Open;
        currentBlendCoroutine = null;
    }

    private IEnumerator TransitionToClose()
    {
        // Wait for delay if specified
        if (openToCloseDelay > 0f)
        {
            yield return new WaitForSeconds(openToCloseDelay);
        }

        // Blend to close from whatever state we're in
        yield return StartCoroutine(BlendToClip(3, blendDuration, openToCloseBlendCurve));

        currentState = AnimationState.Close;

        // Wait for close animation to finish
        yield return StartCoroutine(WaitForClipToFinish(closePlayable));

        // Record the time when close finished
        lastCloseFinishTime = Time.time;
        currentBlendCoroutine = null;
    }

    private void SwitchToInit()
    {
        // Immediate switch to init (no blending)
        mainMixer.SetInputWeight(0, 0f); // Empty
        mainMixer.SetInputWeight(1, 1f); // Init
        mainMixer.SetInputWeight(2, 0f); // Open
        mainMixer.SetInputWeight(3, 0f); // Close

        // Reset init clip time
        initPlayable.SetTime(0);
        currentState = AnimationState.Init;
    }

    private IEnumerator BlendToClip(int targetClipIndex, float duration, AnimationCurve blendCurve)
    {
        isBlending = true;
        float elapsedTime = 0f;

        // Store initial weights
        float[] startWeights = new float[4];
        for (int i = 0; i < 4; i++)
        {
            startWeights[i] = mainMixer.GetInputWeight(i);
        }

        // Reset target clip time
        switch (targetClipIndex)
        {
            case 1: initPlayable.SetTime(0); break;
            case 2: openPlayable.SetTime(0); break;
            case 3: closePlayable.SetTime(0); break;
        }

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float normalizedTime = elapsedTime / duration;
            float curveValue = blendCurve.Evaluate(normalizedTime);

            // Blend weights
            for (int i = 0; i < 4; i++)
            {
                if (i == targetClipIndex)
                {
                    mainMixer.SetInputWeight(i, Mathf.Lerp(startWeights[i], 1f, curveValue));
                }
                else
                {
                    mainMixer.SetInputWeight(i, Mathf.Lerp(startWeights[i], 0f, curveValue));
                }
            }

            yield return null;
        }

        // Ensure final weights are exact
        for (int i = 0; i < 4; i++)
        {
            mainMixer.SetInputWeight(i, i == targetClipIndex ? 1f : 0f);
        }

        isBlending = false;
    }

    private IEnumerator WaitForInitTransitionPoint()
    {
        while (true)
        {
            float initProgress = (float)(initPlayable.GetTime() / initClip.length);
            if (initProgress >= initToOpenTransitionPoint)
            {
                break;
            }
            yield return null;
        }
    }

    private IEnumerator WaitForClipToFinish(AnimationClipPlayable clipPlayable)
    {
        AnimationClip clip = clipPlayable.GetAnimationClip();
        while (clipPlayable.GetTime() < clip.length)
        {
            yield return null;
        }
    }

    // Debug information
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TriggerHandOpen();
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            TriggerHandClose();
        }
    }

    void OnDisable()
    {
        if (playableGraph.IsValid())
        {
            playableGraph.Destroy();
        }
    }

    // Debug helper to show current state
    void OnGUI()
    {
        GUILayout.Label($"Current State: {currentState}");
        GUILayout.Label($"Is Blending: {isBlending}");
        GUILayout.Label($"Time since close finish: {Time.time - lastCloseFinishTime:F1}s");
        GUILayout.Label("Press O for HandOpen, C for HandClose");

        // Show current weights
        if (playableGraph.IsValid())
        {
            GUILayout.Label($"Empty: {mainMixer.GetInputWeight(0):F2}");
            GUILayout.Label($"Init: {mainMixer.GetInputWeight(1):F2}");
            GUILayout.Label($"Open: {mainMixer.GetInputWeight(2):F2}");
            GUILayout.Label($"Close: {mainMixer.GetInputWeight(3):F2}");
        }
    }
}