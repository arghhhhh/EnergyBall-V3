using System.Collections.Generic;
using MarchingCubes;
using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Windows.Kinect;
using Joint = Windows.Kinect.Joint;

[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(MetaballsToSDF))]
public class SceneController : MonoBehaviour
{
    public static SceneController Instance { get; private set; } // singleton pattern
    #region Inspector Settings
    [BoxGroup("Gravity Attraction")]
    public float g = 9.81f;

    [BoxGroup("Gravity Attraction")]
    public float maxTowardsForce = 10f;

    [BoxGroup("Gravity Attraction")]
    public float maxAwayFromForce = 10f;

    [BoxGroup("Gravity Attraction")]
    public float gravityForceDamper = 1f;

    [BoxGroup("Gravity Attraction")]
    public float stopGravityDistance = 0.1f;

    [BoxGroup("Gravity Attraction")]
    public float stopMovingDistance = 0.05f;

    [BoxGroup("Gravity Attraction")]
    public float stopVelocity = 0.1f;

    [BoxGroup("Gravity Attraction")]
    public float attractionRadiusMultiplier = 1f;

    [BoxGroup("Hands Attraction")]
    [InfoBox(
        "Curve settings (forceToMiddle, alignmentVectorStrength) are managed separately below",
        EInfoBoxType.Normal
    )]
    public float singleHandOpenForceDamper = 1f;

    [BoxGroup("Boundary Drag")]
    [Tooltip(
        "Multiplier for max distance calculation. Max distance = this * (longest grid side / 2)."
    )]
    public float addedBoundaryDistance = 1.5f;

    [BoxGroup("Boundary Drag")]
    [Tooltip(
        "Drag applied to stop the sphere when moving away from hands while past the boundary. Set to 0 to disable."
    )]
    public float boundaryOutwardDrag = 50f;

    [BoxGroup("Boundary Drag")]
    [Tooltip(
        "Time in seconds the sphere must be out of bounds before it can be reset to hand midpoint when both hands open."
    )]
    public float outOfBoundsResetDelay = 3f;

    [BoxGroup("Hands Attraction")]
    public float pushForce = 5f;

    [BoxGroup("Hands Attraction")]
    public float minDrag = 0.1f;

    [BoxGroup("Hands Attraction")]
    public float maxDrag = 5f;

    [BoxGroup("Hands Attraction")]
    public float alignmentVectorStrengthScaler = 1f;

    [BoxGroup("Hands Attraction")]
    public float handPushScaler = 1f;

    [BoxGroup("Hands Attraction")]
    public bool prayToActivate = false;

    [BoxGroup("Hands Attraction")]
    [ShowIf("prayToActivate")]
    public float prayToActivateDistance = 0.65f;

    [BoxGroup("Intrinsic Pulsation")]
    [Range(0, 10f)]
    public float pulseAmount = 1f;

    [BoxGroup("Intrinsic Pulsation")]
    public float pulseSpeed = 1f;

    [BoxGroup("Intrinsic Pulsation")]
    public float graphLimit = 10f;

    [BoxGroup("Intrinsic Pulsation")]
    public float[] pulseFreqs = new float[] { 1f, 2f, 3f };

    [BoxGroup("Movement-Based Pulsation")]
    [Tooltip("Allow scaling to occur with only one hand's velocity.")]
    public bool singleHandScaling = true;

    [BoxGroup("Movement-Based Pulsation")]
    [Tooltip("The minimum size that the body can scale down to.")]
    public float minimumUnscaledSize = 0.5f;

    [BoxGroup("Movement-Based Pulsation")]
    [Range(0.0001f, 5f)]
    [Tooltip(
        "Used to mask false velocity readings due to position jitter from inaccurate sensor readings."
    )]
    public float minHandDisplacementPerFrame = 0.01f;

    [BoxGroup("Movement-Based Pulsation")]
    [InfoBox("distanceDamper curve is managed separately below", EInfoBoxType.Normal)]
    [Tooltip("An overall damper for the movement-based pulsation scaling.")]
    public float pulseScaleDamper = 1f;

    [BoxGroup("Miscellaneous")]
    [Tooltip("A damper for the scaling that occurs when multiple bodies merge together.")]
    public float mergeSizeScalerDamper = 1f;

    [BoxGroup("Miscellaneous")]
    public float maxDistanceBetweenHands = 2f;

    [BoxGroup("Miscellaneous")]
    public float baseZDepth = 5f;

    [BoxGroup("Miscellaneous")]
    public float defaultUnscaledSize = 1f;

    [BoxGroup("Miscellaneous")]
    public float bodyScale = 1f;

    [BoxGroup("Miscellaneous")]
    public float maxDistanceFromCamera = 10f;

