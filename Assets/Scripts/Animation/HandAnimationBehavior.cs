using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.VFX;

public class HandAnimationBehavior : PlayableBehaviour
{
    private AnimationMixerPlayable mixer;
    private HandAnimationSettings settings;
    private VisualEffect vfxGraph;

    private HandAnimationState currentState = HandAnimationState.Empty;
    private float stateTime = 0f;
    private float blendProgress = 0f;
    private float closeFinishedTime = -1f;
    private bool pendingTransition = false;
    private float transitionDelay = 0f;

    // Animation clip indices
    private const int EMPTY_INDEX = 0;
    private const int INIT_INDEX = 1;
    private const int OPEN_INDEX = 2;
    private const int CLOSE_INDEX = 3;

    public void Initialize(AnimationMixerPlayable mixerPlayable, HandAnimationSettings animSettings, VisualEffect vfx)
    {
        mixer = mixerPlayable;
        settings = animSettings;
        vfxGraph = vfx;
    }

    public void UpdateSettings(HandAnimationSettings newSettings)
    {
        settings = newSettings;
    }

    public void SetState(HandAnimationState newState)
    {
        currentState = newState;
        stateTime = 0f;
        blendProgress = 0f;
        pendingTransition = false;

        // Set initial weights based on state
        switch (newState)
        {
            case HandAnimationState.Empty:
                SetMixerWeights(1f, 0f, 0f, 0f);
                break;
            case HandAnimationState.Init:
                SetMixerWeights(0f, 1f, 0f, 0f);
                ResetClipTime(INIT_INDEX);
                break;
            case HandAnimationState.Open:
                SetMixerWeights(0f, 0f, 1f, 0f);
                ResetClipTime(OPEN_INDEX);
                break;
            case HandAnimationState.Close:
                SetMixerWeights(0f, 0f, 0f, 1f);
                ResetClipTime(CLOSE_INDEX);
                break;
        }
    }

    public void TriggerHandOpen()
    {
        float currentTime = Time.time;

        if (currentState == HandAnimationState.Close && closeFinishedTime > 0f)
        {
            float timeSinceClose = currentTime - closeFinishedTime;

            if (timeSinceClose < settings.closeToInitTimeThreshold)
            {
                // Skip init, go directly to open
                StartDelayedTransition(HandAnimationState.BlendingCloseToOpen, settings.closeToOpenDelay);
            }
            else
            {
                // Go through init first
                StartDelayedTransition(HandAnimationState.BlendingCloseToInit, settings.closeToOpenDelay);
            }
        }
        else if (currentState == HandAnimationState.Empty)
        {
            // Start init immediately from empty
            SetState(HandAnimationState.Init);
        }
        else if (currentState == HandAnimationState.BlendingToClose)
        {
            // Interrupt close transition and go to open
            StartDelayedTransition(HandAnimationState.BlendingCloseToOpen, settings.closeToOpenDelay);
        }
    }

    public void TriggerHandClose()
    {
        if (currentState != HandAnimationState.Close && currentState != HandAnimationState.BlendingToClose)
        {
            StartDelayedTransition(HandAnimationState.BlendingToClose, settings.openToCloseDelay);
        }
    }

    private void StartDelayedTransition(HandAnimationState targetState, float delay)
    {
        if (delay > 0f)
        {
            pendingTransition = true;
            transitionDelay = delay;
            // We'll transition to targetState after delay
        }
        else
        {
            SetState(targetState);
        }
    }

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        if (mixer.GetInputCount() == 0) return;

        stateTime += (float)info.deltaTime;

        // Handle pending transitions
        if (pendingTransition)
        {
            transitionDelay -= (float)info.deltaTime;
            if (transitionDelay <= 0f)
            {
                pendingTransition = false;
                // Determine which state to transition to based on current context
                if (currentState == HandAnimationState.BlendingToClose)
                {
                    SetState(HandAnimationState.BlendingCloseToOpen);
                }
                else
                {
                    SetState(HandAnimationState.BlendingToClose);
                }
            }
        }

