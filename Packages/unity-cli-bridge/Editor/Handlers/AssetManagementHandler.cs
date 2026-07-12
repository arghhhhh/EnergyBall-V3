using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D;
using UnityEngine.U2D;
using UnityCliBridge.Logging;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Handlers
{
    /// <summary>
    /// Handles asset management operations including prefabs, materials, and imports
    /// </summary>
    public static class AssetManagementHandler
    {
        /// <summary>
        /// Creates a new prefab from a GameObject or from scratch
        /// </summary>
        public static object CreatePrefab(JObject parameters)
        {
            try
            {
                // Parse parameters
                string gameObjectPath = parameters["gameObjectPath"]?.ToString();
                string prefabPath = parameters["prefabPath"]?.ToString();
                bool createFromTemplate = parameters["createFromTemplate"]?.ToObject<bool>() ?? false;
                bool overwrite = parameters["overwrite"]?.ToObject<bool>() ?? false;

                // Validate prefab path
                if (string.IsNullOrEmpty(prefabPath))
                {
                    return new { error = "prefabPath is required" };
                }

                if (!prefabPath.StartsWith("Assets/") || !prefabPath.EndsWith(".prefab"))
                {
                    return new { error = "prefabPath must start with Assets/ and end with .prefab" };
                }

                // Check if prefab already exists
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null && !overwrite)
                {
                    return new { error = $"Prefab already exists at {prefabPath}. Set overwrite to true to replace it." };
                }

                // Ensure directory exists
                string directory = Path.GetDirectoryName(prefabPath);
                if (!AssetDatabase.IsValidFolder(directory))
                {
                    Directory.CreateDirectory(directory);
                    UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                }

                GameObject prefabAsset = null;

                if (createFromTemplate)
                {
                    // Create empty prefab
                    GameObject emptyGO = new GameObject("Empty");
                    prefabAsset = PrefabUtility.SaveAsPrefabAsset(emptyGO, prefabPath);
                    UnityEngine.Object.DestroyImmediate(emptyGO);
                }
                else if (!string.IsNullOrEmpty(gameObjectPath))
                {
                    // Find the GameObject
                    GameObject sourceObject = GameObject.Find(gameObjectPath);
                    if (sourceObject == null)
                    {
                        return new { error = $"GameObject not found at path: {gameObjectPath}" };
                    }

                    // Create prefab from GameObject
                    prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(sourceObject, prefabPath, InteractionMode.UserAction);
                }
                else
                {
                    return new { error = "Either gameObjectPath or createFromTemplate must be specified" };
                }

                if (prefabAsset == null)
                {
                    return new { error = "Failed to create prefab" };
                }

                // Get asset GUID
                string guid = AssetDatabase.AssetPathToGUID(prefabPath);

                return new
                {
                    success = true,
                    prefabPath = prefabPath,
                    guid = guid,
                    message = createFromTemplate ? "Empty prefab created successfully" : "Prefab created successfully"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in CreatePrefab: {e.Message}");
                return new { error = $"Failed to create prefab: {e.Message}" };
            }
        }

        /// <summary>
        /// Modifies properties of an existing prefab
        /// </summary>
        public static object ModifyPrefab(JObject parameters)
        {
            try
            {
                // Parse parameters
                string prefabPath = parameters["prefabPath"]?.ToString();
                JObject modifications = parameters["modifications"] as JObject;
                bool applyToInstances = parameters["applyToInstances"]?.ToObject<bool>() ?? true;

                // Validate parameters
                if (string.IsNullOrEmpty(prefabPath))
                {
                    return new { error = "prefabPath is required" };
                }

                if (modifications == null || !modifications.HasValues)
                {
                    return new { error = "modifications object is required and cannot be empty" };
                }

                // Load the prefab
                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset == null)
                {
                    return new { error = $"Prefab not found at path: {prefabPath}" };
                }

                // Track modifications
                List<string> modifiedProperties = new List<string>();
                
                // Load prefab contents for editing
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                
                try
                {
                    // Apply modifications
                    foreach (var prop in modifications.Properties())
                    {
                        if (ApplyModificationToGameObject(prefabRoot, prop.Name, prop.Value))
                        {
                            modifiedProperties.Add(prop.Name);
                        }
                    }

                    // Save the modified prefab
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                }
                finally
                {
                    // Unload prefab contents
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }

                int affectedInstances = 0;
                
                // Apply to instances if requested
                if (applyToInstances)
                {
                    GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                    foreach (var obj in allObjects)
                    {
                        if (PrefabUtility.GetCorrespondingObjectFromSource(obj) == prefabAsset ||
                            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obj) == prefabPath)
                        {
                            PrefabUtility.RevertPrefabInstance(obj, InteractionMode.AutomatedAction);
                            affectedInstances++;
                        }
                    }
                }

                return new
                {
                    success = true,
                    prefabPath = prefabPath,
                    modifiedProperties = modifiedProperties.ToArray(),
                    affectedInstances = affectedInstances,
                    message = applyToInstances ? 
                        "Prefab modified successfully" : 
                        "Prefab modified without updating instances"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in ModifyPrefab: {e.Message}");
                return new { error = $"Failed to modify prefab: {e.Message}" };
            }
        }

        /// <summary>
        /// Instantiates a prefab in the scene
        /// </summary>
        public static object InstantiatePrefab(JObject parameters)
        {
            try
            {
                // Parse parameters
                string prefabPath = parameters["prefabPath"]?.ToString();
                var position = ParseVector3(parameters["position"]) ?? Vector3.zero;
                var rotation = ParseVector3(parameters["rotation"]) ?? Vector3.zero;
                string parentPath = parameters["parent"]?.ToString();
                string name = parameters["name"]?.ToString();

                // Validate prefab path
                if (string.IsNullOrEmpty(prefabPath))
                {
                    return new { error = "prefabPath is required" };
                }

                // Load the prefab
                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset == null)
                {
                    return new { error = $"Prefab not found at path: {prefabPath}" };
                }

                // Find parent if specified
                GameObject parent = null;
                if (!string.IsNullOrEmpty(parentPath))
                {
                    parent = GameObject.Find(parentPath);
                    if (parent == null)
                    {
                        return new { error = $"Parent GameObject not found at path: {parentPath}" };
                    }
                }

                // Instantiate the prefab
                GameObject instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                if (instance == null)
                {
                    return new { error = "Failed to instantiate prefab" };
                }

                // Set transform
                instance.transform.position = position;
                instance.transform.rotation = Quaternion.Euler(rotation);
                
                // Set parent
                if (parent != null)
                {
                    instance.transform.SetParent(parent.transform, true);
                }

                // Set name if provided
                if (!string.IsNullOrEmpty(name))
                {
                    instance.name = name;
                }

                // Register undo
                Undo.RegisterCreatedObjectUndo(instance, "Instantiate Prefab");

                // Get the path to the created object
                string gameObjectPath = GetGameObjectPath(instance);

                return new
                {
                    success = true,
                    gameObjectPath = gameObjectPath,
                    prefabPath = prefabPath,
                    position = new { x = position.x, y = position.y, z = position.z },
                    rotation = new { x = rotation.x, y = rotation.y, z = rotation.z },
                    parent = parentPath,
                    name = instance.name,
                    message = "Prefab instantiated successfully"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in InstantiatePrefab: {e.Message}");
                return new { error = $"Failed to instantiate prefab: {e.Message}" };
            }
        }

        /// <summary>
        /// Creates a new material with specified shader and properties
        /// </summary>
        public static object CreateMaterial(JObject parameters)
        {
            try
            {
                // Parse parameters
                string materialPath = parameters["materialPath"]?.ToString();
                string shader = parameters["shader"]?.ToString() ?? "Standard";
                JObject properties = parameters["properties"] as JObject;
                string copyFrom = parameters["copyFrom"]?.ToString();
                bool overwrite = parameters["overwrite"]?.ToObject<bool>() ?? false;

                // Validate material path
                if (string.IsNullOrEmpty(materialPath))
                {
                    return new { error = "materialPath is required" };
                }

                if (!materialPath.StartsWith("Assets/") || !materialPath.EndsWith(".mat"))
                {
                    return new { error = "materialPath must start with Assets/ and end with .mat" };
                }

                // Check if material already exists
                if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null && !overwrite)
                {
                    return new { error = $"Material already exists at {materialPath}. Set overwrite to true to replace it." };
                }

                // Ensure directory exists
                string directory = Path.GetDirectoryName(materialPath);
                if (!AssetDatabase.IsValidFolder(directory))
                {
                    Directory.CreateDirectory(directory);
                    UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
                }

                Material material = null;
                Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                List<string> propertiesSet = new List<string>();

                if (!string.IsNullOrEmpty(copyFrom))
                {
                    // Copy from existing material
                    Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(copyFrom);
                    if (sourceMaterial == null)
                    {
                        return new { error = $"Source material not found at: {copyFrom}" };
                    }

                    material = new Material(sourceMaterial);
                }
                else
                {
                    // Find shader
                    Shader shaderAsset = Shader.Find(shader);
                    if (shaderAsset == null)
                    {
                        return new { error = $"Shader not found: {shader}" };
                    }

                    material = new Material(shaderAsset);
                }

                // Apply properties if provided
                if (properties != null && properties.HasValues)
                {
                    foreach (var prop in properties.Properties())
                    {
                        if (ApplyMaterialProperty(material, prop.Name, prop.Value))
                        {
                            propertiesSet.Add(prop.Name);
                        }
                    }
                }

                // Save the material
                if (existingMaterial != null && overwrite)
                {
                    EditorUtility.CopySerialized(material, existingMaterial);
                    UnityEngine.Object.DestroyImmediate(material);
                    material = existingMaterial;
                    EditorUtility.SetDirty(material);
                }
                else
                {
                    AssetDatabase.CreateAsset(material, materialPath);
                }

                AssetDatabase.SaveAssets();

                // Get asset GUID
                string guid = AssetDatabase.AssetPathToGUID(materialPath);

                return new
                {
                    success = true,
                    materialPath = materialPath,
                    shader = material.shader.name,
                    guid = guid,
                    propertiesSet = propertiesSet.ToArray(),
                    copiedFrom = copyFrom,
                    message = !string.IsNullOrEmpty(copyFrom) ? 
                        "Material created from copy successfully" : 
                        "Material created successfully"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in CreateMaterial: {e.Message}");
                return new { error = $"Failed to create material: {e.Message}" };
            }
        }

        /// <summary>
        /// Modifies properties of an existing material
        /// </summary>
        public static object ModifyMaterial(JObject parameters)
        {
            try
            {
                // Parse parameters
                string materialPath = parameters["materialPath"]?.ToString();
                JObject properties = parameters["properties"] as JObject;
                string shader = parameters["shader"]?.ToString();

                // Validate material path
                if (string.IsNullOrEmpty(materialPath))
                {
                    return new { error = "materialPath is required" };
                }

                if (!materialPath.StartsWith("Assets/") || !materialPath.EndsWith(".mat"))
                {
                    return new { error = "materialPath must start with Assets/ and end with .mat" };
                }

                // Validate requested changes
                bool hasPropertyChanges = properties != null && properties.HasValues;
                bool hasShaderChange = !string.IsNullOrEmpty(shader);
                if (!hasPropertyChanges && !hasShaderChange)
                {
                    return new { error = "Either shader or a non-empty properties object is required" };
                }

                // Load the material
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    return new { error = $"Material not found at path: {materialPath}" };
                }

                List<string> propertiesModified = new List<string>();
                bool shaderChanged = false;
                string previousShader = material.shader.name;

                // Change shader if specified
                if (!string.IsNullOrEmpty(shader))
                {
                    Shader newShader = Shader.Find(shader);
                    if (newShader == null)
                    {
                        return new { error = $"Shader not found: {shader}" };
                    }

                    if (material.shader != newShader)
                    {
                        material.shader = newShader;
                        shaderChanged = true;
                    }
                }

                // Apply property modifications
                if (properties != null)
                {
                    foreach (var prop in properties.Properties())
                    {
                        if (ApplyMaterialProperty(material, prop.Name, prop.Value))
                        {
                            propertiesModified.Add(prop.Name);
                        }
                    }
                }

                // Mark as dirty to ensure changes are saved
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    materialPath = materialPath,
                    propertiesModified = propertiesModified.ToArray(),
                    shaderChanged = shaderChanged,
                    previousShader = shaderChanged ? previousShader : null,
                    newShader = shaderChanged ? shader : null,
                    message = "Material modified successfully"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in ModifyMaterial: {e.Message}");
                return new { error = $"Failed to modify material: {e.Message}" };
            }
        }

        /// <summary>
        /// Creates or overwrites an AnimatorController asset with parameters, states, and transitions.
        /// </summary>
        public static object CreateAnimatorController(JObject parameters)
        {
            try
            {
                string controllerPath = parameters["controllerPath"]?.ToString();
                bool overwrite = parameters["overwrite"]?.ToObject<bool>() ?? false;

                var validationErrors = ValidateAnimatorControllerRequest(parameters, controllerPath, overwrite);
                if (validationErrors.Count > 0)
                {
                    return new
                    {
                        error = "AnimatorController request validation failed",
                        validationErrors = validationErrors.ToArray()
                    };
                }

                EnsureAssetDirectoryExists(controllerPath);

                AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                bool existed = controller != null;

                if (controller == null)
                {
                    controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    if (controller != null)
                    {
                        ResetAnimatorController(controller);
                    }
                }
                else
                {
                    ResetAnimatorController(controller);
                }

                if (controller == null)
                {
                    return new { error = $"Failed to create AnimatorController at {controllerPath}" };
                }

                AnimatorStateMachine stateMachine = GetOrCreateBaseLayerStateMachine(controller);
                var parameterDefinitions = parameters["parameters"] as JArray ?? new JArray();
                var stateDefinitions = parameters["states"] as JArray ?? new JArray();
                var transitionDefinitions = parameters["transitions"] as JArray ?? new JArray();

                foreach (JObject parameter in parameterDefinitions.OfType<JObject>())
                {
                    controller.AddParameter(BuildAnimatorParameter(parameter));
                }

                var statesByName = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
                string firstStateName = null;

                foreach (JObject stateDefinition in stateDefinitions.OfType<JObject>())
                {
                    string stateName = stateDefinition["name"]?.ToString();
                    Motion motion = null;
                    string motionPath = stateDefinition["motionPath"]?.ToString();

                    if (!string.IsNullOrEmpty(motionPath))
                    {
                        motion = AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
                    }

                    AnimatorState state = stateMachine.AddState(stateName);
                    state.motion = motion;
                    statesByName[stateName] = state;
                    if (firstStateName == null)
                    {
                        firstStateName = stateName;
                    }
                }

                string defaultStateName = parameters["defaultState"]?.ToString();
                if (string.IsNullOrEmpty(defaultStateName))
                {
                    defaultStateName = firstStateName;
                }

                if (!string.IsNullOrEmpty(defaultStateName))
                {
                    stateMachine.defaultState = statesByName[defaultStateName];
                }

                int transitionCount = 0;

                foreach (JObject transitionDefinition in transitionDefinitions.OfType<JObject>())
                {
                    AnimatorState fromState = statesByName[transitionDefinition["from"]?.ToString()];
                    AnimatorState toState = statesByName[transitionDefinition["to"]?.ToString()];
                    AnimatorStateTransition transition = fromState.AddTransition(toState);

                    if (transitionDefinition["hasExitTime"] != null)
                    {
                        transition.hasExitTime = transitionDefinition["hasExitTime"].ToObject<bool>();
                    }

                    if (transitionDefinition["exitTime"] != null)
                    {
                        transition.hasExitTime = true;
                        transition.exitTime = transitionDefinition["exitTime"].ToObject<float>();
                    }

                    if (transitionDefinition["duration"] != null)
                    {
                        transition.duration = transitionDefinition["duration"].ToObject<float>();
                    }

                    var conditions = transitionDefinition["conditions"] as JArray;
                    if (conditions != null)
                    {
                        foreach (JObject condition in conditions.OfType<JObject>())
                        {
                            transition.AddCondition(
                                ParseAnimatorConditionMode(condition["mode"]?.ToString()),
                                condition["threshold"] != null ? condition["threshold"].ToObject<float>() : 0f,
                                condition["parameter"]?.ToString()
                            );
                        }
                    }

                    transitionCount++;
                }

                EditorUtility.SetDirty(stateMachine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    controllerPath = controllerPath,
                    guid = AssetDatabase.AssetPathToGUID(controllerPath),
                    defaultState = defaultStateName,
                    parameterCount = controller.parameters.Length,
                    stateCount = statesByName.Count,
                    transitionCount = transitionCount,
                    message = existed ? "AnimatorController overwritten successfully" : "AnimatorController created successfully"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in CreateAnimatorController: {e.Message}");
                return new { error = $"Failed to create AnimatorController: {e.Message}" };
            }
        }

        /// <summary>
        /// Creates or overwrites an AnimationClip asset from sprite frames.
        /// </summary>
        public static object CreateAnimationClip(JObject parameters)
        {
            try
            {
                string clipPath = parameters["clipPath"]?.ToString();
                bool overwrite = parameters["overwrite"]?.ToObject<bool>() ?? false;

                var validationErrors = ValidateAnimationClipRequest(parameters, clipPath, overwrite);
                if (validationErrors.Count > 0)
                {
                    return new
                    {
                        error = "AnimationClip request validation failed",
                        validationErrors = validationErrors.ToArray()
                    };
                }

                EnsureAssetDirectoryExists(clipPath);

                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                bool existed = clip != null;

                if (clip == null)
                {
                    clip = new AnimationClip();
                    AssetDatabase.CreateAsset(clip, clipPath);
                }
                else
                {
                    ResetAnimationClip(clip);
                }

                if (clip == null)
                {
                    return new { error = $"Failed to create AnimationClip at {clipPath}" };
                }

                float frameRate = parameters["frameRate"]?.ToObject<float>() ?? 12f;
                bool loopTime = parameters["loopTime"]?.ToObject<bool>() ?? false;
                string bindingPath = parameters["bindingPath"]?.ToString() ?? string.Empty;
                JArray spritePaths = parameters["spritePaths"] as JArray ?? new JArray();

                clip.frameRate = frameRate;

                ObjectReferenceKeyframe[] keyframes = BuildSpriteKeyframes(spritePaths, frameRate);
                var binding = new EditorCurveBinding
                {
                    path = bindingPath,
                    type = typeof(SpriteRenderer),
                    propertyName = "m_Sprite"
                };

                AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
                SetAnimationClipLoopTime(clip, loopTime);

                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    clipPath = clipPath,
                    guid = AssetDatabase.AssetPathToGUID(clipPath),
                    frameRate = clip.frameRate,
                    frameCount = keyframes.Length,
                    loopTime = GetAnimationClipLoopTime(clip),
                    bindingPath = bindingPath,
                    message = existed ? "AnimationClip overwritten successfully" : "AnimationClip created successfully"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in CreateAnimationClip: {e.Message}");
                return new { error = $"Failed to create AnimationClip: {e.Message}" };
            }
        }

        /// <summary>
        /// Creates or overwrites a SpriteAtlas asset with packables and settings.
        /// </summary>
        public static object CreateSpriteAtlas(JObject parameters)
        {
            try
            {
                string atlasPath = parameters["atlasPath"]?.ToString();
                bool overwrite = parameters["overwrite"]?.ToObject<bool>() ?? false;

                var validationErrors = ValidateSpriteAtlasRequest(parameters, atlasPath, overwrite);
                if (validationErrors.Count > 0)
                {
                    return new
                    {
                        error = "SpriteAtlas request validation failed",
                        validationErrors = validationErrors.ToArray()
                    };
                }

                EnsureAssetDirectoryExists(atlasPath);

                SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                bool existed = atlas != null;

                if (atlas == null)
                {
                    atlas = new SpriteAtlas();
                    AssetDatabase.CreateAsset(atlas, atlasPath);
                }
                else
                {
                    ResetSpriteAtlas(atlas);
                }

                if (atlas == null)
                {
                    return new { error = $"Failed to create SpriteAtlas at {atlasPath}" };
                }

                ApplySpriteAtlasSettings(atlas, parameters["packingSettings"] as JObject, parameters["textureSettings"] as JObject);

                UnityEngine.Object[] packables = ResolveSpriteAtlasPackables(parameters["packables"] as JArray);
                if (packables.Length > 0)
                {
                    SpriteAtlasExtensions.Add(atlas, packables);
                }

                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    atlasPath = atlasPath,
                    guid = AssetDatabase.AssetPathToGUID(atlasPath),
                    packableCount = packables.Length,
                    message = existed ? "SpriteAtlas overwritten successfully" : "SpriteAtlas created successfully"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in CreateSpriteAtlas: {e.Message}");
                return new { error = $"Failed to create SpriteAtlas: {e.Message}" };
            }
        }

        #region Helper Methods

        private static List<string> ValidateAnimatorControllerRequest(JObject parameters, string controllerPath, bool overwrite)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(controllerPath))
            {
                errors.Add("controllerPath is required");
                return errors;
            }

            if (!controllerPath.StartsWith("Assets/") || !controllerPath.EndsWith(".controller"))
            {
                errors.Add("controllerPath must start with Assets/ and end with .controller");
            }

            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(controllerPath);
            AnimatorController existingController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existingAsset != null && existingController == null)
            {
                errors.Add($"Asset already exists at {controllerPath} and is not an AnimatorController");
            }
            else if (existingController != null && !overwrite)
            {
                errors.Add($"AnimatorController already exists at {controllerPath}. Set overwrite to true to replace it.");
            }

            JArray parameterDefinitions = ExpectArray(parameters["parameters"], "parameters", errors);
            JArray stateDefinitions = ExpectArray(parameters["states"], "states", errors);
            JArray transitionDefinitions = ExpectArray(parameters["transitions"], "transitions", errors);

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            if (parameterDefinitions != null)
            {
                for (int i = 0; i < parameterDefinitions.Count; i++)
                {
                    JObject parameter = parameterDefinitions[i] as JObject;
                    if (parameter == null)
                    {
                        errors.Add($"parameters[{i}] must be an object");
                        continue;
                    }

                    string name = parameter["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        errors.Add($"parameters[{i}].name is required");
                    }
                    else if (!parameterNames.Add(name))
                    {
                        errors.Add($"Duplicate parameter name: {name}");
                    }

                    string typeName = parameter["type"]?.ToString();
                    if (!TryParseAnimatorParameterType(typeName, out _))
                    {
                        errors.Add($"parameters[{i}].type must be one of Bool, Float, Int, Trigger");
                    }

                    ValidateNumericToken(parameter["defaultFloat"], $"parameters[{i}].defaultFloat", errors);
                    ValidateIntegerToken(parameter["defaultInt"], $"parameters[{i}].defaultInt", errors);
                }
            }

            var stateNames = new HashSet<string>(StringComparer.Ordinal);
            if (stateDefinitions != null)
            {
                for (int i = 0; i < stateDefinitions.Count; i++)
                {
                    JObject state = stateDefinitions[i] as JObject;
                    if (state == null)
                    {
                        errors.Add($"states[{i}] must be an object");
                        continue;
                    }

                    string name = state["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        errors.Add($"states[{i}].name is required");
                    }
                    else if (!stateNames.Add(name))
                    {
                        errors.Add($"Duplicate state name: {name}");
                    }

                    string motionPath = state["motionPath"]?.ToString();
                    if (!string.IsNullOrEmpty(motionPath))
                    {
                        if (!motionPath.StartsWith("Assets/"))
                        {
                            errors.Add($"states[{i}].motionPath must start with Assets/: {motionPath}");
                        }
                        else if (AssetDatabase.LoadAssetAtPath<Motion>(motionPath) == null)
                        {
                            errors.Add($"states[{i}].motionPath does not point to a Motion asset: {motionPath}");
                        }
                    }
                }
            }

            string defaultStateName = parameters["defaultState"]?.ToString();
            if (!string.IsNullOrEmpty(defaultStateName) && !stateNames.Contains(defaultStateName))
            {
                errors.Add($"defaultState references unknown state: {defaultStateName}");
            }

            if (transitionDefinitions != null)
            {
                for (int i = 0; i < transitionDefinitions.Count; i++)
                {
                    JObject transition = transitionDefinitions[i] as JObject;
                    if (transition == null)
                    {
                        errors.Add($"transitions[{i}] must be an object");
                        continue;
                    }

                    string fromState = transition["from"]?.ToString();
                    string toState = transition["to"]?.ToString();

                    if (string.IsNullOrWhiteSpace(fromState))
                    {
                        errors.Add($"transitions[{i}].from is required");
                    }
                    else if (!stateNames.Contains(fromState))
                    {
                        errors.Add($"transitions[{i}].from references unknown state: {fromState}");
                    }

                    if (string.IsNullOrWhiteSpace(toState))
                    {
                        errors.Add($"transitions[{i}].to is required");
                    }
                    else if (!stateNames.Contains(toState))
                    {
                        errors.Add($"transitions[{i}].to references unknown state: {toState}");
                    }

                    ValidateNumericToken(transition["duration"], $"transitions[{i}].duration", errors);
                    ValidateNumericToken(transition["exitTime"], $"transitions[{i}].exitTime", errors);

                    JArray conditions = ExpectArray(transition["conditions"], $"transitions[{i}].conditions", errors);
                    if (conditions == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < conditions.Count; j++)
                    {
                        JObject condition = conditions[j] as JObject;
                        if (condition == null)
                        {
                            errors.Add($"transitions[{i}].conditions[{j}] must be an object");
                            continue;
                        }

                        string parameterName = condition["parameter"]?.ToString();
                        if (string.IsNullOrWhiteSpace(parameterName))
                        {
                            errors.Add($"transitions[{i}].conditions[{j}].parameter is required");
                        }
                        else if (!parameterNames.Contains(parameterName))
                        {
                            errors.Add($"transitions[{i}].conditions[{j}] references unknown parameter: {parameterName}");
                        }

                        string modeName = condition["mode"]?.ToString();
                        if (!TryParseAnimatorConditionMode(modeName, out _))
                        {
                            errors.Add($"transitions[{i}].conditions[{j}].mode must be one of If, IfNot, Greater, Less, Equals, NotEqual");
                        }

                        ValidateNumericToken(condition["threshold"], $"transitions[{i}].conditions[{j}].threshold", errors);
                    }
                }
            }

            return errors;
        }

        private static JArray ExpectArray(JToken token, string fieldName, List<string> errors)
        {
            if (token == null)
            {
                return null;
            }

            JArray array = token as JArray;
            if (array == null)
            {
                errors.Add($"{fieldName} must be an array");
            }

            return array;
        }

        private static void ValidateNumericToken(JToken token, string fieldName, List<string> errors)
        {
            if (token == null)
            {
                return;
            }

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                errors.Add($"{fieldName} must be a number");
            }
        }

        private static void ValidateIntegerToken(JToken token, string fieldName, List<string> errors)
        {
            if (token == null)
            {
                return;
            }

            if (token.Type != JTokenType.Integer)
            {
                errors.Add($"{fieldName} must be an integer");
            }
        }

        private static void EnsureAssetDirectoryExists(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
        }

        private static AnimatorControllerParameter BuildAnimatorParameter(JObject parameter)
        {
            var animatorParameter = new AnimatorControllerParameter
            {
                name = parameter["name"]?.ToString(),
                type = ParseAnimatorParameterType(parameter["type"]?.ToString())
            };

            switch (animatorParameter.type)
            {
                case AnimatorControllerParameterType.Bool:
                    animatorParameter.defaultBool = parameter["defaultBool"]?.ToObject<bool>() ?? false;
                    break;
                case AnimatorControllerParameterType.Float:
                    animatorParameter.defaultFloat = parameter["defaultFloat"]?.ToObject<float>() ?? 0f;
                    break;
                case AnimatorControllerParameterType.Int:
                    animatorParameter.defaultInt = parameter["defaultInt"]?.ToObject<int>() ?? 0;
                    break;
            }

            return animatorParameter;
        }

        private static bool TryParseAnimatorParameterType(string typeName, out AnimatorControllerParameterType type)
        {
            switch (typeName)
            {
                case "Bool":
                    type = AnimatorControllerParameterType.Bool;
                    return true;
                case "Float":
                    type = AnimatorControllerParameterType.Float;
                    return true;
                case "Int":
                    type = AnimatorControllerParameterType.Int;
                    return true;
                case "Trigger":
                    type = AnimatorControllerParameterType.Trigger;
                    return true;
                default:
                    type = AnimatorControllerParameterType.Float;
                    return false;
            }
        }

        private static AnimatorControllerParameterType ParseAnimatorParameterType(string typeName)
        {
            AnimatorControllerParameterType type;
            return TryParseAnimatorParameterType(typeName, out type) ? type : AnimatorControllerParameterType.Float;
        }

        private static bool TryParseAnimatorConditionMode(string modeName, out AnimatorConditionMode mode)
        {
            switch (modeName)
            {
                case "If":
                    mode = AnimatorConditionMode.If;
                    return true;
                case "IfNot":
                    mode = AnimatorConditionMode.IfNot;
                    return true;
                case "Greater":
                    mode = AnimatorConditionMode.Greater;
                    return true;
                case "Less":
                    mode = AnimatorConditionMode.Less;
                    return true;
                case "Equals":
                    mode = AnimatorConditionMode.Equals;
                    return true;
                case "NotEqual":
                    mode = AnimatorConditionMode.NotEqual;
                    return true;
                default:
                    mode = AnimatorConditionMode.If;
                    return false;
            }
        }

        private static AnimatorConditionMode ParseAnimatorConditionMode(string modeName)
        {
            AnimatorConditionMode mode;
            return TryParseAnimatorConditionMode(modeName, out mode) ? mode : AnimatorConditionMode.If;
        }

        private static void ResetAnimatorController(AnimatorController controller)
        {
            for (int i = controller.parameters.Length - 1; i >= 0; i--)
            {
                controller.RemoveParameter(i);
            }

            for (int i = controller.layers.Length - 1; i > 0; i--)
            {
                controller.RemoveLayer(i);
            }

            ClearStateMachine(GetOrCreateBaseLayerStateMachine(controller));
        }

        private static AnimatorStateMachine GetOrCreateBaseLayerStateMachine(AnimatorController controller)
        {
            if (controller.layers == null || controller.layers.Length == 0)
            {
                controller.AddLayer(CreateBaseLayer(controller));
            }

            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer baseLayer = layers[0];
            baseLayer.name = "Base Layer";
            baseLayer.defaultWeight = 1f;

            if (baseLayer.stateMachine == null)
            {
                baseLayer.stateMachine = new AnimatorStateMachine { name = "Base Layer" };
                AssetDatabase.AddObjectToAsset(baseLayer.stateMachine, controller);
            }

            layers[0] = baseLayer;
            controller.layers = layers;
            return controller.layers[0].stateMachine;
        }

        private static AnimatorControllerLayer CreateBaseLayer(AnimatorController controller)
        {
            var stateMachine = new AnimatorStateMachine { name = "Base Layer" };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            return new AnimatorControllerLayer
            {
                name = "Base Layer",
                defaultWeight = 1f,
                stateMachine = stateMachine
            };
        }

        private static void ClearStateMachine(AnimatorStateMachine stateMachine)
        {
            foreach (var transition in stateMachine.anyStateTransitions.ToArray())
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }

            foreach (var transition in stateMachine.entryTransitions.ToArray())
            {
                stateMachine.RemoveEntryTransition(transition);
            }

            foreach (var childStateMachine in stateMachine.stateMachines.ToArray())
            {
                stateMachine.RemoveStateMachine(childStateMachine.stateMachine);
            }

            foreach (var childState in stateMachine.states.ToArray())
            {
                stateMachine.RemoveState(childState.state);
            }

            stateMachine.defaultState = null;
        }

        private static List<string> ValidateAnimationClipRequest(JObject parameters, string clipPath, bool overwrite)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(clipPath))
            {
                errors.Add("clipPath is required");
                return errors;
            }

            if (!clipPath.StartsWith("Assets/") || !clipPath.EndsWith(".anim"))
            {
                errors.Add("clipPath must start with Assets/ and end with .anim");
            }

            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(clipPath);
            AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existingAsset != null && existingClip == null)
            {
                errors.Add($"Asset already exists at {clipPath} and is not an AnimationClip");
            }
            else if (existingClip != null && !overwrite)
            {
                errors.Add($"AnimationClip already exists at {clipPath}. Set overwrite to true to replace it.");
            }

            JArray spritePaths = ExpectArray(parameters["spritePaths"], "spritePaths", errors);
            if (spritePaths == null || spritePaths.Count == 0)
            {
                errors.Add("spritePaths must contain at least one sprite reference");
            }
            else
            {
                for (int i = 0; i < spritePaths.Count; i++)
                {
                    if (spritePaths[i].Type != JTokenType.String)
                    {
                        errors.Add($"spritePaths[{i}] must be a string");
                        continue;
                    }

                    string spriteReference = spritePaths[i].ToString();
                    if (ResolveSpriteReference(spriteReference) == null)
                    {
                        errors.Add($"spritePaths[{i}] must resolve to an existing Sprite asset: {spriteReference}");
                    }
                }
            }

            ValidateNumericToken(parameters["frameRate"], "frameRate", errors);
            if (parameters["frameRate"] != null && parameters["frameRate"].ToObject<float>() <= 0f)
            {
                errors.Add("frameRate must be greater than 0");
            }

            return errors;
        }

        private static void ResetAnimationClip(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            }

            clip.events = Array.Empty<AnimationEvent>();
        }

        private static ObjectReferenceKeyframe[] BuildSpriteKeyframes(JArray spritePaths, float frameRate)
        {
            var keyframes = new ObjectReferenceKeyframe[spritePaths.Count];
            for (int i = 0; i < spritePaths.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / frameRate,
                    value = ResolveSpriteReference(spritePaths[i].ToString())
                };
            }

            return keyframes;
        }

        private static Sprite ResolveSpriteReference(string spriteReference)
        {
            if (string.IsNullOrWhiteSpace(spriteReference))
            {
                return null;
            }

            string assetPath = spriteReference;
            string spriteName = null;
            int separatorIndex = spriteReference.IndexOf('#');
            if (separatorIndex >= 0)
            {
                assetPath = spriteReference.Substring(0, separatorIndex);
                spriteName = spriteReference.Substring(separatorIndex + 1);
            }

            if (!assetPath.StartsWith("Assets/"))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(spriteName))
            {
                return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                    .OfType<Sprite>()
                    .FirstOrDefault(sprite => sprite.name == spriteName);
            }

            Sprite spriteAsset = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (spriteAsset != null)
            {
                return spriteAsset;
            }

            return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().FirstOrDefault();
        }

        private static void SetAnimationClipLoopTime(AnimationClip clip, bool loopTime)
        {
            var serializedClip = new SerializedObject(clip);
            var clipSettings = serializedClip.FindProperty("m_AnimationClipSettings");
            if (clipSettings != null)
            {
                var loopTimeProperty = clipSettings.FindPropertyRelative("m_LoopTime");
                if (loopTimeProperty != null)
                {
                    loopTimeProperty.boolValue = loopTime;
                    serializedClip.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static bool GetAnimationClipLoopTime(AnimationClip clip)
        {
            var serializedClip = new SerializedObject(clip);
            var clipSettings = serializedClip.FindProperty("m_AnimationClipSettings");
            if (clipSettings == null)
            {
                return false;
            }

            var loopTimeProperty = clipSettings.FindPropertyRelative("m_LoopTime");
            return loopTimeProperty != null && loopTimeProperty.boolValue;
        }

        private static List<string> ValidateSpriteAtlasRequest(JObject parameters, string atlasPath, bool overwrite)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(atlasPath))
            {
                errors.Add("atlasPath is required");
                return errors;
            }

            if (!atlasPath.StartsWith("Assets/") || !atlasPath.EndsWith(".spriteatlas"))
            {
                errors.Add("atlasPath must start with Assets/ and end with .spriteatlas");
            }

            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(atlasPath);
            SpriteAtlas existingAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (existingAsset != null && existingAtlas == null)
            {
                errors.Add($"Asset already exists at {atlasPath} and is not a SpriteAtlas");
            }
            else if (existingAtlas != null && !overwrite)
            {
                errors.Add($"SpriteAtlas already exists at {atlasPath}. Set overwrite to true to replace it.");
            }

            JArray packables = ExpectArray(parameters["packables"], "packables", errors);
            if (packables != null)
            {
                for (int i = 0; i < packables.Count; i++)
                {
                    if (packables[i].Type != JTokenType.String)
                    {
                        errors.Add($"packables[{i}] must be a string path");
                        continue;
                    }

                    string packablePath = packables[i].ToString();
                    if (!IsValidSpriteAtlasPackable(packablePath))
                    {
                        errors.Add($"packables[{i}] must point to an existing folder, Sprite, or Texture2D asset: {packablePath}");
                    }
                }
            }

            JObject packingSettings = ExpectObject(parameters["packingSettings"], "packingSettings", errors);
            if (packingSettings != null)
            {
                ValidateIntegerToken(packingSettings["padding"], "packingSettings.padding", errors);
                if (packingSettings["padding"] != null && packingSettings["padding"].ToObject<int>() < 0)
                {
                    errors.Add("packingSettings.padding must be >= 0");
                }
            }

            JObject textureSettings = ExpectObject(parameters["textureSettings"], "textureSettings", errors);
            if (textureSettings != null)
            {
                string filterMode = textureSettings["filterMode"]?.ToString();
                if (!string.IsNullOrEmpty(filterMode) && !TryParseFilterMode(filterMode, out _))
                {
                    errors.Add("textureSettings.filterMode must be one of Point, Bilinear, Trilinear");
                }
            }

            return errors;
        }

        private static JObject ExpectObject(JToken token, string fieldName, List<string> errors)
        {
            if (token == null)
            {
                return null;
            }

            JObject obj = token as JObject;
            if (obj == null)
            {
                errors.Add($"{fieldName} must be an object");
            }

            return obj;
        }

        private static void ResetSpriteAtlas(SpriteAtlas atlas)
        {
            UnityEngine.Object[] existingPackables = SpriteAtlasExtensions.GetPackables(atlas);
            if (existingPackables != null && existingPackables.Length > 0)
            {
                SpriteAtlasExtensions.Remove(atlas, existingPackables);
            }
        }

        private static void ApplySpriteAtlasSettings(SpriteAtlas atlas, JObject packingSettingsToken, JObject textureSettingsToken)
        {
            SpriteAtlasPackingSettings packingSettings = SpriteAtlasExtensions.GetPackingSettings(atlas);
            if (packingSettingsToken != null)
            {
                if (packingSettingsToken["padding"] != null)
                {
                    packingSettings.padding = packingSettingsToken["padding"].ToObject<int>();
                }
                if (packingSettingsToken["allowRotation"] != null)
                {
                    packingSettings.enableRotation = packingSettingsToken["allowRotation"].ToObject<bool>();
                }
                if (packingSettingsToken["tightPacking"] != null)
                {
                    packingSettings.enableTightPacking = packingSettingsToken["tightPacking"].ToObject<bool>();
                }
            }
            SpriteAtlasExtensions.SetPackingSettings(atlas, packingSettings);

            SpriteAtlasTextureSettings textureSettings = SpriteAtlasExtensions.GetTextureSettings(atlas);
            if (textureSettingsToken != null)
            {
                if (textureSettingsToken["filterMode"] != null)
                {
                    textureSettings.filterMode = ParseFilterMode(textureSettingsToken["filterMode"].ToString());
                }
                if (textureSettingsToken["generateMipMaps"] != null)
                {
                    textureSettings.generateMipMaps = textureSettingsToken["generateMipMaps"].ToObject<bool>();
                }
            }
            SpriteAtlasExtensions.SetTextureSettings(atlas, textureSettings);
        }

        private static UnityEngine.Object[] ResolveSpriteAtlasPackables(JArray packables)
        {
            if (packables == null || packables.Count == 0)
            {
                return Array.Empty<UnityEngine.Object>();
            }

            var resolved = new List<UnityEngine.Object>();
            foreach (JToken token in packables)
            {
                string packablePath = token.ToString();
                UnityEngine.Object packable = AssetDatabase.LoadMainAssetAtPath(packablePath);
                if (packable == null && AssetDatabase.IsValidFolder(packablePath))
                {
                    packable = AssetDatabase.LoadAssetAtPath<DefaultAsset>(packablePath);
                }
                if (packable != null)
                {
                    resolved.Add(packable);
                }
            }

            return resolved.ToArray();
        }

        private static bool IsValidSpriteAtlasPackable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/"))
            {
                return false;
            }

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return true;
            }

            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            return asset is Sprite || asset is Texture2D;
        }

        private static bool TryParseFilterMode(string filterMode, out FilterMode mode)
        {
            switch (filterMode)
            {
                case "Point":
                    mode = FilterMode.Point;
                    return true;
                case "Bilinear":
                    mode = FilterMode.Bilinear;
                    return true;
                case "Trilinear":
                    mode = FilterMode.Trilinear;
                    return true;
                default:
                    mode = FilterMode.Bilinear;
                    return false;
            }
        }

        private static FilterMode ParseFilterMode(string filterMode)
        {
            FilterMode mode;
            return TryParseFilterMode(filterMode, out mode) ? mode : FilterMode.Bilinear;
        }

        private static bool ApplyMaterialProperty(Material material, string propertyName, JToken value)
        {
            try
            {
                propertyName = NormalizeMaterialPropertyName(material, propertyName);
                // Check if property exists
                if (!material.HasProperty(propertyName))
                {
                    BridgeLogger.LogWarning("AssetManagementHandler", $"Material does not have property: {propertyName}");
                    return false;
                }

                // Get property type and apply value
                int propId = Shader.PropertyToID(propertyName);
                
                // Try to determine property type by value
                if (value.Type == JTokenType.Array)
                {
                    var array = value as JArray;
                    if (array != null)
                    {
                        if (array.Count == 4)
                        {
                            // Color property
                            material.SetColor(propId, new Color(
                                array[0].ToObject<float>(),
                                array[1].ToObject<float>(),
                                array[2].ToObject<float>(),
                                array[3].ToObject<float>()
                            ));
                            return true;
                        }
                        else if (array.Count == 3)
                        {
                            // Vector3 property
                            material.SetVector(propId, new Vector4(
                                array[0].ToObject<float>(),
                                array[1].ToObject<float>(),
                                array[2].ToObject<float>(),
                                0
                            ));
                            return true;
                        }
                        else if (array.Count == 2)
                        {
                            // Vector2 property
                            material.SetVector(propId, new Vector4(
                                array[0].ToObject<float>(),
                                array[1].ToObject<float>(),
                                0, 0
                            ));
                            return true;
                        }
                    }
                }
                else if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                {
                    // Float property
                    material.SetFloat(propId, value.ToObject<float>());
                    return true;
                }
                else if (value.Type == JTokenType.String)
                {
                    // Could be a texture reference
                    string texturePath = value.ToString();
                    if (texturePath.StartsWith("Assets/"))
                    {
                        Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
                        if (texture != null)
                        {
                            material.SetTexture(propId, texture);
                            return true;
                        }
                    }
                }
                else if (value.Type == JTokenType.Boolean)
                {
                    // Boolean as float (0 or 1)
                    material.SetFloat(propId, value.ToObject<bool>() ? 1f : 0f);
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                BridgeLogger.LogWarning("AssetManagementHandler", $"Failed to set material property {propertyName}: {e.Message}");
                return false;
            }
        }

        private static string NormalizeMaterialPropertyName(Material mat, string name)
        {
            if (string.IsNullOrEmpty(name) || mat == null || mat.shader == null) return name;
            if (string.Equals(name, "_Smoothness", StringComparison.Ordinal))
            {
                if (!mat.HasProperty("_Smoothness") && mat.HasProperty("_Glossiness")) return "_Glossiness";
            }
            else if (string.Equals(name, "_Glossiness", StringComparison.Ordinal))
            {
                if (!mat.HasProperty("_Glossiness") && mat.HasProperty("_Smoothness")) return "_Smoothness";
            }
            return name;
        }

        private static Vector3? ParseVector3(JToken token)
        {
            if (token == null) return null;
            
            try
            {
                float x = token["x"]?.ToObject<float>() ?? 0f;
                float y = token["y"]?.ToObject<float>() ?? 0f;
                float z = token["z"]?.ToObject<float>() ?? 0f;
                return new Vector3(x, y, z);
            }
            catch
            {
                return null;
            }
        }

        private static bool ApplyModificationToGameObject(GameObject target, string propertyName, JToken value)
        {
            try
            {
                switch (propertyName.ToLower())
                {
                    case "name":
                        target.name = value.ToString();
                        return true;

                    case "tag":
                        target.tag = value.ToString();
                        return true;

                    case "layer":
                        int layer = value.ToObject<int>();
                        if (layer >= 0 && layer < 32)
                        {
                            target.layer = layer;
                            return true;
                        }
                        break;

                    case "active":
                    case "isactive":
                        target.SetActive(value.ToObject<bool>());
                        return true;

                    case "transform":
                        var transformData = value as JObject;
                        if (transformData != null)
                        {
                            if (transformData["position"] != null)
                            {
                                var pos = ParseVector3(transformData["position"]);
                                if (pos.HasValue) target.transform.localPosition = pos.Value;
                            }
                            if (transformData["rotation"] != null)
                            {
                                var rot = ParseVector3(transformData["rotation"]);
                                if (rot.HasValue) target.transform.localRotation = Quaternion.Euler(rot.Value);
                            }
                            if (transformData["scale"] != null)
                            {
                                var scale = ParseVector3(transformData["scale"]);
                                if (scale.HasValue) target.transform.localScale = scale.Value;
                            }
                            return true;
                        }
                        break;

                    case "components":
                        var componentsData = value as JObject;
                        if (componentsData != null)
                        {
                            foreach (var comp in componentsData.Properties())
                            {
                                ApplyComponentModification(target, comp.Name, comp.Value as JObject);
                            }
                            return true;
                        }
                        break;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyComponentModification(GameObject target, string componentType, JObject properties)
        {
            if (properties == null) return;

            var component = target.GetComponent(componentType);
            if (component == null) return;

            var type = component.GetType();
            
            foreach (var prop in properties.Properties())
            {
                try
                {
                    var field = type.GetField(prop.Name);
                    if (field != null && field.IsPublic)
                    {
                        field.SetValue(component, prop.Value.ToObject(field.FieldType));
                        continue;
                    }

                    var property = type.GetProperty(prop.Name);
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(component, prop.Value.ToObject(property.PropertyType));
                    }
                }
                catch (Exception e)
                {
                    BridgeLogger.LogWarning("AssetManagementHandler", $"Failed to set {prop.Name} on {componentType}: {e.Message}");
                }
            }
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            Transform parent = obj.transform.parent;
            
            while (parent != null)
            {
                path = "/" + parent.name + path;
                parent = parent.parent;
            }
            
            return path;
        }

        #endregion

        /// <summary>
        /// Opens a prefab in prefab mode for editing
        /// </summary>
        public static object OpenPrefab(JObject parameters)
        {
            try
            {
                // Parse parameters
                string prefabPath = parameters["prefabPath"]?.ToString();
                string focusObject = parameters["focusObject"]?.ToString();
                bool isolateObject = parameters["isolateObject"]?.ToObject<bool>() ?? false;

                // Validate prefab path
                if (string.IsNullOrEmpty(prefabPath))
                {
                    return new { error = "prefabPath is required" };
                }

                // Load the prefab asset
                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset == null)
                {
                    return new { error = $"Prefab asset not found at path: {prefabPath}" };
                }

                // Check if asset is actually a prefab
                if (!PrefabUtility.IsPartOfPrefabAsset(prefabAsset))
                {
                    return new { error = $"Asset at path is not a prefab: {prefabPath}" };
                }

                // Check if already in prefab mode with this prefab
                var currentStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                bool wasAlreadyOpen = false;
                
                if (currentStage != null && currentStage.assetPath == prefabPath)
                {
                    wasAlreadyOpen = true;
                }
                else
                {
                    // Open the prefab in prefab mode
                    AssetDatabase.OpenAsset(prefabAsset);
                    
                    // Wait for prefab stage to be ready
                    currentStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                }

                if (currentStage == null)
                {
                    return new { error = "Failed to enter prefab mode" };
                }

                GameObject prefabRoot = currentStage.prefabContentsRoot;
                string focusedObjectPath = null;

                // Focus on specific object if requested
                if (!string.IsNullOrEmpty(focusObject) && prefabRoot != null)
                {
                    Transform focusTransform = prefabRoot.transform.Find(focusObject.TrimStart('/'));
                    if (focusTransform != null)
                    {
                        Selection.activeGameObject = focusTransform.gameObject;
                        EditorGUIUtility.PingObject(focusTransform.gameObject);
                        focusedObjectPath = GetGameObjectPath(focusTransform.gameObject);

                        if (isolateObject)
                        {
                            // Use scene visibility to isolate object
                            UnityEditor.SceneVisibilityManager.instance.Isolate(focusTransform.gameObject, true);
                        }
                    }
                }

                return new
                {
                    success = true,
                    prefabPath = prefabPath,
                    isInPrefabMode = true,
                    prefabContentsRoot = GetGameObjectPath(prefabRoot),
                    focusedObject = focusedObjectPath,
                    wasAlreadyOpen = wasAlreadyOpen,
                    message = wasAlreadyOpen ? "Already editing this prefab" : "Prefab opened in prefab mode"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in OpenPrefab: {e.Message}");
                return new { error = $"Failed to open prefab: {e.Message}" };
            }
        }

        /// <summary>
        /// Exits prefab mode and optionally saves changes
        /// </summary>
        public static object ExitPrefabMode(JObject parameters)
        {
            try
            {
                // Parse parameters
                bool saveChanges = parameters["saveChanges"]?.ToObject<bool>() ?? true;

                // Check if in prefab mode
                var currentStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (currentStage == null)
                {
                    return new
                    {
                        success = true,
                        wasInPrefabMode = false,
                        message = "Not currently in prefab mode"
                    };
                }

                string prefabPath = currentStage.assetPath;
                bool changesSaved = false;

                // Save changes if requested
                if (saveChanges && currentStage.scene.isDirty)
                {
                    try
                    {
                        PrefabUtility.SaveAsPrefabAsset(currentStage.prefabContentsRoot, prefabPath);
                        changesSaved = true;
                    }
                    catch (Exception saveEx)
                    {
                        return new { error = $"Failed to save prefab changes: {saveEx.Message}" };
                    }
                }

                // Exit prefab mode
                UnityEditor.SceneManagement.StageUtility.GoBackToPreviousStage();

                return new
                {
                    success = true,
                    wasInPrefabMode = true,
                    changesSaved = changesSaved,
                    prefabPath = prefabPath,
                    message = changesSaved ? "Exited prefab mode and saved changes" : "Exited prefab mode without saving changes"
                };
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in ExitPrefabMode: {e.Message}");
                return new { error = $"Failed to exit prefab mode: {e.Message}" };
            }
        }

        /// <summary>
        /// Saves current prefab changes or applies overrides from a prefab instance
        /// </summary>
        public static object SavePrefab(JObject parameters)
        {
            try
            {
                // Parse parameters
                string gameObjectPath = parameters["gameObjectPath"]?.ToString();
                bool includeChildren = parameters["includeChildren"]?.ToObject<bool>() ?? true;

                // Check if in prefab mode
                var currentStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                
                if (currentStage != null && string.IsNullOrEmpty(gameObjectPath))
                {
                    // Save current prefab in prefab mode
                    string prefabPath = currentStage.assetPath;
                    
                    if (currentStage.scene.isDirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(currentStage.prefabContentsRoot, prefabPath);
                        
                        return new
                        {
                            success = true,
                            savedInPrefabMode = true,
                            prefabPath = prefabPath,
                            message = "Prefab changes saved successfully"
                        };
                    }
                    else
                    {
                        return new
                        {
                            success = true,
                            savedInPrefabMode = true,
                            prefabPath = prefabPath,
                            message = "No changes to save"
                        };
                    }
                }
                else if (!string.IsNullOrEmpty(gameObjectPath))
                {
                    // Save prefab instance overrides
                    GameObject gameObject = GameObject.Find(gameObjectPath);
                    if (gameObject == null)
                    {
                        return new { error = $"GameObject not found at path: {gameObjectPath}" };
                    }

                    // Check if it's a prefab instance
                    if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
                    {
                        return new { error = $"GameObject is not a prefab instance: {gameObjectPath}" };
                    }

                    // Get prefab path
                    string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                    
                    // Count overrides before applying
                    var overrides = PrefabUtility.GetObjectOverrides(gameObject, includeChildren);
                    int overrideCount = overrides.Count;

                    // Apply overrides
                    if (includeChildren)
                    {
                        PrefabUtility.ApplyPrefabInstance(gameObject, InteractionMode.UserAction);
                    }
                    else
                    {
                        // Apply only root object overrides
                        PrefabUtility.ApplyObjectOverride(gameObject, AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(gameObject)), InteractionMode.UserAction);
                    }

                    return new
                    {
                        success = true,
                        gameObjectPath = gameObjectPath,
                        prefabPath = prefabPath,
                        overridesApplied = overrideCount,
                        includedChildren = includeChildren,
                        message = $"Applied {overrideCount} overrides to prefab"
                    };
                }
                else
                {
                    return new { error = "Not currently in prefab mode and no gameObjectPath specified" };
                }
            }
            catch (Exception e)
            {
                BridgeLogger.LogError("AssetManagementHandler", $"Error in SavePrefab: {e.Message}");
                return new { error = $"Failed to save prefab: {e.Message}" };
            }
        }
    }
}