    [BoxGroup("Animation")]
    [Tooltip(
        "The amount of time it takes for the particle initialization animation to play once a new player is added to the scene."
    )]
    public float particleInitializationDelay = 1f;

    [BoxGroup("Animation")]
    [Tooltip(
        "Time in seconds the hands must be closed before the initialization animation plays once both hands are opened."
    )]
    public float initializationResetDelay = 3f;

    [BoxGroup("Animation")]
    [Tooltip(
        "Minimum time in single-hand-open state before the final push uses that hand's position. "
            + "Accounts for slight timing discrepancies with real Kinect users."
    )]
    public float singleHandOpenThreshold = 0.1f;

    [BoxGroup("Animation")]
    [Tooltip(
        "Duration in seconds to lerp the force damper from single-hand to both-hands strength "
            + "when transitioning from single-hand-open to both-hands-open."
    )]
    public float singleHandForceLerpDuration = 0.35f;

    [BoxGroup("Animation")]
    [Range(0f, 1f)]
    [Tooltip(
        "Speed of the hand opening animation during initialization. Lower values = slower animation."
    )]
    public float initializationSpeed = 0.05f;

    [BoxGroup("Animation")]
    [Tooltip(
        "Duration in seconds for the metaball radius to animate from minimum to full size during initialization."
    )]
    public float metaballRadiusAnimationDuration = 2f;

    [BoxGroup("Animation")]
    [Tooltip("The starting radius for the metaball animation during initialization.")]
    public float metaballRadiusAnimationStartSize = 0.1f;

    [BoxGroup("Animation")]
    [Tooltip(
        "Animation curve for the metaball radius transition (0-1 input maps to animation progress)."
    )]
    public AnimationCurve metaballRadiusAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Curve Settings")]
    [BoxGroup("Hands Attraction Curves")]
    [Tooltip("Force curve that controls attraction to the middle point between hands")]
    public AnimationCurve forceToMiddle = AnimationCurve.Linear(0, 0, 1, 1);

    [BoxGroup("Hands Attraction Curves")]
    [Tooltip("Alignment vector strength curve based on hand distance")]
    public AnimationCurve alignmentVectorStrength = AnimationCurve.Linear(0, 0, 1, 1);

    [BoxGroup("Movement-Based Pulsation Curves")]
    [Tooltip(
        "Dampen the ratio between body scale and hand distance based on hand distance relative to maxDistanceBetweenHands"
    )]
    public AnimationCurve distanceDamper = AnimationCurve.Linear(0, 0, 1, 1);

    [BoxGroup("Style Settings")]
    public bool customColors = false;

    [BoxGroup("Style Settings")]
    [HideIf("customColors")]
    [Tooltip("Use tracking state colors for the skeleton")]
    public bool useTrackingStateColors = true;

    [BoxGroup("Style Settings")]
    [HideIf(EConditionOperator.Or, "customColors", "useTrackingStateColors")]
    public Color skeletonColor = Color.magenta;

    [BoxGroup("Style Settings")]
    [HideIf("customColors")]
    [GradientUsage(true)]
    public Gradient particleColor = new();
    private int lastColorIndex;

    [ShowIf("customColors")]
    [BoxGroup("Style Settings")]
    public Color[] skeletonColors = new Color[]
    {
        Color.blue,
        Color.cyan,
        Color.green,
        Color.magenta,
        Color.red,
        new(1, 0.5f, 0),
        Color.yellow
    };

    [ShowIf("customColors")]
    [BoxGroup("Style Settings")]
    [GradientUsage(true)]
    public Gradient[] particleColors = new Gradient[]
    {
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
        new()
    };

    [BoxGroup("Debugging")]
    public bool dummyOnlyMode = false;

    [BoxGroup("Debugging")]
    public bool drawSkeleton = false;

    [BoxGroup("Debugging")]
    public bool showSphereMeshOnHandCollision = false;

    [BoxGroup("Debugging")]
    [Tooltip("When enabled, the sphere mesh is always visible regardless of hand collision state.")]
    public bool alwaysShowSphereMesh = false;

    [BoxGroup("Debugging")]
    [Tooltip("When enabled, the metaball mesh renderer is visible for debugging.")]
    public bool showMetaballMesh = false;

    [BoxGroup("Debugging")]
    public bool showAttractionRadius = false;

    [BoxGroup("Debugging")]
    public bool showHandTrailDistorters = false;

    [BoxGroup("Debugging")]
    public bool showSecondaryAttractor = false;
    #endregion