        switch (currentState)
        {
            case HandAnimationState.Init:
                HandleInitState();
                break;
            case HandAnimationState.BlendingInitToOpen:
                HandleInitToOpenBlending(info);
                break;
            case HandAnimationState.BlendingToClose:
                HandleToCloseBlending(info);
                break;
            case HandAnimationState.Close:
                HandleCloseState();
                break;
            case HandAnimationState.BlendingCloseToOpen:
                HandleCloseToOpenBlending(info);
                break;
            case HandAnimationState.BlendingCloseToInit:
                HandleCloseToInitBlending(info);
                break;
        }
    }

    private void HandleInitState()
    {
        var initClip = (AnimationClipPlayable)mixer.GetInput(INIT_INDEX);
        float initProgress = (float)(initClip.GetTime() / settings.initClip.length);

        if (initProgress >= settings.initToOpenTransitionPoint)
        {
            SetState(HandAnimationState.BlendingInitToOpen);
        }
    }

    private void HandleInitToOpenBlending(FrameData info)
    {
        var initClip = (AnimationClipPlayable)mixer.GetInput(INIT_INDEX);
        float initProgress = (float)(initClip.GetTime() / settings.initClip.length);

        if (initProgress >= 1f)
        {
            SetState(HandAnimationState.Open);
        }
        else
        {
            float blendAmount = Mathf.InverseLerp(settings.initToOpenTransitionPoint, 1f, initProgress);
            BlendBetweenClips(INIT_INDEX, OPEN_INDEX, blendAmount);
        }
    }

    private void HandleToCloseBlending(FrameData info)
    {
        blendProgress += (float)info.deltaTime;
        float blendDuration = 1f; // You can make this configurable

        float normalizedProgress = Mathf.Clamp01(blendProgress / blendDuration);
        float curveValue = settings.openToCloseCurve.Evaluate(normalizedProgress);

        // Blend from current state to close
        if (currentState == HandAnimationState.BlendingToClose)
        {
            BlendToClose(curveValue);

            if (normalizedProgress >= 1f)
            {
                SetState(HandAnimationState.Close);
            }
        }
    }

    private void HandleCloseState()
    {
        var closeClip = (AnimationClipPlayable)mixer.GetInput(CLOSE_INDEX);
        if (closeClip.GetTime() >= settings.closeClip.length)
        {
            closeFinishedTime = Time.time;
        }
    }

    private void HandleCloseToOpenBlending(FrameData info)
    {
        blendProgress += (float)info.deltaTime;
        float blendDuration = 1f; // Configurable

        float normalizedProgress = Mathf.Clamp01(blendProgress / blendDuration);
        float curveValue = settings.closeToOpenCurve.Evaluate(normalizedProgress);

        BlendBetweenClips(CLOSE_INDEX, OPEN_INDEX, curveValue);

        if (normalizedProgress >= 1f)
        {
            SetState(HandAnimationState.Open);
        }
    }

    private void HandleCloseToInitBlending(FrameData info)
    {
        blendProgress += (float)info.deltaTime;
        float blendDuration = 0.5f; // Faster transition to init

        float normalizedProgress = Mathf.Clamp01(blendProgress / blendDuration);

        BlendBetweenClips(CLOSE_INDEX, INIT_INDEX, normalizedProgress);

        if (normalizedProgress >= 1f)
        {
            SetState(HandAnimationState.Init);
        }
    }

    private void BlendBetweenClips(int fromIndex, int toIndex, float blendAmount)
    {
        SetMixerWeights(0f, 0f, 0f, 0f);
        mixer.SetInputWeight(fromIndex, 1f - blendAmount);
        mixer.SetInputWeight(toIndex, blendAmount);

        // Sync the target clip time if needed
        if (blendAmount > 0f && toIndex == OPEN_INDEX)
        {
            ResetClipTime(toIndex);
        }
    }

    private void BlendToClose(float blendAmount)
    {
        // Determine which clips are currently active
        float initWeight = mixer.GetInputWeight(INIT_INDEX);
        float openWeight = mixer.GetInputWeight(OPEN_INDEX);

        if (initWeight > 0f)
        {
            mixer.SetInputWeight(INIT_INDEX, initWeight * (1f - blendAmount));
        }
        if (openWeight > 0f)
        {
            mixer.SetInputWeight(OPEN_INDEX, openWeight * (1f - blendAmount));
        }

        mixer.SetInputWeight(CLOSE_INDEX, blendAmount);

        if (blendAmount > 0f)
        {
            ResetClipTime(CLOSE_INDEX);
        }
    }

    private void SetMixerWeights(float empty, float init, float open, float close)
    {
        mixer.SetInputWeight(EMPTY_INDEX, empty);
        mixer.SetInputWeight(INIT_INDEX, init);
        mixer.SetInputWeight(OPEN_INDEX, open);
        mixer.SetInputWeight(CLOSE_INDEX, close);
    }

    private void ResetClipTime(int clipIndex)
    {
        var clip = (AnimationClipPlayable)mixer.GetInput(clipIndex);
        clip.SetTime(0);
    }
}