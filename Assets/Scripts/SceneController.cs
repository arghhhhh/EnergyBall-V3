using System.Collections.Generic;
using MarchingCubes;
using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Windows.Kinect;
using Joint = Windows.Kinect.Joint;

public class SceneController : MonoBehaviour
{
    public static SceneController Instance { get; private set; } // singleton pattern

    [Label("Settings Config")]
    public SceneSettingsSO so;
    
    [Header("Runtime Settings")]
    public InGameSettingsMenu settingsMenu;
    private RuntimeSceneSettings runtimeSettings;
    GravityForce gravityForceController;
    HandForce handForceController;
    HandEffects handEffectsController;
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

    public bool drawSkeleton;
    public bool customColors;
    private int lastColorIndex;
    public Color[] colors = new Color[]
    {
        Color.blue,
        Color.cyan,
        Color.green,
        Color.magenta,
        Color.red,
        new (1, 0.5f, 0),
        Color.yellow
    };

    [GradientUsage(true)]
    public Gradient[] gradients = new Gradient[]
    {
        new(),
        new(),
        new(),
        new(),
        new(),
        new(),
        new()
    };



    private void OnEnable()
    {
        // Actions.OnPlayerAdded += AddPlayer;
        // Actions.OnPlayerRemoved += RemovePlayer;
        Actions.OnDummyAdded += InitializeNewDummy;
        Actions.OnDummyRemoved += RemovePlayer;

        // Subscribe to debugging setting changes
        if (so != null)
        {
            so.OnAnyDebuggingSettingChanged += OnAnyDebuggingSettingChanged;
        }
        
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

        // Unsubscribe from debugging setting changes
        if (so != null)
        {
            so.OnAnyDebuggingSettingChanged -= OnAnyDebuggingSettingChanged;
        }
        
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
        playerScaleController = new();
        metaballsToSDF = GetComponent<MetaballsToSDF>();
        bodySourceManager = GetComponent<BodySourceManager>();
        
        // Initialize runtime settings from ScriptableObject
        if (settingsMenu != null)
        {
            runtimeSettings = settingsMenu.GetCurrentSettings();
        }
        
        // Use runtime settings or fallback to SO
        var currentSettings = GetCurrentSettings();
        // transform.position = new Vector3(transform.position.x, transform.position.y, currentSettings.baseZDepth);

        // set main camera far clipping plane to currentSettings.maxDistanceFromCamera
        // Camera.main.farClipPlane = currentSettings.maxDistanceFromCamera + transform.position.z;
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
            var currentSettings = GetCurrentSettings();
            dummy.unscaledSize = new Vector3(
                currentSettings.defaultUnscaledSize,
                currentSettings.defaultUnscaledSize,
                currentSettings.defaultUnscaledSize
            );
            if (customColors)
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
            if (customColors)
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
        int colorIndex = Random.Range(0, colors.Length);
        while (colorIndex == lastColorIndex)
        {
            colorIndex = Random.Range(0, colors.Length);
        }
        lastColorIndex = colorIndex;
        player.skeletonColor = colors[colorIndex];
        player.leftHandVfx.SetGradient("playerAuraBase", gradients[colorIndex]);
        player.rightHandVfx.SetGradient("playerAuraBase", gradients[colorIndex]);
    }

    void SetPlayerHandStates(Body body, PlayerConstructor player)
    {
        player.leftHandState = body.HandLeftState;
        player.rightHandState = body.HandRightState;
    }

    private Vector3 GetVector3FromJoint(Joint joint)
    {
        var currentSettings = GetCurrentSettings();
        return new Vector3(
            joint.Position.X * currentSettings.bodyScale,
            joint.Position.Y * currentSettings.bodyScale,
            joint.Position.Z * currentSettings.bodyScale
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

        if (playerConstructor.beginInitialization)
        {
            metaballsToSDF.SetMetaballPosition(
                playerConstructor.metaballIndex,
                playerConstructor.sphere.transform.position
            );
            metaballsToSDF.SetMetaballRadius(
                playerConstructor.metaballIndex,
                playerConstructor.sphere.transform.localScale.x / 2f
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
            handEffectsController.ManageHandEffects(playerConstructor);
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
                //     $"position: {targetPosition}\n" +
                //     $"maxDistanceFromCamera: {so.maxDistanceFromCamera}";

                Transform jointObject = playerConstructor.jointMap[joint].transform;
                jointObject.position = targetPosition;

                var currentSettings = GetCurrentSettings();
                if (
                    (joint == JointType.ShoulderLeft || joint == JointType.ShoulderRight)
                    && targetPosition.z > currentSettings.maxDistanceFromCamera
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

                if (drawSkeleton)
                {
                    playerConstructor.leftHandCollider.gameObject.SetActive(true);
                    playerConstructor.rightHandCollider.gameObject.SetActive(true);

                    if (boneMap.ContainsKey(joint))
                    {
                        Joint? targetJoint = body.Joints[boneMap[joint]];

                        if (targetJoint.HasValue)
                        {
                            LineRenderer lr = jointObject.GetComponent<LineRenderer>();
                            lr.enabled = true;
                            lr.SetPosition(0, jointObject.localPosition);
                            lr.SetPosition(1, GetVector3FromJoint(targetJoint.Value));
                            if (customColors)
                            {
                                lr.startColor = playerConstructor.skeletonColor;
                                lr.endColor = playerConstructor.skeletonColor;
                            }
                            else
                            {
                                lr.startColor = ColorSkeleton(sourceJoint.TrackingState);
                                lr.endColor = ColorSkeleton(targetJoint.Value.TrackingState);
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
                Destroy(players[trackingId]);
                players.Remove(trackingId);
            }
        }
    }

    void FixedUpdate()
    {
        var currentSettings = GetCurrentSettings();
        if (!currentSettings.dummyOnlyMode)
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

            if (Input.GetMouseButtonDown(0))
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

    private RuntimeSceneSettings CreateFallbackSettings()
    {
        if (so == null) return new RuntimeSceneSettings();
        
        var fallback = new RuntimeSceneSettings();
        fallback.CopyFromScriptableObject(so);
        return fallback;
    }

    private void OnRuntimeSettingsChanged(RuntimeSceneSettings newSettings)
    {
        runtimeSettings = newSettings;
        
        // Update any cached references or trigger updates as needed
        UpdateAllPlayersDebuggingVisuals();
    }

    public RuntimeSceneSettings GetRuntimeSettings()
    {
        return GetCurrentSettings();
    }
    #endregion

    #region Debugging
    private void OnAnyDebuggingSettingChanged()
    {
        UpdateAllPlayersDebuggingVisuals();
    }

    private void UpdatePlayerHandTrailDistorters(PlayerConstructor player)
    {
        var currentSettings = GetCurrentSettings();
        if (currentSettings.showHandTrailDistorters)
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
        var currentSettings = GetCurrentSettings();
        if (currentSettings.showAttractionRadius)
        {
            player.radiusSprite.enabled = true;
            // set size of radius sprite
            player.radiusSprite.transform.localScale = new Vector3(
                currentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler,
                currentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler,
                currentSettings.attractionRadiusMultiplier * 0.4f * player.attractionRadiusScaler
            );
        }
        else
        {
            player.radiusSprite.enabled = false;
        }
    }

    private void UpdatePlayerSecondaryAttractor(PlayerConstructor player)
    {
        var currentSettings = GetCurrentSettings();
        if (currentSettings.showSecondaryAttractor)
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