    [Header("Runtime Settings")]
    public InGameSettingsMenu settingsMenu;
    public VolumeController volumeController;
    private RuntimeSceneSettings runtimeSettings;
    private RuntimeSceneSettings cachedCurrentSettings; // Cache for current settings
    GravityForce gravityForceController;
    HandForce handForceController;
    HandEffects handEffectsController;
    BoundaryForce boundaryForceController;
    PlayerScaler playerScaleController;
    MetaballsToSDF metaballsToSDF = null;
    BodySourceManager bodySourceManager = null;
    Body[] bodyData;
    List<ulong> trackedIds = new();
    List<ulong> knownIds = new();
    public GameObject playerPrefab;
    public TextMeshProUGUI debugText;
    private readonly Dictionary<ulong, GameObject> players = new();
    public Dictionary<ulong, GameObject> Players
    {
        get { return players; }
    }

    private readonly Dictionary<ulong, GameObject> dummies = new();

    private readonly Dictionary<JointType, JointType> boneMap =
        new()
        {
            // { JointType.FootLeft, JointType.AnkleLeft },
            // { JointType.AnkleLeft, JointType.KneeLeft },
            // { JointType.KneeLeft, JointType.HipLeft },
            // { JointType.HipLeft, JointType.SpineBase },

            // { JointType.FootRight, JointType.AnkleRight },
            // { JointType.AnkleRight, JointType.KneeRight },
            // { JointType.KneeRight, JointType.HipRight },
            // { JointType.HipRight, JointType.SpineBase },

            // { JointType.HandTipLeft, JointType.HandLeft },
            // { JointType.ThumbLeft, JointType.HandLeft },
            { JointType.HandLeft, JointType.WristLeft },
            { JointType.WristLeft, JointType.ElbowLeft },
            { JointType.ElbowLeft, JointType.ShoulderLeft },
            { JointType.ShoulderLeft, JointType.SpineShoulder },
            // { JointType.HandTipRight, JointType.HandRight },
            // { JointType.ThumbRight, JointType.HandRight },
            { JointType.HandRight, JointType.WristRight },
            { JointType.WristRight, JointType.ElbowRight },
            { JointType.ElbowRight, JointType.ShoulderRight },
            { JointType.ShoulderRight, JointType.SpineShoulder },

            // { JointType.SpineBase, JointType.SpineMid },
            // { JointType.SpineMid, JointType.SpineShoulder },
            // { JointType.SpineShoulder, JointType.Neck },
            // { JointType.Neck, JointType.Head },
        };

    private void OnEnable()
    {
        // Actions.OnPlayerAdded += AddPlayer;
        // Actions.OnPlayerRemoved += RemovePlayer;
        Actions.OnDummyAdded += InitializeNewDummy;
        Actions.OnDummyRemoved += RemovePlayer;
        Actions.OnCustomColorsChanged += OnCustomColorsChanged;

        // Debugging setting changes are now handled via OnValidate() when inspector values change

        // Subscribe to runtime settings changes
        if (settingsMenu != null)
        {
            settingsMenu.OnSettingsChanged += OnRuntimeSettingsChanged;
        }
    }

    private void OnDisable()
    {
        // Actions.OnPlayerAdded -= AddPlayer;
        // Actions.OnPlayerRemoved -= RemovePlayer;
        Actions.OnDummyAdded -= InitializeNewDummy;
        Actions.OnDummyRemoved -= RemovePlayer;
        Actions.OnCustomColorsChanged -= OnCustomColorsChanged;

        // Debugging setting changes are now handled via OnValidate() when inspector values change

        // Unsubscribe from runtime settings changes
        if (settingsMenu != null)
        {
            settingsMenu.OnSettingsChanged -= OnRuntimeSettingsChanged;
        }
    }

    private void Awake()
    {
        // only if not in unity editor

        if (!Application.isEditor)
        {
            Cursor.visible = false;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }

        gravityForceController = new();
        handForceController = new();
        handEffectsController = new();
        boundaryForceController = new();
        playerScaleController = new();
        metaballsToSDF = GetComponent<MetaballsToSDF>();
        bodySourceManager = GetComponent<BodySourceManager>();

        // Initialize runtime settings from inspector values
        if (settingsMenu != null)
        {
            runtimeSettings = settingsMenu.GetCurrentSettings();
        }

        // Ensure we have runtime settings (fallback if needed)
        if (runtimeSettings == null)
        {
            runtimeSettings = CreateFallbackSettings();
        }

        // Apply settings from inspector to runtime settings (without UI sync during initialization)
        if (runtimeSettings != null)
        {
            CopyInspectorToRuntime(runtimeSettings);
            cachedCurrentSettings = GetCurrentSettings();
        }
        // transform.position = new Vector3(transform.position.x, transform.position.y, cachedCurrentSettings.baseZDepth);

        // set main camera far clipping plane to cachedCurrentSettings.maxDistanceFromCamera
        // Camera.main.farClipPlane = cachedCurrentSettings.maxDistanceFromCamera + transform.position.z;
    }

