using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Ox1;
using Trash;
using PlayerInput = Trash.PlayerInput;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace TrashDayCouch;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    static Harmony _harmony;
    static PlayerController _player2;
    static PlayerInput _player2Input;
    static GameObject _camera2Root;
    static bool _drivingPlayer2;
    static bool _split;
    static bool _restrictedDevices;
    static Vector2 _player2Look;
    static bool _jumpHeld;
    static bool _sprintHeld;
    static bool _attackHeld;
    static bool _interactHeld;
    static bool _biteHeld;
    static bool _throwHeld;
    static readonly List<Camera> _mainCameras = new List<Camera>();
    static readonly List<Rect> _mainRects = new List<Rect>();
    static readonly List<RectTransform> _hudPaddings = new List<RectTransform>();
    static readonly List<Vector2> _hudAnchorMin = new List<Vector2>();
    static readonly List<Vector2> _hudAnchorMax = new List<Vector2>();
    static readonly List<Vector2> _hudSize = new List<Vector2>();
    static readonly List<Vector2> _hudPosition = new List<Vector2>();
    static readonly Dictionary<string, Sprite> _keySprites = new Dictionary<string, Sprite>();
    static bool _suppressLocalCamera;
    static bool _promptsForGamepad;

    private void Awake()
    {
        Logger = base.Logger;
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.Patch(AccessTools.Method(typeof(PlayerInput), "Update"), prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipPlayer2Input)));
        _harmony.Patch(AccessTools.Method(typeof(PlayerCamera), "LateUpdate"), prefix: new HarmonyMethod(typeof(Plugin), nameof(BeginPlayer2Camera)), postfix: new HarmonyMethod(typeof(Plugin), nameof(EndPlayer2Camera)));
        _harmony.Patch(AccessTools.Method(typeof(PlayerInput), nameof(PlayerInput.ReadLookInput), new[] { typeof(float) }), prefix: new HarmonyMethod(typeof(Plugin), nameof(ReadPlayer2Look)));
        _harmony.Patch(AccessTools.Method(typeof(LocalCamera), nameof(LocalCamera.SetTransform)), prefix: new HarmonyMethod(typeof(Plugin), nameof(RedirectPlayer2Camera)));
        _harmony.Patch(AccessTools.Method(typeof(LocalPlayerFocusToggle), nameof(LocalPlayerFocusToggle.SetState)), prefix: new HarmonyMethod(typeof(Plugin), nameof(ShowPlayer2Controls)));
        _harmony.Patch(AccessTools.Method(typeof(LocalCamera), "OnEnable"), prefix: new HarmonyMethod(typeof(Plugin), nameof(AllowLocalCameraEnable)));
        _harmony.Patch(AccessTools.Method(typeof(LocalCamera), "OnDisable"), prefix: new HarmonyMethod(typeof(Plugin), nameof(AllowLocalCameraDisable)));
        PatchPromptRefresh("InputIcons.II_ImagePrompt", "UpdateDisplayedImages");
        PatchPromptRefresh("InputIcons.II_ImagePromptBehaviour", "UpdateDisplayedImages");
        PatchPromptRefresh("InputIcons.II_SpritePrompt", "UpdateDisplayedSprites");
        PatchPromptRefresh("InputIcons.II_TextPrompt", "UpdateDisplayedSprites");
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded. Press F7 to toggle player 2.");
    }

    private void OnDestroy()
    {
        TurnOff();
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        if (_split && _player2 == null)
            TurnOff();

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f7Key.wasPressedThisFrame)
        {
            if (_player2 != null)
                TurnOff();
            else
                TurnOn();
        }

        if (_player2 != null)
        {
            DrivePlayer2();
            var gamepadConnected = Gamepad.all.Count > 0;
            if (gamepadConnected != _promptsForGamepad)
                ApplyPromptDevices();
        }
    }

    private void LateUpdate()
    {
        if (_split)
        {
            ApplySplitRects();
            FitHud(FindPrimaryPlayer(), 0f, 0.5f);
            if (_player2 != null)
                FitHud(_player2, 0.5f, 1f);
        }
    }

    private void OnGUI()
    {
        if (_player2 == null)
            return;

        var line = new Rect(Screen.width * 0.5f - 2f, 0f, 4f, Screen.height);
        var previous = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(line, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    static void TurnOn()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Logger.LogWarning("Player 2 can only be added by the host.");
            return;
        }

        var local = FindPrimaryPlayer();
        if (local == null)
        {
            Logger.LogWarning("Start a game before toggling player 2.");
            return;
        }

        var prefab = FindSpawnPrefab(local);
        if (prefab == null)
        {
            Logger.LogWarning("Couldn't find a player prefab to spawn.");
            return;
        }

        var position = local.transform.position + local.transform.right * 1.5f + Vector3.up * 0.2f;
        var spawned = Util.InstantiateAndSpawnPrefab(prefab, position, local.transform.rotation, null, local.OwnerClientId);
        if (spawned == null || !spawned.TryGetComponent<NetworkObject>(out var networkObject) || !networkObject.IsSpawned)
        {
            Logger.LogWarning("Player 2 failed to spawn.");
            if (spawned != null)
                Destroy(spawned);
            return;
        }

        _player2 = spawned.GetComponent<PlayerController>();
        _player2Input = spawned.GetComponentInChildren<PlayerInput>(true);
        foreach (var toggle in spawned.GetComponentsInChildren<LocalPlayerFocusToggle>(true))
            toggle.SetState(false, false, false);

        var playerCamera = spawned.GetComponentInChildren<PlayerCamera>(true);
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(true);
            playerCamera.Reset();
        }

        if (_player2Input != null)
            _player2Input.gameObject.SetActive(true);

        AimDetector(local);
        AimDetector(_player2);
        FitHud(local, 0f, 0.5f);
        FitHud(_player2, 0.5f, 1f);
        ApplyPromptDevices();
        CreateSplitCamera();
        RestrictPlayer1ToKeyboardMouse();
        ResetHeldButtons();
        _split = true;
        Logger.LogInfo($"Player 2 on ({prefab.name}). {ControlHint()}");
    }

    static void TurnOff()
    {
        if (_player2 != null)
        {
            ReleasePlayer2();
            if (_player2.TryGetComponent<NetworkObject>(out var networkObject) && networkObject.IsSpawned)
                networkObject.Despawn(true);
            else
                Destroy(_player2.gameObject);
        }

        _player2 = null;
        _player2Input = null;
        RestoreHuds();
        RestoreCameras();
        RestoreInputDevices();
        ResetHeldButtons();
        if (_split)
            Logger.LogInfo("Player 2 off.");
        _split = false;
    }

    static void DrivePlayer2()
    {
        var controller = _player2;
        if (controller == null)
            return;

        if (Time.deltaTime == 0f || PlayerInput.IsSuppressed)
        {
            controller.SetLocalMoveInput(Vector2.zero);
            _player2Look = Vector2.zero;
            ReleasePlayer2();
            return;
        }

        var gamepad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
        var keyboard = Keyboard.current;
        var move = Vector2.zero;
        var look = Vector2.zero;
        var jump = false;
        var sprint = false;
        var attack = false;
        var interact = false;
        var bite = false;
        var throwItem = false;

        if (gamepad != null)
        {
            move.x = ReadGamepad(gamepad, "MoveRight") - ReadGamepad(gamepad, "MoveLeft");
            move.y = ReadGamepad(gamepad, "MoveForward") - ReadGamepad(gamepad, "MoveBack");
            if (move.sqrMagnitude < 0.04f)
                move = gamepad.leftStick.ReadValue();
            var lookX = ReadGamepad(gamepad, "LookRight") - ReadGamepad(gamepad, "LookLeft");
            var lookY = ReadGamepad(gamepad, "LookUp") - ReadGamepad(gamepad, "LookDown");
            var stick = new Vector2(lookX, lookY);
            if (stick.sqrMagnitude < 0.04f)
                stick = gamepad.rightStick.ReadValue();
            if (stick.sqrMagnitude > 0.04f)
                look = stick * Time.deltaTime * 160f;
            jump = ReadGamepad(gamepad, "Jump") > 0.5f;
            sprint = ReadGamepad(gamepad, "Sprint") > 0.5f;
            attack = ReadGamepad(gamepad, "Attack") > 0.5f;
            interact = ReadGamepad(gamepad, "Interact") > 0.5f;
            bite = ReadGamepad(gamepad, "Bite") > 0.5f;
            throwItem = ReadGamepad(gamepad, "Throw") > 0.5f;
        }
        else if (keyboard != null)
        {
            if (keyboard.jKey.isPressed) move.x -= 1f;
            if (keyboard.lKey.isPressed) move.x += 1f;
            if (keyboard.kKey.isPressed) move.y -= 1f;
            if (keyboard.iKey.isPressed) move.y += 1f;
            var yaw = 0f;
            var pitch = 0f;
            if (keyboard.uKey.isPressed) yaw -= 1f;
            if (keyboard.oKey.isPressed) yaw += 1f;
            if (keyboard.hKey.isPressed) pitch -= 1f;
            if (keyboard.yKey.isPressed) pitch += 1f;
            look = new Vector2(yaw, pitch) * Time.deltaTime * 160f;
            jump = keyboard.nKey.isPressed;
            sprint = keyboard.rightShiftKey.isPressed;
            attack = keyboard.mKey.isPressed;
            interact = keyboard.commaKey.isPressed;
            bite = keyboard.bKey.isPressed;
            throwItem = keyboard.periodKey.isPressed;
        }

        if (move.sqrMagnitude < 0.04f)
            move = Vector2.zero;
        else if (move.sqrMagnitude > 1f)
            move.Normalize();

        if (Options.Instance != null)
        {
            look *= Options.Instance.MouseSensitivity;
            if (Options.Instance.InvertLook == Options.Toggle.On)
                look.y *= -1f;
        }

        _player2Look = look;
        controller.SetLocalMoveInput(move);
        if (_camera2Root != null)
            controller.SetCameraRotation(_camera2Root.transform.rotation);

        Edge(_jumpHeld, jump, down =>
        {
            if (down) controller.JumpPressed();
            else controller.JumpReleased();
        });
        Edge(_sprintHeld, sprint, down => controller.SetSprintPressed(down));
        Edge(_attackHeld, attack, down =>
        {
            if (down) controller.SetAttackPressed();
            else controller.SetAttackReleased();
        });
        Edge(_interactHeld, interact, down =>
        {
            if (_player2Input == null || _player2Input.InteractionDetector == null)
                return;
            if (down) _player2Input.InteractionDetector.SetInteractionPressed();
            else _player2Input.InteractionDetector.SetInteractionReleased();
        });
        Edge(_biteHeld, bite, down =>
        {
            if (controller.Bite == null)
                return;
            if (down) controller.Bite.SetBitePressed();
            else controller.Bite.SetBiteReleased();
        });
        Edge(_throwHeld, throwItem, down =>
        {
            if (_player2Input == null || _player2Input.Inventory == null)
                return;
            if (down)
            {
                if (_player2Input.Inventory.CanThrow())
                    _player2Input.Inventory.SetThrowPressed();
                else if (_player2Input.Slots != null && _player2Input.Slots.HasAnyCosmeticsAttached())
                    _player2Input.Slots.SetDropPressed();
            }
            else
            {
                _player2Input.Inventory.SetThrowReleased();
                if (_player2Input.Slots != null)
                    _player2Input.Slots.SetDropReleased();
            }
        });

        _jumpHeld = jump;
        _sprintHeld = sprint;
        _attackHeld = attack;
        _interactHeld = interact;
        _biteHeld = bite;
        _throwHeld = throwItem;
    }

    static void Edge(bool wasHeld, bool held, System.Action<bool> apply)
    {
        if (wasHeld == held)
            return;
        apply(held);
    }

    static void ReleasePlayer2()
    {
        if (_player2 == null)
            return;
        if (_jumpHeld) _player2.JumpReleased();
        if (_sprintHeld) _player2.SetSprintPressed(false);
        if (_attackHeld) _player2.SetAttackReleased();
        if (_biteHeld && _player2.Bite != null) _player2.Bite.SetBiteReleased();
        if (_player2Input != null)
        {
            if (_interactHeld && _player2Input.InteractionDetector != null)
                _player2Input.InteractionDetector.SetInteractionReleased();
            if (_throwHeld && _player2Input.Inventory != null)
            {
                _player2Input.Inventory.SetThrowReleased();
                if (_player2Input.Slots != null)
                    _player2Input.Slots.SetDropReleased();
            }
        }
        ResetHeldButtons();
    }

    static void ResetHeldButtons()
    {
        _jumpHeld = false;
        _sprintHeld = false;
        _attackHeld = false;
        _interactHeld = false;
        _biteHeld = false;
        _throwHeld = false;
    }

    static PlayerController FindPrimaryPlayer()
    {
        foreach (var player in PlayerController.All)
        {
            if (player != null && player != _player2 && player.IsOwner && player.IsSpawned)
                return player;
        }
        return null;
    }

    static GameObject FindSpawnPrefab(PlayerController local)
    {
        var prefabs = new List<GameObject>();
        var registered = NetworkManager.Singleton.NetworkConfig != null ? NetworkManager.Singleton.NetworkConfig.Prefabs : null;
        if (registered != null)
        {
            foreach (var entry in registered.Prefabs)
            {
                var prefab = entry.Override == NetworkPrefabOverride.None ? entry.Prefab : entry.OverridingTargetPrefab;
                if (prefab != null && prefab.GetComponent<PlayerController>() != null)
                    prefabs.Add(prefab);
            }
        }

        if (prefabs.Count == 0)
            return null;

        var wantedHash = PrefabHash(local.GetComponent<NetworkObject>());
        var wantedName = local.name.Replace("(Clone)", "").Trim();
        var match = 0;
        for (var i = 0; i < prefabs.Count; i++)
        {
            var networkObject = prefabs[i].GetComponent<NetworkObject>();
            if ((wantedHash != 0 && networkObject.PrefabIdHash == wantedHash) || prefabs[i].name == wantedName)
            {
                match = i;
                break;
            }
        }

        return prefabs[(match + 1) % prefabs.Count];
    }

    static uint PrefabHash(NetworkObject networkObject)
    {
        if (networkObject == null)
            return 0;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var field = networkObject.GetType().GetField("PrefabGlobalObjectIdHash", flags);
        if (field != null && field.GetValue(networkObject) is uint prefabHash && prefabHash != 0)
            return prefabHash;
        return networkObject.PrefabIdHash;
    }

    static void CreateSplitCamera()
    {
        if (!LocalCamera.Instance.TryGet(out var localCamera))
        {
            Logger.LogWarning("Player 2 spawned, but there is no local camera to split.");
            return;
        }

        _suppressLocalCamera = true;
        _camera2Root = Instantiate(localCamera.gameObject);
        _camera2Root.name = "CouchPlayer2Camera";
        _suppressLocalCamera = false;

        foreach (var listener in _camera2Root.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;
        foreach (var extra in _camera2Root.GetComponentsInChildren<LocalCamera>(true))
            extra.enabled = false;
        foreach (var camera in _camera2Root.GetComponentsInChildren<Camera>(true))
            camera.tag = "Untagged";

        _mainCameras.Clear();
        _mainRects.Clear();
        foreach (var camera in localCamera.GetComponentsInChildren<Camera>(true))
        {
            _mainCameras.Add(camera);
            _mainRects.Add(camera.rect);
        }
        ApplySplitRects();
    }

    static void ApplySplitRects()
    {
        var left = new Rect(0f, 0f, 0.5f, 1f);
        var right = new Rect(0.5f, 0f, 0.5f, 1f);
        foreach (var camera in _mainCameras)
        {
            if (camera != null)
                camera.rect = left;
        }
        if (_camera2Root == null)
            return;
        foreach (var camera in _camera2Root.GetComponentsInChildren<Camera>(true))
            camera.rect = right;
    }

    static void RestoreCameras()
    {
        for (var i = 0; i < _mainCameras.Count; i++)
        {
            if (_mainCameras[i] != null)
                _mainCameras[i].rect = _mainRects[i];
        }
        _mainCameras.Clear();
        _mainRects.Clear();
        if (_camera2Root != null)
            Destroy(_camera2Root);
        _camera2Root = null;
    }

    static void RestrictPlayer1ToKeyboardMouse()
    {
        if (InputSystem.actions == null || Keyboard.current == null || Mouse.current == null)
            return;
        var devices = new ReadOnlyArray<InputDevice>(new InputDevice[] { Keyboard.current, Mouse.current });
        foreach (var map in InputSystem.actions.actionMaps)
            map.devices = devices;
        _restrictedDevices = true;
    }

    static void RestoreInputDevices()
    {
        if (!_restrictedDevices || InputSystem.actions == null)
            return;
        foreach (var map in InputSystem.actions.actionMaps)
            map.devices = null;
        _restrictedDevices = false;
    }

    static string ControlHint()
    {
        if (Gamepad.all.Count > 0)
            return "Gamepad: stick move/look, A jump, LB sprint, RT attack, X interact, Y throw, LT bite";
        return "Keys: IJKL move, UO/YH look, N jump, RShift sprint, M attack, , interact, . throw, B bite";
    }

    static bool IsPlayer2(Component component)
    {
        return _player2 != null && component != null && component.GetComponentInParent<PlayerController>() == _player2;
    }

    static bool SkipPlayer2Input(PlayerInput __instance)
    {
        return !IsPlayer2(__instance);
    }

    static void BeginPlayer2Camera(PlayerCamera __instance)
    {
        _drivingPlayer2 = IsPlayer2(__instance);
    }

    static void EndPlayer2Camera()
    {
        _drivingPlayer2 = false;
    }

    static bool ReadPlayer2Look(ref Vector2 __result)
    {
        if (!_drivingPlayer2)
            return true;
        __result = _player2Look;
        return false;
    }

    static bool RedirectPlayer2Camera(Vector3 position, Quaternion rotation, float fov)
    {
        if (!_drivingPlayer2 || _camera2Root == null)
            return true;
        _camera2Root.transform.SetPositionAndRotation(position, rotation);
        foreach (var camera in _camera2Root.GetComponentsInChildren<Camera>(true))
            camera.fieldOfView = fov;
        return false;
    }

    static void ShowPlayer2Controls(LocalPlayerFocusToggle __instance, ref bool controlled, ref bool focused)
    {
        if (!IsPlayer2(__instance))
            return;
        if (__instance.GetComponent<PlayerInput>() != null)
            controlled = true;
        if (__instance.GetComponent<PlayerCamera>() != null || __instance.name == "PlayerHud")
            focused = true;
    }

    static float ReadGamepad(Gamepad pad, string actionName)
    {
        var action = InputSystem.actions != null ? InputSystem.actions.FindAction(actionName, false) : null;
        if (action == null)
            return 0f;
        var best = 0f;
        for (var i = 0; i < action.bindings.Count; i++)
        {
            var binding = action.bindings[i];
            if (binding.isComposite)
                continue;
            var path = string.IsNullOrEmpty(binding.effectivePath) ? binding.path : binding.effectivePath;
            if (string.IsNullOrEmpty(path) || path.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var control = InputControlPath.TryFindControl(pad, path);
            if (control != null)
                best = Mathf.Max(best, ReadControl(control));
        }
        return best;
    }

    static float ReadControl(InputControl control)
    {
        if (control is AxisControl axis)
            return axis.ReadValue();
        if (control is ButtonControl button)
            return button.ReadValue();
        if (control is Vector2Control vector)
            return vector.ReadValue().magnitude;
        return 0f;
    }

    static void AimDetector(PlayerController player)
    {
        var detector = player.GetComponentInChildren<InteractionDetector>(true);
        var playerCamera = player.GetComponentInChildren<PlayerCamera>(true);
        if (detector == null || playerCamera == null)
            return;
        if (detector.CameraTransform == null || !detector.CameraTransform.IsChildOf(player.transform))
            detector.CameraTransform = playerCamera.transform;
    }

    static void FitHud(PlayerController player, float minX, float maxX)
    {
        if (player == null)
            return;
        var padding = player.transform.Find("PlayerHud/Screen Padding") as RectTransform;
        if (padding == null)
            return;
        if (!_hudPaddings.Contains(padding))
        {
            _hudPaddings.Add(padding);
            _hudAnchorMin.Add(padding.anchorMin);
            _hudAnchorMax.Add(padding.anchorMax);
            _hudSize.Add(padding.sizeDelta);
            _hudPosition.Add(padding.anchoredPosition);
        }
        padding.anchorMin = new Vector2(minX, 0f);
        padding.anchorMax = new Vector2(maxX, 1f);
        padding.offsetMin = new Vector2(25f, 25f);
        padding.offsetMax = new Vector2(-25f, -25f);
    }

    static void RestoreHuds()
    {
        for (var i = 0; i < _hudPaddings.Count; i++)
        {
            var padding = _hudPaddings[i];
            if (padding == null)
                continue;
            padding.anchorMin = _hudAnchorMin[i];
            padding.anchorMax = _hudAnchorMax[i];
            padding.sizeDelta = _hudSize[i];
            padding.anchoredPosition = _hudPosition[i];
        }
        _hudPaddings.Clear();
        _hudAnchorMin.Clear();
        _hudAnchorMax.Clear();
        _hudSize.Clear();
        _hudPosition.Clear();
        _promptsForGamepad = false;
    }

    static void ApplyPromptDevices()
    {
        _promptsForGamepad = Gamepad.all.Count > 0;
        var primary = FindPrimaryPlayer();
        if (primary != null)
            SetPromptDevice(primary.gameObject, "KeyboardAndMouse");
        if (_player2 != null)
            SetPromptDevice(_player2.gameObject, _promptsForGamepad ? "Gamepad" : "KeyboardAndMouse");
    }

    static void SetPromptDevice(GameObject root, string deviceType)
    {
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Namespace != "InputIcons")
                continue;
            var changed = false;
            foreach (var field in behaviour.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.GetValue(behaviour) is not IList list)
                    continue;
                foreach (var item in list)
                {
                    var deviceField = item?.GetType().GetField("deviceType");
                    if (deviceField == null)
                        continue;
                    deviceField.SetValue(item, Enum.Parse(deviceField.FieldType, deviceType));
                    changed = true;
                }
            }
            if (!changed)
                continue;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var update = behaviour.GetType().GetMethod("UpdateDisplayedImages", flags, null, Type.EmptyTypes, null)
                ?? behaviour.GetType().GetMethod("UpdateDisplayedSprites", flags, null, Type.EmptyTypes, null);
            update?.Invoke(behaviour, null);
        }
    }

    static void PatchPromptRefresh(string typeName, string methodName)
    {
        var type = AccessTools.TypeByName(typeName);
        var method = type == null ? null : AccessTools.Method(type, methodName, new[] { typeof(bool) }) ?? AccessTools.Method(type, methodName);
        if (method != null)
            _harmony.Patch(method, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterPromptRefresh)));
    }

    static void AfterPromptRefresh(Component __instance)
    {
        if (_player2 == null || Gamepad.all.Count > 0 || __instance == null)
            return;
        if (__instance.GetComponentInParent<PlayerController>() != _player2)
            return;
        OverrideKeyboardPrompt(__instance);
    }

    static void OverrideKeyboardPrompt(Component prompt)
    {
        var list = prompt.GetType().GetField("spritePromptDatas")?.GetValue(prompt) as IList;
        if (list == null)
            return;
        foreach (var item in list)
        {
            if (item == null)
                continue;
            var actionReference = item.GetType().GetField("actionReference")?.GetValue(item);
            var action = actionReference?.GetType().GetProperty("action")?.GetValue(actionReference) as InputAction;
            var bindingType = item.GetType().GetField("bindingType")?.GetValue(item);
            var key = KeyForAction(action != null ? action.name : null, bindingType != null ? bindingType.ToString() : null);
            var sprite = key != null ? KeySprite(key) : null;
            var image = item.GetType().GetField("image")?.GetValue(item);
            if (sprite != null && image != null)
                image.GetType().GetProperty("sprite")?.SetValue(image, sprite);
        }
    }

    static string KeyForAction(string action, string bindingType)
    {
        if (action == "MoveForward")
            return "I";
        if (action == "MoveBack")
            return "K";
        if (action == "MoveLeft")
            return "J";
        if (action == "MoveRight")
            return "L";
        if (action == "Move")
        {
            if (bindingType == "Up" || bindingType == "Forward")
                return "I";
            if (bindingType == "Down" || bindingType == "Backward")
                return "K";
            if (bindingType == "Left")
                return "J";
            if (bindingType == "Right")
                return "L";
            return null;
        }
        if (action == "LookUp")
            return "Y";
        if (action == "LookDown")
            return "H";
        if (action == "LookLeft")
            return "U";
        if (action == "LookRight")
            return "O";
        if (action == "Jump")
            return "N";
        if (action == "Sprint")
            return "RightShift";
        if (action == "Attack")
            return "M";
        if (action == "Interact")
            return "Comma";
        if (action == "Throw")
            return "Period";
        if (action == "Bite")
            return "B";
        return null;
    }

    static Sprite KeySprite(string key)
    {
        if (_keySprites.TryGetValue(key, out var cached))
            return cached;
        var prefix = (key + "_Key").ToUpperInvariant();
        Sprite found = null;
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite.name.ToUpperInvariant().StartsWith(prefix))
            {
                found = sprite;
                break;
            }
        }
        _keySprites[key] = found;
        return found;
    }

    static bool AllowLocalCameraEnable(LocalCamera __instance)
    {
        return !_suppressLocalCamera && !__instance.name.Contains("CouchPlayer2Camera");
    }

    static bool AllowLocalCameraDisable(LocalCamera __instance)
    {
        return !_suppressLocalCamera && !__instance.name.Contains("CouchPlayer2Camera");
    }
}
