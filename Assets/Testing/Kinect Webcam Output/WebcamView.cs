using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class WebcamView : MonoBehaviour
{
    public Material TargetMaterial;
    public RenderTexture TargetRenderTexture;

    private WebCamTexture _webcamTexture;

#if UNITY_IOS || UNITY_WEBGL
    private bool CheckPermissionAndRaiseCallbackIfGranted(
        UserAuthorization authenticationType,
        Action authenticationGrantedAction
    )
    {
        if (Application.HasUserAuthorization(authenticationType))
        {
            if (authenticationGrantedAction != null)
                authenticationGrantedAction();

            return true;
        }
        return false;
    }

    private IEnumerator AskForPermissionIfRequired(
        UserAuthorization authenticationType,
        Action authenticationGrantedAction
    )
    {
        if (
            !CheckPermissionAndRaiseCallbackIfGranted(
                authenticationType,
                authenticationGrantedAction
            )
        )
        {
            yield return Application.RequestUserAuthorization(authenticationType);
            if (
                !CheckPermissionAndRaiseCallbackIfGranted(
                    authenticationType,
                    authenticationGrantedAction
                )
            )
                Debug.LogWarning($"Permission {authenticationType} Denied");
        }
    }
#elif UNITY_ANDROID
    private void PermissionCallbacksPermissionGranted(string permissionName)
    {
        StartCoroutine(DelayedCameraInitialization());
    }

    private IEnumerator DelayedCameraInitialization()
    {
        yield return null;
        InitializeCamera();
    }

    private void PermissionCallbacksPermissionDenied(string permissionName)
    {
        Debug.LogWarning($"Permission {permissionName} Denied");
    }

    private void AskCameraPermission()
    {
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionDenied += PermissionCallbacksPermissionDenied;
        callbacks.PermissionGranted += PermissionCallbacksPermissionGranted;
        Permission.RequestUserPermission(Permission.Camera, callbacks);
    }
#endif

    void Start()
    {
#if UNITY_IOS || UNITY_WEBGL
        StartCoroutine(
            AskForPermissionIfRequired(
                UserAuthorization.WebCam,
                () =>
                {
                    InitializeCamera();
                }
            )
        );
        return;
#elif UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            AskCameraPermission();
            return;
        }
#endif
        InitializeCamera();
    }

    private void InitializeCamera()
    {
        // Find the default webcam device
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No webcam devices found.");
            return;
        }

        // Use the first available device
        _webcamTexture = new WebCamTexture(devices[0].name);

        // Set the material's main texture to the webcam feed
        if (TargetMaterial != null)
        {
            TargetMaterial.mainTexture = _webcamTexture;
        }
        else
        {
            Debug.LogWarning(
                "TargetMaterial is not assigned. Webcam feed will not be displayed on a material."
            );
        }

        // Start playing the webcam feed
        _webcamTexture.Play();

        // HACK: Enable the ColorDataView component from here in order to see the left and right quads simulatenously
        gameObject.GetComponent<ColorDataView>().enabled = true;

        Debug.Log(
            $"Webcam started: {_webcamTexture.deviceName} ({_webcamTexture.width}x{_webcamTexture.height})"
        );
    }

    void Update()
    {
        if (_webcamTexture != null && _webcamTexture.isPlaying)
        {
            // If a Render Texture is assigned, blit the webcam feed to it
            if (TargetRenderTexture != null)
            {
                Graphics.Blit(_webcamTexture, TargetRenderTexture);
            }
        }
    }

    void OnApplicationQuit()
    {
        if (_webcamTexture != null)
        {
            _webcamTexture.Stop();
            Destroy(_webcamTexture);
            Debug.Log("Webcam stopped and disposed.");
        }
    }
}