    void Start()
    {
        // Perform full sync after all components are initialized
        SyncInspectorToRuntime();
    }

    /// <summary>
    /// Get the PlayerPrefs key for this scene's last used scene profile
    /// </summary>
    public string GetSceneSpecificSceneProfileKey()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return $"LastUsedSceneProfile_{sceneName}";
    }

    /// <summary>
    /// Get the PlayerPrefs key for this scene's last used post-processing profile
    /// </summary>
    public string GetSceneSpecificPostProcessingProfileKey()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return $"LastUsedPostProcessingProfile_{sceneName}";
    }

    void InitializeNewDummy(PlayerConstructor dummy)
    {
        if (dummy)
        {
            ulong userId = (ulong)Random.Range(90, 100); // generate random userId between 90 and 99
            while (dummies.ContainsKey(userId))
            {
                userId = (ulong)Random.Range(90, 100);
            }
            dummy.name = $"Player {userId}";
            dummy.userId = userId;
            metaballsToSDF.AssignMetaballIndex(dummy);
            dummy.unscaledSize = new Vector3(
                CurrentSettings.defaultUnscaledSize,
                CurrentSettings.defaultUnscaledSize,
                CurrentSettings.defaultUnscaledSize
            );
            if (CurrentSettings.customColors)
            {
                ChoosePlayerColor(dummy);
            }

            dummies[dummy.userId] = dummy.gameObject;
            players[dummy.userId] = dummy.gameObject;

            UpdatePlayerDebuggingVisuals(dummy);

            // debugText.text = $"userId: {userId}\nunscaledSize: {dummy.unscaledSize.x}\nplayer: {players[userId].name}";
        }
    }

    GameObject InitializeNewPlayer(ulong userId)
    {
        GameObject newPlayer = Instantiate(playerPrefab);

        if (newPlayer.TryGetComponent<PlayerConstructor>(out var playerConstructor))
        {
            newPlayer.name = $"Player {userId}";
            playerConstructor.userId = userId;
            metaballsToSDF.AssignMetaballIndex(playerConstructor);
            if (CurrentSettings.customColors)
            {
                ChoosePlayerColor(playerConstructor);
            }

            UpdatePlayerDebuggingVisuals(playerConstructor);
        }

        return newPlayer;
    }

    void RemovePlayer(ulong userId)
    {
        if (players[userId])
        {
            PlayerConstructor playerConstructor = players[userId].GetComponent<PlayerConstructor>();
            if (playerConstructor.isDummy)
            {
                dummies.Remove(userId);
            }
            metaballsToSDF.RemoveMetaballIndex(playerConstructor.metaballIndex);
            Destroy(players[userId]);
            players.Remove(userId);
        }
    }

    void ChoosePlayerColor(PlayerConstructor player)
    {
        if (CurrentSettings.customColors)
        {
            // Collect all currently used color indices
            HashSet<int> usedIndices = new();
            foreach (var existingPlayer in players.Values)
            {
                if (existingPlayer != null && existingPlayer != player.gameObject)
                {
                    var pc = existingPlayer.GetComponent<PlayerConstructor>();
                    for (int i = 0; i < skeletonColors.Length; i++)
                    {
                        if (pc.skeletonColor == skeletonColors[i])
                        {
                            usedIndices.Add(i);
                            break;
                        }
                    }
                }
            }

            int colorIndex;
            // If there are unused colors, pick from those
            if (usedIndices.Count < skeletonColors.Length)
            {
                do
                {
                    colorIndex = Random.Range(0, skeletonColors.Length);
                } while (usedIndices.Contains(colorIndex));
            }
            else
            {
                // All colors in use, pick randomly (avoid last used for variety)
                colorIndex = Random.Range(0, skeletonColors.Length);
                while (colorIndex == lastColorIndex && skeletonColors.Length > 1)
                {
                    colorIndex = Random.Range(0, skeletonColors.Length);
                }
            }

            lastColorIndex = colorIndex;
            player.skeletonColor = skeletonColors[colorIndex];
            player.leftHandVfx.SetGradient("playerAuraBase", particleColors[colorIndex]);
            player.rightHandVfx.SetGradient("playerAuraBase", particleColors[colorIndex]);
        }
        else
        {
            player.skeletonColor = skeletonColor;
            player.leftHandVfx.SetGradient("playerAuraBase", particleColor);
            player.rightHandVfx.SetGradient("playerAuraBase", particleColor);
        }
    }

    void SetPlayerHandStates(Body body, PlayerConstructor player)
    {
        player.leftHandState = body.HandLeftState;
        player.rightHandState = body.HandRightState;
    }

    private Vector3 GetVector3FromJoint(Joint joint)
    {
        return new Vector3(
            joint.Position.X * CurrentSettings.bodyScale,
            joint.Position.Y * CurrentSettings.bodyScale,
            joint.Position.Z * CurrentSettings.bodyScale
        );
    }

    private static Color ColorSkeleton(TrackingState state)
    {
        return state switch
        {
            TrackingState.Tracked => Color.white,
            TrackingState.Inferred => Color.grey,
            _ => Color.black,
        };
    }

    void UpdateOtherPlayerData(GameObject player)
    {
        PlayerConstructor playerConstructor = player.GetComponent<PlayerConstructor>();

        // Update hand state tracking before processing hand forces
        playerConstructor.UpdateSingleHandOpenTracking();

        if (playerConstructor.beginInitialization)
        {
            metaballsToSDF.SetMetaballPosition(
                playerConstructor.metaballIndex,
                playerConstructor.GetClampedMetaballPosition()
            );
            metaballsToSDF.SetMetaballRadius(
                playerConstructor.metaballIndex,
                playerConstructor.GetMetaballRadius(cachedCurrentSettings)
            );
            // TEMPLATE: Add metaballs for each hand
            //
            // metaballsToSDF.SetMetaballPosition(
            //     playerConstructor.metaballIndex + 1,
            //     playerConstructor.HandRight.transform.position
            // );
            // metaballsToSDF.SetMetaballRadius(
            //     playerConstructor.metaballIndex + 1,
            //     playerConstructor.sphere.transform.localScale.x / 2f
            // );
            playerConstructor.SetAttractionRadius();
            playerConstructor.SetMass();
            playerConstructor.SetPulseSize();
            playerConstructor.SetScale();
            handForceController.ManageHandForce(playerConstructor);
            boundaryForceController.ManageBoundaryForce(playerConstructor);
            handEffectsController.ManageHandEffects(playerConstructor, cachedCurrentSettings);
            handEffectsController.ManageHandTrailDistorters(playerConstructor);
            playerScaleController.ScaleSetup(playerConstructor);
        }

        // wait until player's hands first open to initialize particles
        if (
            !playerConstructor.beginInitialization
            && playerConstructor.leftHandState == HandState.Open
            && playerConstructor.rightHandState == HandState.Open
        )
        {
            playerConstructor.beginInitialization = true;
            // playerConstructor.InitializeParticles();
        }
        else if (!playerConstructor.beginInitialization)
        {
            playerConstructor.ResetSphereToHandMidpoint();
        }
    }

    void UpdateKinectPlayerData(Body body, GameObject player)
    {
        if (!body.IsRestricted)
        {
            PlayerConstructor playerConstructor = player.GetComponent<PlayerConstructor>();

            foreach (JointType joint in boneMap.Keys)
            {
                Joint sourceJoint = body.Joints[joint];
                Vector3 targetPosition = GetVector3FromJoint(sourceJoint);

                // debugText.text = $"userId: {playerConstructor.userId}\n" +
                //     $"joint: {joint}\n" +
                //     $"position: {player}\n" +
                //     $"maxDistanceFromCamera: {CurrentSettings.maxDistanceFromCamera}";

                Transform jointObject = playerConstructor.jointMap[joint].transform;
                jointObject.position = targetPosition;

                if (
                    (joint == JointType.HandLeft || joint == JointType.HandRight)
                    && targetPosition.z > CurrentSettings.maxDistanceFromCamera
                )
                {
                    playerConstructor.leftHandState = HandState.Closed;
                    playerConstructor.rightHandState = HandState.Closed;
                    foreach (LineRenderer lr in player.GetComponentsInChildren<LineRenderer>())
                    {
                        lr.enabled = false;
                    }
                    playerConstructor.leftHandCollider.gameObject.SetActive(false);
                    playerConstructor.rightHandCollider.gameObject.SetActive(false);

                    return;
                }

                playerConstructor.leftHandCollider.gameObject.SetActive(true);
                playerConstructor.rightHandCollider.gameObject.SetActive(true);

                if (CurrentSettings.drawSkeleton)
                {
                    if (boneMap.ContainsKey(joint))
                    {
                        Joint? targetJoint = body.Joints[boneMap[joint]];

                        if (targetJoint.HasValue)
                        {
                            LineRenderer lr = jointObject.GetComponent<LineRenderer>();
                            lr.enabled = true;
                            lr.SetPosition(0, jointObject.localPosition);
                            lr.SetPosition(1, GetVector3FromJoint(targetJoint.Value));
                            if (CurrentSettings.useTrackingStateColors)
                            {
                                lr.startColor = ColorSkeleton(sourceJoint.TrackingState);
                                lr.endColor = ColorSkeleton(targetJoint.Value.TrackingState);
                            }
                            else
                            {
                                lr.startColor = playerConstructor.skeletonColor;
                                lr.endColor = playerConstructor.skeletonColor;
                            }
                        }
                    }
                }
                else
                {
                    LineRenderer lr = jointObject.GetComponent<LineRenderer>();
                    lr.enabled = false;
                }
            }

            SetPlayerHandStates(body, playerConstructor);
        }
    }

    private void DeleteAllBodies(List<ulong> ids)
    {
        foreach (ulong trackingId in ids)
        {
            if (!dummies.ContainsKey(trackingId))
            {
                RemovePlayer(trackingId);
            }
        }
    }

    void FixedUpdate()
    {
        if (!CurrentSettings.dummyOnlyMode)
        {
            bodyData = bodySourceManager.GetData();
            if (bodyData == null)
            {
                return;
            }

            trackedIds.Clear();
            foreach (var body in bodyData)
            {
                if (body == null)
                    continue;

                if (body.IsTracked)
                    trackedIds.Add(body.TrackingId);
            }

            knownIds = new List<ulong>(players.Keys);
            foreach (ulong trackingId in knownIds)
            {
                if (!trackedIds.Contains(trackingId) && !dummies.ContainsKey(trackingId))
                {
                    RemovePlayer(trackingId);
                }
            }

            if (Input.GetMouseButtonDown(0) && (settingsMenu == null || !settingsMenu.IsMenuOpen))
            {
                DeleteAllBodies(knownIds);
            }
            else if (Input.GetMouseButtonDown(1))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }

            foreach (var body in bodyData)
            {
                if (body == null)
                    continue;

                if (body.IsTracked)
                {
                    if (!players.ContainsKey(body.TrackingId))
                    {
                        players[body.TrackingId] = InitializeNewPlayer(body.TrackingId);
                    }

                    UpdateKinectPlayerData(body, players[body.TrackingId]);
                    UpdateOtherPlayerData(players[body.TrackingId]);
                }
            }
        }

        if (dummies.Count > 0)
        {
            foreach (var dummy in dummies)
            {
                UpdateOtherPlayerData(dummy.Value);
            }
        }

        gravityForceController.ManageGravity();
    }

    #region Settings Management
    private RuntimeSceneSettings GetCurrentSettings()
    {
        return runtimeSettings ?? (settingsMenu?.GetCurrentSettings()) ?? CreateFallbackSettings();
    }

    /// <summary>
    /// Gets the current cached settings. Use this instead of GetCurrentSettings() for performance.
    /// </summary>
    public RuntimeSceneSettings CurrentSettings =>
        cachedCurrentSettings ?? (cachedCurrentSettings = GetCurrentSettings());

    private RuntimeSceneSettings CreateFallbackSettings()
    {
        var fallback = new RuntimeSceneSettings();
        CopyInspectorToRuntime(fallback);
        return fallback;
    }

    /// <summary>
    /// Copy inspector values to runtime settings
    /// </summary>
    public void CopyInspectorToRuntime(RuntimeSceneSettings target)
    {
        // Gravity Attraction
        target.g = g;
        target.maxTowardsForce = maxTowardsForce;
        target.maxAwayFromForce = maxAwayFromForce;
        target.gravityForceDamper = gravityForceDamper;
        target.stopGravityDistance = stopGravityDistance;
        target.stopMovingDistance = stopMovingDistance;
        target.stopVelocity = stopVelocity;
        target.attractionRadiusMultiplier = attractionRadiusMultiplier;

        // Hands Attraction (curves and other settings)
        target.forceToMiddle = new AnimationCurve(forceToMiddle.keys);
        target.singleHandOpenForceDamper = singleHandOpenForceDamper;

        // Boundary Drag
        target.addedBoundaryDistance = addedBoundaryDistance;
        target.boundaryOutwardDrag = boundaryOutwardDrag;
        target.outOfBoundsResetDelay = outOfBoundsResetDelay;

        target.pushForce = pushForce;
        target.minDrag = minDrag;
        target.maxDrag = maxDrag;
        target.alignmentVectorStrength = new AnimationCurve(alignmentVectorStrength.keys);
        target.alignmentVectorStrengthScaler = alignmentVectorStrengthScaler;
        target.handPushScaler = handPushScaler;
        target.prayToActivate = prayToActivate;
        target.prayToActivateDistance = prayToActivateDistance;

        // Intrinsic Pulsation
        target.pulseAmount = pulseAmount;
        target.pulseSpeed = pulseSpeed;
        target.graphLimit = graphLimit;
        target.pulseFreqs = (float[])pulseFreqs.Clone();

        // Movement-Based Pulsation
        target.singleHandScaling = singleHandScaling;
        target.minimumUnscaledSize = minimumUnscaledSize;
        target.minHandDisplacementPerFrame = minHandDisplacementPerFrame;
        target.distanceDamper = new AnimationCurve(distanceDamper.keys);
        target.pulseScaleDamper = pulseScaleDamper;

        // Miscellaneous
        target.mergeSizeScalerDamper = mergeSizeScalerDamper;
        target.maxDistanceBetweenHands = maxDistanceBetweenHands;
        target.baseZDepth = baseZDepth;
        target.defaultUnscaledSize = defaultUnscaledSize;
        target.bodyScale = bodyScale;
        target.maxDistanceFromCamera = maxDistanceFromCamera;

        // Animation
        target.particleInitializationDelay = particleInitializationDelay;
        target.initializationResetDelay = initializationResetDelay;
        target.singleHandOpenThreshold = singleHandOpenThreshold;
        target.singleHandForceLerpDuration = singleHandForceLerpDuration;
        target.initializationSpeed = initializationSpeed;
        target.metaballRadiusAnimationDuration = metaballRadiusAnimationDuration;
        target.metaballRadiusAnimationStartSize = metaballRadiusAnimationStartSize;
        target.metaballRadiusAnimationCurve = new AnimationCurve(metaballRadiusAnimationCurve.keys);

        // Debugging
        target.dummyOnlyMode = dummyOnlyMode;
        target.drawSkeleton = drawSkeleton;
        target.customColors = customColors;
        target.showSphereMeshOnHandCollision = showSphereMeshOnHandCollision;
        target.alwaysShowSphereMesh = alwaysShowSphereMesh;
        target.showMetaballMesh = showMetaballMesh;
        target.showAttractionRadius = showAttractionRadius;
        target.showHandTrailDistorters = showHandTrailDistorters;
        target.showSecondaryAttractor = showSecondaryAttractor;
    }

    /// <summary>
    /// Copy runtime settings back to inspector values
    /// </summary>
    private void CopyRuntimeToInspector(RuntimeSceneSettings source)
    {
        // Gravity Attraction
        g = source.g;
        maxTowardsForce = source.maxTowardsForce;
        maxAwayFromForce = source.maxAwayFromForce;
        gravityForceDamper = source.gravityForceDamper;
        stopGravityDistance = source.stopGravityDistance;
        stopMovingDistance = source.stopMovingDistance;
        stopVelocity = source.stopVelocity;
        attractionRadiusMultiplier = source.attractionRadiusMultiplier;

        // Hands Attraction
        forceToMiddle = new AnimationCurve(source.forceToMiddle.keys);
        singleHandOpenForceDamper = source.singleHandOpenForceDamper;

        // Boundary Drag
        addedBoundaryDistance = source.addedBoundaryDistance;
        boundaryOutwardDrag = source.boundaryOutwardDrag;
        outOfBoundsResetDelay = source.outOfBoundsResetDelay;

        pushForce = source.pushForce;
        minDrag = source.minDrag;
        maxDrag = source.maxDrag;
        alignmentVectorStrength = new AnimationCurve(source.alignmentVectorStrength.keys);
        alignmentVectorStrengthScaler = source.alignmentVectorStrengthScaler;
        handPushScaler = source.handPushScaler;
        prayToActivate = source.prayToActivate;
        prayToActivateDistance = source.prayToActivateDistance;

        // Intrinsic Pulsation
        pulseAmount = source.pulseAmount;
        pulseSpeed = source.pulseSpeed;
        graphLimit = source.graphLimit;
        pulseFreqs = (float[])source.pulseFreqs.Clone();

        // Movement-Based Pulsation
        singleHandScaling = source.singleHandScaling;
        minimumUnscaledSize = source.minimumUnscaledSize;
        minHandDisplacementPerFrame = source.minHandDisplacementPerFrame;
        distanceDamper = new AnimationCurve(source.distanceDamper.keys);
        pulseScaleDamper = source.pulseScaleDamper;

        // Miscellaneous
        mergeSizeScalerDamper = source.mergeSizeScalerDamper;
        maxDistanceBetweenHands = source.maxDistanceBetweenHands;
        baseZDepth = source.baseZDepth;
        defaultUnscaledSize = source.defaultUnscaledSize;
        bodyScale = source.bodyScale;
        maxDistanceFromCamera = source.maxDistanceFromCamera;

        // Animation
        particleInitializationDelay = source.particleInitializationDelay;
        initializationResetDelay = source.initializationResetDelay;
        singleHandOpenThreshold = source.singleHandOpenThreshold;
        singleHandForceLerpDuration = source.singleHandForceLerpDuration;
        initializationSpeed = source.initializationSpeed;
        metaballRadiusAnimationDuration = source.metaballRadiusAnimationDuration;
        metaballRadiusAnimationStartSize = source.metaballRadiusAnimationStartSize;
        metaballRadiusAnimationCurve = new AnimationCurve(source.metaballRadiusAnimationCurve.keys);

        // Debugging
        dummyOnlyMode = source.dummyOnlyMode;
        drawSkeleton = source.drawSkeleton;
        customColors = source.customColors;
        showSphereMeshOnHandCollision = source.showSphereMeshOnHandCollision;
        alwaysShowSphereMesh = source.alwaysShowSphereMesh;
        showMetaballMesh = source.showMetaballMesh;
        showAttractionRadius = source.showAttractionRadius;
        showHandTrailDistorters = source.showHandTrailDistorters;
        showSecondaryAttractor = source.showSecondaryAttractor;
    }

    /// <summary>
    /// Sync inspector values to runtime settings
    /// </summary>
    private void SyncInspectorToRuntime()
    {
        if (runtimeSettings != null)
        {
            CopyInspectorToRuntime(runtimeSettings);
            cachedCurrentSettings = GetCurrentSettings();
            UpdateAllPlayersDebuggingVisuals();

            // Update the settings menu UI to reflect inspector changes
            if (settingsMenu != null)
            {
                settingsMenu.UpdateSettingsFromInspector(runtimeSettings);
            }

            // Update volume profile settings
            if (volumeController != null)
            {
                volumeController.ApplyCurrentSettings(runtimeSettings);
            }
        }
    }

    private void OnRuntimeSettingsChanged(RuntimeSceneSettings newSettings)
    {
        runtimeSettings = newSettings;

        // Update cached settings
        cachedCurrentSettings = GetCurrentSettings();

        // Copy runtime settings back to inspector for synchronization
        CopyRuntimeToInspector(newSettings);

        // Update any cached references or trigger updates as needed
        UpdateAllPlayersDebuggingVisuals();

        // Update volume profile settings
        if (volumeController != null)
        {
            volumeController.ApplyCurrentSettings(newSettings);
        }
    }

    private void OnCustomColorsChanged(bool enabled)
    {
        foreach (var player in players.Values)
        {
            if (player == null)
                continue;
            var pc = player.GetComponent<PlayerConstructor>();
            ChoosePlayerColor(pc);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Called when inspector values change
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying && runtimeSettings != null)
        {
            // Sync inspector changes to runtime settings
            SyncInspectorToRuntime();
        }
    }
