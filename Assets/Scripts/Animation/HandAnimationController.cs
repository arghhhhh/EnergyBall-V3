using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.VFX;

[System.Serializable]
public class HandAnimationSettings
{
    [Header("Animation Clips")]
    public AnimationClip emptyClip;
    public AnimationClip initClip;
    public AnimationClip openClip;
    public AnimationClip closeClip;

    [Header("Blend Curves")]
    public AnimationCurve closeToOpenCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve openToCloseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Transition Delays")]
    public float closeToOpenDelay = 0f;
    public float openToCloseDelay = 0f;

    [Header("Timing")]
    public float initToOpenTransitionPoint = 0.8f; // When init is 80% complete, start blending to open
    public float closeToInitTimeThreshold = 2f; // 2 seconds threshold
}

public enum HandAnimationState
{
    Empty,
    Init,
    BlendingInitToOpen,
    Open,
    BlendingToClose,
    Close,
    BlendingCloseToOpen,
    BlendingCloseToInit
}

[RequireComponent(typeof(VisualEffect))]
public class HandAnimationController : MonoBehaviour
{
    [SerializeField] private HandAnimationSettings settings;
    [SerializeField] private bool isLeftHand = true; // Identifies which hand this controller represents

    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixer;
    private ScriptPlayable<HandAnimationBehavior> scriptPlayable;
    private HandAnimationBehavior behaviour;
    private VisualEffect vfxGraph;

    private void Start()
    {
        vfxGraph = GetComponent<VisualEffect>();
        InitializePlayableGraph();

        // Subscribe to hand action events
        Actions.OnHandOpen += OnHandOpenAction;
        Actions.OnHandClose += OnHandCloseAction;
    }

    private void InitializePlayableGraph()
    {
        playableGraph = PlayableGraph.Create();
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        // Create mixer with 4 inputs (empty, init, open, close)
        mixer = AnimationMixerPlayable.Create(playableGraph, 4);

        // Create animation clip playables
        var emptyPlayable = AnimationClipPlayable.Create(playableGraph, settings.emptyClip);
        var initPlayable = AnimationClipPlayable.Create(playableGraph, settings.initClip);
        var openPlayable = AnimationClipPlayable.Create(playableGraph, settings.openClip);
        var closePlayable = AnimationClipPlayable.Create(playableGraph, settings.closeClip);

        // Connect clips to mixer
        playableGraph.Connect(emptyPlayable, 0, mixer, 0);
        playableGraph.Connect(initPlayable, 0, mixer, 1);
        playableGraph.Connect(openPlayable, 0, mixer, 2);
        playableGraph.Connect(closePlayable, 0, mixer, 3);

        // Create custom behaviour
        scriptPlayable = ScriptPlayable<HandAnimationBehavior>.Create(playableGraph);
        behaviour = scriptPlayable.GetBehaviour();
        behaviour.Initialize(mixer, settings, vfxGraph);

        // Connect mixer to behaviour
        playableGraph.Connect(mixer, 0, scriptPlayable, 0);

        // Create output
        var output = AnimationPlayableOutput.Create(playableGraph, "VFX Animation", GetComponent<Animator>());
        output.SetSourcePlayable(scriptPlayable);

        // Start with empty state
        behaviour.SetState(HandAnimationState.Empty);

        playableGraph.Play();
    }

    private void OnHandOpenAction(PlayerConstructor player, bool isActionForLeftHand)
    {
        // Only respond if this action is for the hand this controller represents
        if (isActionForLeftHand == isLeftHand)
        {
            OnHandOpen();
        }
    }

    private void OnHandCloseAction(PlayerConstructor player, bool isActionForLeftHand)
    {
        // Only respond if this action is for the hand this controller represents
        if (isActionForLeftHand == isLeftHand)
        {
            OnHandClose();
        }
    }

    private void OnHandOpen()
    {
        behaviour.TriggerHandOpen();

        // Send VFX event
        vfxGraph.SendEvent("handOpen");
    }

    private void OnHandClose()
    {
        behaviour.TriggerHandClose();

        // Send VFX event
        vfxGraph.SendEvent("handClose");
    }

    private void OnDisable()
    {
        // Unsubscribe from actions to prevent memory leaks
        Actions.OnHandOpen -= OnHandOpenAction;
        Actions.OnHandClose -= OnHandCloseAction;

        if (playableGraph.IsValid())
            playableGraph.Destroy();
    }

    private void OnValidate()
    {
        if (behaviour != null)
            behaviour.UpdateSettings(settings);
    }
}