#endif

    public RuntimeSceneSettings GetRuntimeSettings()
    {
        return CurrentSettings;
    }

    /// <summary>
    /// Returns the size of the marching cubes grid in world units.
    /// </summary>
    public Vector3 GetGridSize()
    {
        return metaballsToSDF.GetGridSize();
    }
    #endregion

    #region Debugging
    private void OnAnyDebuggingSettingChanged()
    {
        UpdateAllPlayersDebuggingVisuals();
    }

    private void UpdatePlayerHandTrailDistorters(PlayerConstructor player)
    {
        if (CurrentSettings.showHandTrailDistorters)
        {
            foreach (GameObject distorter in player.leftHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = true;
            }
            foreach (GameObject distorter in player.rightHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = true;
            }
        }
        else
        {
            foreach (GameObject distorter in player.leftHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = false;
            }
            foreach (GameObject distorter in player.rightHandTrailDistorters)
            {
                distorter.GetComponent<MeshRenderer>().enabled = false;
            }
        }
    }

    private void UpdatePlayerAttractionRadius(PlayerConstructor player)
    {
        if (CurrentSettings.showAttractionRadius)
        {
            player.radiusSprite.enabled = true;
            // set size of radius sprite
            player.radiusSprite.transform.localScale = new Vector3(
                CurrentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler,
                CurrentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler,
                CurrentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler
            );
        }
        else
        {
            player.radiusSprite.enabled = false;
        }
    }

    private void UpdatePlayerSecondaryAttractor(PlayerConstructor player)
    {
        if (CurrentSettings.showSecondaryAttractor)
        {
            player.leftHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = true;
            player.rightHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = true;
        }
        else
        {
            player.leftHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = false;
            player.rightHandSecondaryAttractor.GetComponent<MeshRenderer>().enabled = false;
        }
    }

    private void UpdatePlayerDebuggingVisuals(PlayerConstructor player)
    {
        UpdatePlayerHandTrailDistorters(player);
        UpdatePlayerAttractionRadius(player);
        UpdatePlayerSecondaryAttractor(player);
    }

    private void UpdateAllPlayersDebuggingVisuals()
    {
        foreach (var player in players.Values)
        {
            if (player != null)
            {
                UpdatePlayerDebuggingVisuals(player.GetComponent<PlayerConstructor>());
            }
        }
    }
    #endregion
}
