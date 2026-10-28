using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using FluffyGroomingTool;
using Ox1;
using Trash;
using PlayerInput = Trash.PlayerInput;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

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
    static int _promptPadCount = -1;
    static GUIStyle _playerNameStyle;
    static bool _throwCameraSwapped;
    static Vector3 _throwCameraPosition;
    static Quaternion _throwCameraRotation;
    static PlayerController _customizePlayer;
    static bool _pickCustomizePlayer;
    static bool _replacingPlayer2;
    static bool _blockCouchFocusSteal;
    static List<PlayerController> _playersBeforeRespawn;
    static readonly List<InputAction> _silencedMenuActions = new List<InputAction>();
    static readonly Dictionary<VisualElement, PickingMode> _menuPicking = new Dictionary<VisualElement, PickingMode>();
    static readonly List<Canvas> _nameplateCanvases = new List<Canvas>();
    static readonly Dictionary<FurRenderer, Camera> _savedFurCameras = new Dictionary<FurRenderer, Camera>();
    static readonly List<SkinnedMeshRenderer> _openedFurSkins = new List<SkinnedMeshRenderer>();
    static float _menuNavTime;
    static bool _hidCursorForPlayer2;
    static bool _player2Spectating;
    static PlayerController _player2SpectateTarget;
    static float _player2DeathTime = -1f;
    static bool _spectateSource;
    static bool _spectateSourceKeepsView;
    static bool _wantPlayer2;
    static int _splitCameraSourceId;
    static bool _applyLookPending;
    static int _lookSettleFrames;
    static float _nextPlayer2SpawnTime;
    static GameObject _savedBodyPrefab;
    static GameObject _savedHatPrefab;
    static GameObject _savedGlassesPrefab;
    static Vector3 _savedHatOffset;
    static Vector3 _savedGlassesOffset;
    static Quaternion _savedHatRotation = Quaternion.identity;
    static Quaternion _savedGlassesRotation = Quaternion.identity;
    static Color _savedAccent;
    static bool _hasSavedAccent;
    static GameObject _savedPrimaryBody;
    static int _savedPrimaryIndex = -1;
    static Color _savedPrimaryAccent;
    static bool _hasSavedPrimaryAccent;
    static GameObject _savedPrimaryHat;
    static GameObject _savedPrimaryGlasses;
    static Vector3 _savedPrimaryHatOffset;
    static Vector3 _savedPrimaryGlassesOffset;
    static Quaternion _savedPrimaryHatRotation = Quaternion.identity;
    static Quaternion _savedPrimaryGlassesRotation = Quaternion.identity;
    static int _primaryLookId;
    static int _primaryAcceptedId;
    static int _primarySettleFrames;
    static bool _applyPrimaryLook;
    static bool _acceptPrimaryRespawn;
    static bool _keepPrimaryBody;
        
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
        _harmony.Patch(AccessTools.Method(typeof(PlayerController), nameof(PlayerController.GetLocalCameraRaycast)), prefix: new HarmonyMethod(typeof(Plugin), nameof(Player2CameraRay)));
        _harmony.Patch(AccessTools.Method(typeof(Inventory), nameof(Inventory.Throw)), prefix: new HarmonyMethod(typeof(Plugin), nameof(Player2ThrowStart)), postfix: new HarmonyMethod(typeof(Plugin), nameof(Player2ThrowEnd)));
        var highlight = AccessTools.Method(typeof(InteractionHighlight), "IsLocalPlayerFocused");
        if (highlight != null)
            _harmony.Patch(highlight, prefix: new HarmonyMethod(typeof(Plugin), nameof(HighlightForCouchPlayer)));
        _harmony.Patch(AccessTools.Method(typeof(CustomizeInteraction), nameof(CustomizeInteraction.SetPredictedInteracting)), prefix: new HarmonyMethod(typeof(Plugin), nameof(RememberCustomizePlayer)), postfix: new HarmonyMethod(typeof(Plugin), nameof(UpdateCustomizeMenuControl)));
        _harmony.Patch(AccessTools.Method(typeof(MenuSceneManager), "ShouldShowCursor"), prefix: new HarmonyMethod(typeof(Plugin), nameof(HideCursorForPlayer2Menu)));
        _harmony.Patch(AccessTools.Method(typeof(MenuSceneManager), "UpdateShowCursor"), postfix: new HarmonyMethod(typeof(Plugin), nameof(ApplyMenuCursor)));
        _harmony.Patch(AccessTools.Method(typeof(PlayerNameplateUI), "PreRenderUpdate"), postfix: new HarmonyMethod(typeof(Plugin), nameof(PlaceNameplateForCamera)));
        _harmony.Patch(AccessTools.Method(typeof(InputNavigationManager), "Update"), prefix: new HarmonyMethod(typeof(Plugin), nameof(KeepCustomizeOnButtons)));
        _harmony.Patch(AccessTools.Method(typeof(PlayerLoadout), nameof(PlayerLoadout.SetLocalLoadout)), prefix: new HarmonyMethod(typeof(Plugin), nameof(ProtectPrimaryLoadout)), postfix: new HarmonyMethod(typeof(Plugin), nameof(ClearCustomizePlayer)));
        var gameOver = AccessTools.Method(typeof(GameOver), nameof(GameOver.SetGameOver));
        if (gameOver != null)
            _harmony.Patch(gameOver, prefix: new HarmonyMethod(typeof(Plugin), nameof(KeepPrimaryOnReset)));
        var respawn = AccessTools.Method(typeof(PlayerSpawner), "RespawnInPlace");
        if (respawn != null)
            _harmony.Patch(respawn, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeRespawn)), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterRespawn)));
        else
            Logger.LogWarning("Couldn't patch player respawn.");
        _harmony.Patch(AccessTools.Method(typeof(PlayerSpawner), nameof(PlayerSpawner.TryGetPlayerOwnedBy)), prefix: new HarmonyMethod(typeof(Plugin), nameof(PickRespawnPlayer)));
        var claimFocus = AccessTools.Method(typeof(LocalPlayerFocusTarget), "SetControlledOwnerRpc");
        if (claimFocus != null)
            _harmony.Patch(claimFocus, prefix: new HarmonyMethod(typeof(Plugin), nameof(BlockCouchFocusSteal)));
        else
            Logger.LogWarning("Couldn't patch local player focus.");
        var deathSpectate = AccessTools.Method(typeof(PlayerDeathSpectator), "SetSpectating");
        if (deathSpectate != null)
            _harmony.Patch(deathSpectate, prefix: new HarmonyMethod(typeof(Plugin), nameof(OnlyPrimaryPlayerSpectates)));
        else
            Logger.LogWarning("Couldn't patch death spectate.");
        var spectatorName = AccessTools.Method(typeof(SpectatorUI), "OnLocalPlayerChanged");
        if (spectatorName != null)
            _harmony.Patch(spectatorName, postfix: new HarmonyMethod(typeof(Plugin), nameof(UseCouchSpectatorName)));
        PatchPromptRefresh("InputIcons.II_ImagePrompt", "UpdateDisplayedImages");
        PatchPromptRefresh("InputIcons.II_ImagePromptBehaviour", "UpdateDisplayedImages");
        PatchPromptRefresh("InputIcons.II_SpritePrompt", "UpdateDisplayedSprites");
        PatchPromptRefresh("InputIcons.II_TextPrompt", "UpdateDisplayedSprites");
        RenderPipelineManager.beginCameraRendering += BeforeSplitCamera;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded. Press F7 to toggle player 2.");
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeSplitCamera;
        TurnOff();
        _harmony?.UnpatchSelf();
    }

    static void BeforeSplitCamera(ScriptableRenderContext context, Camera camera)
    {
        if (_split && IsSplitCamera(camera))
        {
            ApplySplitRects();
            KeepNameplatesActive();
        }
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f10Key.wasPressedThisFrame)
        {
            if (_player2 != null || _wantPlayer2 || _split)
                TurnOff();
            else
                TurnOn();
        }
        else if (_player2 == null)
        {
            if (_wantPlayer2)
                RestorePlayer2();
            else if (_split)
                TurnOff();
        }

        if (_player2 != null)
        {
            UpdatePlayer2DeathSpectate();
            DrivePlayer2();
            AssignPlayer1Devices();
            if (Gamepad.all.Count != _promptPadCount)
                ApplyPromptDevices();
        }

        UpdateCustomizeMenuControl();
    }

    private void LateUpdate()
    {
        ApplyMenuCursor();
        if (_split)
            KeepNameplatesActive();
        if (_player2 != null)
        {
            if (_applyLookPending && ApplyPlayer2Look())
                _applyLookPending = false;
            RememberPlayer2Look();
            if (_hasSavedAccent)
                ApplySavedAccent(_player2);
            LinkPlayer2Name();
        }
        TrackPrimaryLook();
        if (_split)
        {
            CreateSplitCamera();
            EnsureMainCameras();
            ApplySplitRects();
            FitHud(FindPrimaryPlayer(), 0f, 0.5f);
            if (_player2 != null)
                FitHud(_player2, 0.5f, 1f);
            AimFurCameras();
            SamplePlayer2Spectate();
            ShowPlayer2SpectatorUi();
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
        DrawPlayer2Name();
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
        ResetHeldButtons();
        _player2Spectating = false;
        _player2SpectateTarget = null;
        _player2DeathTime = -1f;
        _savedBodyPrefab = prefab;
        _wantPlayer2 = true;
        _applyLookPending = true;
        _lookSettleFrames = 8;
        _split = true;
        AssignPlayer1Devices();
        Logger.LogInfo($"Player 2 on ({prefab.name}). {ControlHint()}");
    }

    static void RestorePlayer2()
    {
        if (Time.unscaledTime < _nextPlayer2SpawnTime)
            return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || FindPrimaryPlayer() == null)
            return;
        _nextPlayer2SpawnTime = Time.unscaledTime + 0.5f;
        TurnOn();
    }

    static void TurnOff()
    {
        if (_player2 != null)
            RememberPlayer2Look();
        _wantPlayer2 = false;
        _applyLookPending = false;
        _splitCameraSourceId = 0;
        ReleaseCustomizeMenuControl();
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
        _player2Spectating = false;
        _player2SpectateTarget = null;
        _player2DeathTime = -1f;
        RestoreHuds();
        RestoreCameras();
        RestoreNameplateCanvases();
        RestoreFurCameras();
        RestoreInputDevices();
        ResetHeldButtons();
        if (_split)
            Logger.LogInfo("Player 2 off.");
        _split = false;
        RestoreLocalNameplate();
    }

    static void DrivePlayer2()
    {
        var controller = _player2;
        if (controller == null)
            return;

        if (Time.deltaTime == 0f || PlayerInput.IsSuppressed || MenuSceneManager.IsMenuOpen)
        {
            controller.SetLocalMoveInput(Vector2.zero);
            _player2Look = Vector2.zero;
            if (controller.Pointing != null)
                controller.Pointing.SetPointInput(PointInput.None);
            controller.AdjustHeldItem(false, Vector3Int.zero, 0f);
            ReleasePlayer2();
            return;
        }

        var gamepad = Gamepad.all.Count >= 2 ? Gamepad.all[1] : Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
        var keyboard = Keyboard.current;
        var move = Vector2.zero;
        var look = Vector2.zero;
        var jump = false;
        var sprint = false;
        var attack = false;
        var interact = false;
        var bite = false;
        var throwItem = false;
        var point = PointInput.None;
        var pointHeld = false;

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
            point = ReadPoint(gamepad, move);
            pointHeld = ReadGamepad(gamepad, "Point") > 0.5f;
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
            if (keyboard.gKey.isPressed)
            {
                pointHeld = true;
                point |= PointInput.Pointing;
                if (keyboard.lKey.isPressed)
                    point |= PointInput.Right;
                if (keyboard.jKey.isPressed)
                    point |= PointInput.Left;
                if (keyboard.iKey.isPressed)
                    point |= PointInput.Up;
                if (keyboard.kKey.isPressed)
                    point |= PointInput.Down;
            }
        }

        if (move.sqrMagnitude < 0.04f)
            move = Vector2.zero;
        else if (move.sqrMagnitude > 1f)
            move.Normalize();

        if (Player2Spectating())
        {
            if (_player2SpectateTarget == null)
                _player2SpectateTarget = _player2;
            else if (_player2SpectateTarget != _player2 && _player2SpectateTarget.IsDead)
            {
                var living = NextSpectateTarget(_player2SpectateTarget);
                var guard = 0;
                while (living != null && living.IsDead && living != _player2SpectateTarget && guard < 8)
                {
                    living = NextSpectateTarget(living);
                    guard++;
                }
                if (living != null && !living.IsDead)
                    _player2SpectateTarget = living;
            }
            controller.SetLocalMoveInput(Vector2.zero);
            _player2Look = Vector2.zero;
            if (controller.Pointing != null)
                controller.Pointing.SetPointInput(PointInput.None);
            controller.AdjustHeldItem(false, Vector3Int.zero, 0f);
            var attackWasHeld = _attackHeld;
            var jumpWasHeld = _jumpHeld;
            ReleasePlayer2();
            if ((attack && !attackWasHeld) || (jump && !jumpWasHeld))
                _player2SpectateTarget = NextSpectateTarget(_player2SpectateTarget);
            _jumpHeld = jump;
            _sprintHeld = sprint;
            _attackHeld = attack;
            _interactHeld = interact;
            _biteHeld = bite;
            _throwHeld = throwItem;
            return;
        }

        if (Options.Instance != null)
        {
            look *= Options.Instance.MouseSensitivity;
            if (Options.Instance.InvertLook == Options.Toggle.On)
                look.y *= -1f;
        }

        _player2Look = look;
        controller.SetLocalMoveInput(move);
        if (controller.Pointing != null)
            controller.Pointing.SetPointInput(point);
        var rotate = Vector3Int.zero;
        var modifying = pointHeld && controller.Inventory != null && controller.Inventory.HasItem();
        if (modifying)
        {
            if (move.y > 0.5f)
                rotate.x++;
            if (move.y < -0.5f)
                rotate.x--;
            if (move.x > 0.5f)
                rotate.y--;
            if (move.x < -0.5f)
                rotate.y++;
            if (gamepad != null)
            {
                if (ReadGamepad(gamepad, "PointUp") > 0.5f)
                    rotate.x++;
                if (ReadGamepad(gamepad, "PointDown") > 0.5f)
                    rotate.x--;
                if (ReadGamepad(gamepad, "PointRight") > 0.5f)
                    rotate.y--;
                if (ReadGamepad(gamepad, "PointLeft") > 0.5f)
                    rotate.y++;
            }
        }
        controller.AdjustHeldItem(modifying, rotate, 0f);
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
        if (_player2.Pointing != null) _player2.Pointing.SetPointInput(PointInput.None);
        _player2.AdjustHeldItem(false, Vector3Int.zero, 0f);
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
        if (_savedBodyPrefab != null)
            return _savedBodyPrefab;

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

    static GameObject FindNetworkPrefab(NetworkObject instance)
    {
        if (instance == null || NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
            return null;
        var wantedHash = PrefabHash(instance);
        var wantedName = instance.name.Replace("(Clone)", "").Trim();
        foreach (var entry in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
        {
            var prefab = entry.Override == NetworkPrefabOverride.None ? entry.Prefab : entry.OverridingTargetPrefab;
            if (prefab == null)
                continue;
            var networkObject = prefab.GetComponent<NetworkObject>();
            if (networkObject == null)
                continue;
            if ((wantedHash != 0 && networkObject.PrefabIdHash == wantedHash) || prefab.name == wantedName)
                return prefab;
        }
        return null;
    }

    static void RememberPlayer2Look()
    {
        if (_player2 == null)
            return;
        var body = FindNetworkPrefab(_player2.GetComponent<NetworkObject>());
        if (body != null)
            _savedBodyPrefab = body;
        var slots = _player2.GetComponentInChildren<CosmeticSlots>(true);
        var allowClear = _lookSettleFrames <= 0;
        if (_lookSettleFrames > 0)
            _lookSettleFrames--;
        RememberCosmetic(slots != null ? slots.HatSlot : null, ref _savedHatPrefab, ref _savedHatOffset, ref _savedHatRotation, allowClear);
        RememberCosmetic(slots != null ? slots.GlassesSlot : null, ref _savedGlassesPrefab, ref _savedGlassesOffset, ref _savedGlassesRotation, allowClear);
    }

    static void RememberCosmetic(CosmeticAttachPoint point, ref GameObject prefab, ref Vector3 offset, ref Quaternion rotation, bool allowClear)
    {
        if (point != null && point.TryGetAttachedCosmetic(out var cosmetic) && cosmetic != null)
        {
            var found = FindNetworkPrefab(cosmetic.GetComponent<NetworkObject>());
            if (found != null)
            {
                prefab = found;
                offset = cosmetic.AttachOffset;
                rotation = cosmetic.AttachRotation;
            }
            return;
        }
        if (allowClear)
            prefab = null;
    }

    static bool ApplyPlayer2Look()
    {
        if (_player2 == null)
            return false;
        ApplySavedAccent(_player2);
        if (_savedHatPrefab == null && _savedGlassesPrefab == null)
            return true;
        var slots = _player2.GetComponentInChildren<CosmeticSlots>(true);
        if (slots == null)
            return false;
        return AttachSavedCosmetic(_player2, slots.HatSlot, _savedHatPrefab, _savedHatOffset, _savedHatRotation)
            && AttachSavedCosmetic(_player2, slots.GlassesSlot, _savedGlassesPrefab, _savedGlassesOffset, _savedGlassesRotation);
    }

    static bool AttachSavedCosmetic(PlayerController owner, CosmeticAttachPoint point, GameObject prefab, Vector3 offset, Quaternion rotation)
    {
        if (prefab == null || point == null || owner == null)
            return true;
        if (point.NetworkObject == null || !point.NetworkObject.IsSpawned)
            return false;
        if (point.HasCosmeticAttached())
            return true;
        var spawned = Util.InstantiateAndSpawnPrefab(prefab, owner.transform.position + Vector3.up, Quaternion.identity, null, owner.OwnerClientId);
        if (spawned == null || !spawned.TryGetComponent<Item>(out var item))
        {
            if (spawned != null)
                Destroy(spawned);
            return true;
        }
        item.AttachTo(point, offset, rotation, 0f);
        return true;
    }

    static void ApplySavedAccent(PlayerController player)
    {
        if (!_hasSavedAccent || player == null)
            return;
        foreach (var accent in player.GetComponentsInChildren<AccentColorMaterial>(true))
        {
            if (accent != null && accent.Renderer != null)
                accent.Renderer.material.color = _savedAccent;
        }
    }

    static void CreateSplitCamera()
    {
        if (!LocalCamera.Instance.TryGet(out var localCamera))
            return;

        var sourceId = localCamera.GetInstanceID();
        var splitReady = false;
        if (_camera2Root != null && _splitCameraSourceId == sourceId)
        {
            foreach (var camera in _camera2Root.GetComponentsInChildren<Camera>(true))
            {
                if (camera != null && camera.enabled)
                {
                    splitReady = true;
                    break;
                }
            }
        }
        if (splitReady)
            return;
        if (_camera2Root != null)
            Destroy(_camera2Root);

        _suppressLocalCamera = true;
        _camera2Root = Instantiate(localCamera.gameObject);
        _camera2Root.name = "CouchPlayer2Camera";
        _suppressLocalCamera = false;
        _splitCameraSourceId = sourceId;

        foreach (var listener in _camera2Root.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;
        foreach (var extra in _camera2Root.GetComponentsInChildren<LocalCamera>(true))
            extra.enabled = false;
        foreach (var camera in _camera2Root.GetComponentsInChildren<Camera>(true))
        {
            camera.tag = "Untagged";
            camera.enabled = true;
        }

        _mainCameras.Clear();
        _mainRects.Clear();
        foreach (var camera in localCamera.GetComponentsInChildren<Camera>(true))
        {
            _mainCameras.Add(camera);
            _mainRects.Add(new Rect(0f, 0f, 1f, 1f));
        }
        ApplySplitRects();
    }

    static void RememberCustomizePlayer(NetworkObject player, bool interacting)
    {
        if (!interacting || player == null)
            return;
        var controller = player.GetComponentInParent<PlayerController>();
        if (controller != null)
            _customizePlayer = controller;
    }

    static void ProtectPrimaryLoadout()
    {
        if (_customizePlayer == null || _customizePlayer != _player2)
            return;
        var primary = FindPrimaryPlayer();
        if (primary != null)
            RememberPrimaryLook(primary, true);
    }

    static void KeepPrimaryOnReset()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;
        var primary = FindPrimaryPlayer();
        if (primary != null)
            RememberPrimaryLook(primary, true);
        RestorePrimaryChoice();
    }

    static void TrackPrimaryLook()
    {
        var primary = FindPrimaryPlayer();
        if (primary == null)
            return;
        var id = primary.GetInstanceID();
        if (id != _primaryLookId)
        {
            var seenBefore = _primaryLookId != 0;
            _primaryLookId = id;
            if (!seenBefore || _acceptPrimaryRespawn)
            {
                _acceptPrimaryRespawn = false;
                _primaryAcceptedId = id;
                _applyPrimaryLook = false;
                _primarySettleFrames = 8;
            }
            else
            {
                _applyPrimaryLook = _savedPrimaryBody != null || _hasSavedPrimaryAccent || _savedPrimaryHat != null || _savedPrimaryGlasses != null;
                _primarySettleFrames = 8;
            }
        }
        if (_applyPrimaryLook && ApplyPrimaryLook(primary))
        {
            _applyPrimaryLook = false;
            var body = FindNetworkPrefab(primary.GetComponent<NetworkObject>());
            if (_savedPrimaryBody == null || body == _savedPrimaryBody)
                _primaryAcceptedId = id;
        }
        var acceptLive = id == _primaryAcceptedId && (_customizePlayer == null || _customizePlayer != _player2);
        RememberPrimaryLook(primary, acceptLive);
    }

    static void RememberPrimaryLook(PlayerController player, bool acceptLive)
    {
        if (player == null)
            return;
        var body = FindNetworkPrefab(player.GetComponent<NetworkObject>());
        if (body != null && (acceptLive || _savedPrimaryBody == null))
            _savedPrimaryBody = body;
        if (acceptLive && _savedPrimaryBody != null && PlayerLoadout.TryGetByOwner(player.OwnerClientId, out var loadout) && loadout != null && loadout.Customization != null)
        {
            var characters = loadout.Customization.Characters;
            var savedHash = _savedPrimaryBody.GetComponent<NetworkObject>() != null ? _savedPrimaryBody.GetComponent<NetworkObject>().PrefabIdHash : 0u;
            for (var i = 0; i < characters.Length; i++)
            {
                var character = characters[i].Prefab;
                if (character == null)
                    continue;
                var characterHash = character.GetComponent<NetworkObject>() != null ? character.GetComponent<NetworkObject>().PrefabIdHash : 0u;
                if (character == _savedPrimaryBody || (savedHash != 0 && savedHash == characterHash) || character.name == _savedPrimaryBody.name)
                {
                    _savedPrimaryIndex = i;
                    break;
                }
            }
            var accent = player.GetComponentInChildren<AccentColorMaterial>(true);
            if (accent != null && accent.Renderer != null && accent.Renderer.material.color.a > 0f)
            {
                _savedPrimaryAccent = accent.Renderer.material.color;
                _hasSavedPrimaryAccent = true;
            }
        }
        var slots = player.GetComponentInChildren<CosmeticSlots>(true);
        var allowClear = acceptLive && _primarySettleFrames <= 0;
        if (_primarySettleFrames > 0)
            _primarySettleFrames--;
        RememberCosmetic(slots != null ? slots.HatSlot : null, ref _savedPrimaryHat, ref _savedPrimaryHatOffset, ref _savedPrimaryHatRotation, allowClear);
        RememberCosmetic(slots != null ? slots.GlassesSlot : null, ref _savedPrimaryGlasses, ref _savedPrimaryGlassesOffset, ref _savedPrimaryGlassesRotation, allowClear);
    }

    static bool ApplyPrimaryLook(PlayerController player)
    {
        if (player == null)
            return false;
        if (_hasSavedPrimaryAccent)
        {
            foreach (var accent in player.GetComponentsInChildren<AccentColorMaterial>(true))
            {
                if (accent != null && accent.Renderer != null)
                    accent.Renderer.material.color = _savedPrimaryAccent;
            }
        }
        if (_savedPrimaryHat == null && _savedPrimaryGlasses == null)
            return true;
        var slots = player.GetComponentInChildren<CosmeticSlots>(true);
        if (slots == null)
            return false;
        return AttachSavedCosmetic(player, slots.HatSlot, _savedPrimaryHat, _savedPrimaryHatOffset, _savedPrimaryHatRotation)
            && AttachSavedCosmetic(player, slots.GlassesSlot, _savedPrimaryGlasses, _savedPrimaryGlassesOffset, _savedPrimaryGlassesRotation);
    }

    static void RestorePrimaryChoice()
    {
        if (_savedPrimaryIndex < 0 && !_hasSavedPrimaryAccent)
            return;
        if (Options.Instance != null)
        {
            if (_savedPrimaryIndex >= 0)
                Options.Instance.CharacterIndex = _savedPrimaryIndex;
            if (_hasSavedPrimaryAccent)
                Options.Instance.AccentColor = _savedPrimaryAccent;
        }
        var clientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
        var primary = FindPrimaryPlayer();
        if (primary != null)
            clientId = primary.OwnerClientId;
        if (!PlayerLoadout.TryGetByOwner(clientId, out var loadout) || loadout == null || !loadout.IsSpawned)
            return;
        var indexVar = AccessTools.Field(typeof(PlayerLoadout), "_characterIndexVar")?.GetValue(loadout) as NetworkVariable<int>;
        var colorVar = AccessTools.Field(typeof(PlayerLoadout), "_accentColorVar")?.GetValue(loadout) as NetworkVariable<Color>;
        if (indexVar != null && _savedPrimaryIndex >= 0 && indexVar.Value != _savedPrimaryIndex)
        {
            _keepPrimaryBody = true;
            indexVar.Value = _savedPrimaryIndex;
            _keepPrimaryBody = false;
        }
        if (colorVar != null && _hasSavedPrimaryAccent)
            colorVar.Value = _savedPrimaryAccent;
    }

    static void ClearCustomizePlayer()
    {
        var player2Customized = _customizePlayer != null && _customizePlayer == _player2;
        if (player2Customized)
            SavePlayer2Choice();
        ReleaseCustomizeMenuControl();
        _customizePlayer = null;
        if (player2Customized)
            RestorePrimaryChoice();
    }

    static void SavePlayer2Choice()
    {
        if (_player2 == null || Options.Instance == null)
            return;
        if (PlayerLoadout.TryGetByOwner(_player2.OwnerClientId, out var loadout) && loadout != null && loadout.Customization != null && loadout.Customization.TryGetPrefab(Options.Instance.CharacterIndex, out var prefab))
            _savedBodyPrefab = prefab;
        if (Options.Instance.AccentColor.a > 0f)
        {
            _savedAccent = Options.Instance.AccentColor;
            _hasSavedAccent = true;
            ApplySavedAccent(_player2);
        }
    }

    static bool Player2OwnsCustomizeMenu()
    {
        if (_player2 == null || _customizePlayer != _player2)
            return false;
        var ui = UnityEngine.Object.FindFirstObjectByType<CustomizeUI>();
        if (ui == null || !ui.isActiveAndEnabled)
            return false;
        var stack = ui.GetComponentInParent<MenuSceneStack>(true);
        return stack != null && stack.IsVisible;
    }

    static bool HideCursorForPlayer2Menu(ref bool __result)
    {
        if (!Player2OwnsCustomizeMenu())
            return true;
        __result = false;
        return false;
    }

    static void ApplyMenuCursor()
    {
        if (Player2OwnsCustomizeMenu())
        {
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
            _hidCursorForPlayer2 = true;
            return;
        }

        if (!_hidCursorForPlayer2 && !MenuSceneManager.IsMenuOpen)
            return;
        _hidCursorForPlayer2 = false;
        var show = MenuSceneManager.IsMenuOpen;
        UnityEngine.Cursor.visible = show;
        if (show)
            UnityEngine.Cursor.lockState = CursorLockMode.None;
    }

    static bool KeepCustomizeOnButtons(InputNavigationManager __instance)
    {
        if (!Player2OwnsCustomizeMenu())
            return true;
        AccessTools.Method(typeof(InputNavigationManager), "SetNavigationType")?.Invoke(__instance, new object[] { NavigationType.Button });
        return false;
    }

    static void UpdateCustomizeMenuControl()
    {
        if (!Player2OwnsCustomizeMenu())
        {
            ReleaseCustomizeMenuControl();
            return;
        }

        ApplyMenuCursor();
        SilenceCustomizeMenuActions();
        IgnoreCustomizePointer();
        DriveCustomizeMenu();
    }

    static void SilenceCustomizeMenuActions()
    {
        if (_silencedMenuActions.Count > 0)
            return;
        foreach (var module in UnityEngine.Object.FindObjectsByType<InputSystemUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SilenceMenuAction(module.point);
            SilenceMenuAction(module.leftClick);
            SilenceMenuAction(module.rightClick);
            SilenceMenuAction(module.middleClick);
            SilenceMenuAction(module.scrollWheel);
            SilenceMenuAction(module.move);
            SilenceMenuAction(module.submit);
            SilenceMenuAction(module.cancel);
        }
    }

    static void SilenceMenuAction(InputActionReference reference)
    {
        var action = reference?.action;
        if (action == null || !action.enabled || _silencedMenuActions.Contains(action))
            return;
        action.Disable();
        _silencedMenuActions.Add(action);
    }

    static void IgnoreCustomizePointer()
    {
        var root = CustomizeRoot();
        if (root == null)
            return;
        root.Query<VisualElement>().ForEach(element =>
        {
            if (!_menuPicking.ContainsKey(element))
                _menuPicking.Add(element, element.pickingMode);
            element.pickingMode = PickingMode.Ignore;
        });
    }

    static VisualElement CustomizeRoot()
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<CustomizeUI>();
        if (ui == null || !ui.isActiveAndEnabled)
            return null;
        return ui.GetComponent<UIDocument>()?.rootVisualElement;
    }

    static void DriveCustomizeMenu()
    {
        var focused = CustomizeRoot()?.panel?.focusController?.focusedElement as VisualElement;
        if (focused == null)
            return;

        var move = Vector2.zero;
        var submit = false;
        var cancel = false;
        var gamepad = Gamepad.all.Count >= 2 ? Gamepad.all[1] : Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
        if (gamepad != null)
        {
            move = gamepad.leftStick.ReadValue();
            var dpad = gamepad.dpad.ReadValue();
            if (dpad.sqrMagnitude > move.sqrMagnitude)
                move = dpad;
            submit = gamepad.buttonSouth.wasPressedThisFrame;
            cancel = gamepad.buttonEast.wasPressedThisFrame;
        }
        else if (Keyboard.current != null)
        {
            var keyboard = Keyboard.current;
            if (keyboard.lKey.isPressed) move.x += 1f;
            if (keyboard.jKey.isPressed) move.x -= 1f;
            if (keyboard.iKey.isPressed) move.y += 1f;
            if (keyboard.kKey.isPressed) move.y -= 1f;
            submit = keyboard.commaKey.wasPressedThisFrame || keyboard.nKey.wasPressedThisFrame;
            cancel = keyboard.periodKey.wasPressedThisFrame;
        }

        var direction = Vector2.zero;
        if (Mathf.Abs(move.x) >= Mathf.Abs(move.y))
        {
            if (move.x > 0.5f)
                direction = Vector2.right;
            else if (move.x < -0.5f)
                direction = Vector2.left;
        }
        else if (move.y > 0.5f)
            direction = Vector2.up;
        else if (move.y < -0.5f)
            direction = Vector2.down;

        if (direction == Vector2.zero)
            _menuNavTime = 0f;
        else if (Time.unscaledTime >= _menuNavTime)
        {
            using (var moveEvent = NavigationMoveEvent.GetPooled(direction))
                focused.SendEvent(moveEvent);
            _menuNavTime = Time.unscaledTime + (_menuNavTime == 0f ? 0.38f : 0.16f);
        }

        if (submit)
        {
            using (var submitEvent = NavigationSubmitEvent.GetPooled())
                focused.SendEvent(submitEvent);
        }

        if (cancel)
        {
            using (var cancelEvent = NavigationCancelEvent.GetPooled())
                focused.SendEvent(cancelEvent);
        }
    }

    static void ReleaseCustomizeMenuControl()
    {
        foreach (var action in _silencedMenuActions)
        {
            if (action != null)
                action.Enable();
        }
        _silencedMenuActions.Clear();
        foreach (var entry in _menuPicking)
        {
            if (entry.Key != null)
                entry.Key.pickingMode = entry.Value;
        }
        _menuPicking.Clear();
        _menuNavTime = 0f;
        ApplyMenuCursor();
    }

    static void KeepNameplatesActive()
    {
        foreach (var plate in UnityEngine.Object.FindObjectsByType<PlayerNameplateUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (plate == null)
                continue;
            ShowNameplateChain(plate.transform);
            if (plate.Container != null && !plate.Container.gameObject.activeSelf)
                plate.Container.gameObject.SetActive(true);
            if (plate.NameText == null)
                continue;
            if (IsPlayer2(plate))
                plate.NameText.text = CouchPlayerName();
            else if (string.IsNullOrEmpty(plate.NameText.text) && plate.FocusTarget != null && plate.FocusTarget.Controller.TryGet(out var online) && online.PlayerName.TryGet(out var playerName) && !string.IsNullOrEmpty(playerName))
                plate.NameText.text = playerName;
        }
    }

    static void ShowNameplateChain(Transform node)
    {
        if (node == null || node.GetComponent<PlayerController>() != null)
            return;
        ShowNameplateChain(node.parent);
        var toggle = node.GetComponent<LocalPlayerFocusToggle>();
        if (toggle != null && (toggle.Activation == LocalPlayerFocusToggle.ActivationOption.NotFocused || toggle.Activation == LocalPlayerFocusToggle.ActivationOption.NotControlled))
            toggle.SetState(false, false, false);
        if (!node.gameObject.activeSelf)
            node.gameObject.SetActive(true);
    }

    static void RestoreLocalNameplate()
    {
        var primary = FindPrimaryPlayer();
        if (primary == null)
            return;
        var plate = primary.GetComponentInChildren<PlayerNameplateUI>(true);
        if (plate == null)
            return;
        var node = plate.transform;
        while (node != null && node != primary.transform)
        {
            var toggle = node.GetComponent<LocalPlayerFocusToggle>();
            if (toggle != null && (toggle.Activation == LocalPlayerFocusToggle.ActivationOption.NotFocused || toggle.Activation == LocalPlayerFocusToggle.ActivationOption.NotControlled))
                toggle.SetState(true, true, false);
            node = node.parent;
        }
    }

    static void PlaceNameplateForCamera(PlayerNameplateUI __instance, Camera c)
    {
        if (__instance.Container == null || __instance.CanvasGroup == null)
            return;
        var canvas = __instance.Container.GetComponentInParent<Canvas>();
        if (!_split)
        {
            RestoreNameplateCanvas(canvas);
            return;
        }
        RestoreNameplateCanvas(canvas);
        ApplySplitRects();

        var viewer = IsPlayer2(__instance) ? SplitCamera(false) : SplitCamera(true);
        if (viewer == null)
            return;
        var position = __instance.transform.position;
        if (__instance.PlayerPointing != null)
            position.y += __instance.PlayerPointing.PointingBlend * __instance.PointingHeightOffset;
        var toCamera = position - viewer.transform.position;
        var viewport = viewer.WorldToViewportPoint(position);
        var pixel = viewer.pixelRect;
        if (Vector3.Dot(viewer.transform.forward, toCamera) <= 0f || viewport.z <= 0.1f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            __instance.CanvasGroup.alpha = 0f;
            return;
        }

        var distance = toCamera.magnitude;
        __instance.CanvasGroup.alpha = __instance.AlphaForDistance.Eval(distance);
        __instance.Container.localScale = Vector3.one * __instance.ScaleForDistance.Eval(distance);
        var margin = 36f;
        var screen = new Vector2(
            Mathf.Clamp(pixel.x + viewport.x * pixel.width, pixel.xMin + margin, Mathf.Max(pixel.xMin + margin, pixel.xMax - margin)),
            Mathf.Clamp(pixel.y + viewport.y * pixel.height, pixel.yMin + margin, Mathf.Max(pixel.yMin + margin, pixel.yMax - margin)));
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)__instance.Container.parent, screen, null, out var localPoint))
            __instance.Container.localPosition = localPoint;
    }

    static Camera SplitCamera(bool player2)
    {
        Camera best = null;
        var bestArea = 0f;
        if (player2)
        {
            if (_camera2Root == null)
                return null;
            foreach (var camera in _camera2Root.GetComponentsInChildren<Camera>(true))
            {
                if (camera == null || !camera.enabled)
                    continue;
                var area = camera.pixelRect.width * camera.pixelRect.height;
                if (area > bestArea)
                {
                    best = camera;
                    bestArea = area;
                }
            }
            return best;
        }

        foreach (var camera in _mainCameras)
        {
            if (camera == null || !camera.enabled)
                continue;
            var area = camera.pixelRect.width * camera.pixelRect.height;
            if (area > bestArea)
            {
                best = camera;
                bestArea = area;
            }
        }
        return best;
    }

    static void AimFurCameras()
    {
        var first = SplitCamera(false);
        var second = SplitCamera(true);
        foreach (var fur in UnityEngine.Object.FindObjectsByType<FurRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (fur == null)
                continue;
            if (!_savedFurCameras.ContainsKey(fur))
                _savedFurCameras.Add(fur, fur.lodCamera);
            var skin = fur.GetComponent<SkinnedMeshRenderer>();
            if (skin != null && !skin.updateWhenOffscreen)
            {
                skin.updateWhenOffscreen = true;
                _openedFurSkins.Add(skin);
            }
            var chosen = FurCameraFor(fur, first, second);
            if (chosen != null)
                fur.lodCamera = chosen;
        }
    }

    static Camera FurCameraFor(FurRenderer fur, Camera first, Camera second)
    {
        var renderer = fur.GetComponent<Renderer>();
        var bounds = renderer != null ? renderer.bounds : new Bounds(fur.transform.position, Vector3.one * 1.5f);
        if (bounds.size.sqrMagnitude < 0.0001f)
            bounds = new Bounds(fur.transform.position, Vector3.one * 1.5f);
        bounds.Expand(0.4f);
        var firstScore = FrustumScore(first, bounds);
        var secondScore = FrustumScore(second, bounds);
        if (firstScore <= 0 && secondScore <= 0)
            return null;
        if (secondScore > firstScore)
            return second;
        if (firstScore > secondScore)
            return first;
        var origin = bounds.center;
        var firstDistance = first != null ? (first.transform.position - origin).sqrMagnitude : float.MaxValue;
        var secondDistance = second != null ? (second.transform.position - origin).sqrMagnitude : float.MaxValue;
        return secondDistance < firstDistance ? second : first;
    }

    static int FrustumScore(Camera camera, Bounds bounds)
    {
        if (camera == null || !camera.enabled)
            return 0;
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var center = bounds.center;
        var extents = bounds.extents;
        var score = 0;
        for (var x = -1f; x <= 1f; x += 2f)
        {
            for (var y = -1f; y <= 1f; y += 2f)
            {
                for (var z = -1f; z <= 1f; z += 2f)
                {
                    var point = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    var inside = true;
                    for (var i = 0; i < planes.Length; i++)
                    {
                        if (planes[i].GetDistanceToPoint(point) < -0.05f)
                        {
                            inside = false;
                            break;
                        }
                    }
                    if (inside)
                        score++;
                }
            }
        }
        return score;
    }

    static void RestoreFurCameras()
    {
        foreach (var entry in _savedFurCameras)
        {
            if (entry.Key != null)
                entry.Key.lodCamera = entry.Value;
        }
        _savedFurCameras.Clear();
        for (var i = 0; i < _openedFurSkins.Count; i++)
        {
            if (_openedFurSkins[i] != null)
                _openedFurSkins[i].updateWhenOffscreen = false;
        }
        _openedFurSkins.Clear();
    }

    static bool IsSplitCamera(Camera c)
    {
        if (c == null)
            return false;
        if (_camera2Root != null && c.transform.IsChildOf(_camera2Root.transform))
            return true;
        return _mainCameras.Contains(c);
    }

    static void RestoreNameplateCanvas(Canvas canvas)
    {
        if (canvas == null || !_nameplateCanvases.Contains(canvas))
            return;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        _nameplateCanvases.Remove(canvas);
    }

    static void RestoreNameplateCanvases()
    {
        for (var i = _nameplateCanvases.Count - 1; i >= 0; i--)
            RestoreNameplateCanvas(_nameplateCanvases[i]);
    }

    static bool BeforeRespawn()
    {
        if (_keepPrimaryBody)
        {
            _keepPrimaryBody = false;
            return false;
        }
        if (_customizePlayer != null && _customizePlayer != _player2)
            _acceptPrimaryRespawn = true;
        _pickCustomizePlayer = _customizePlayer != null;
        _replacingPlayer2 = _customizePlayer != null && _customizePlayer == _player2;
        if (_replacingPlayer2)
            SavePlayer2Choice();
        _blockCouchFocusSteal = _replacingPlayer2;
        _playersBeforeRespawn = new List<PlayerController>();
        foreach (var player in PlayerController.All)
            _playersBeforeRespawn.Add(player);
        return true;
    }

    static bool PickRespawnPlayer(ulong clientId, ref PlayerController player, ref bool __result)
    {
        if (!_pickCustomizePlayer || _customizePlayer == null || _customizePlayer.OwnerClientId != clientId)
            return true;
        player = _customizePlayer;
        __result = true;
        return false;
    }

    static bool BlockCouchFocusSteal()
    {
        return !_blockCouchFocusSteal;
    }

    static void AfterRespawn()
    {
        _pickCustomizePlayer = false;
        _blockCouchFocusSteal = false;
        var replacingPlayer2 = _replacingPlayer2;
        var before = _playersBeforeRespawn;
        _replacingPlayer2 = false;
        _playersBeforeRespawn = null;
        if (!replacingPlayer2)
            return;
        PlayerController spawned = null;
        foreach (var player in PlayerController.All)
        {
            if (player != null && (before == null || !before.Contains(player)))
                spawned = player;
        }
        if (spawned != null)
            AdoptPlayer2(spawned);
    }

    static void AdoptPlayer2(PlayerController spawned)
    {
        _player2 = spawned;
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
        AimDetector(_player2);
        FitHud(_player2, 0.5f, 1f);
        ApplyPromptDevices();
        ResetHeldButtons();
        if (_camera2Root == null)
            CreateSplitCamera();
        _lookSettleFrames = 8;
        ApplySavedAccent(spawned);
        var body = FindNetworkPrefab(spawned.GetComponent<NetworkObject>());
        if (body != null)
            _savedBodyPrefab = body;
        _split = true;
    }

    static void EnsureMainCameras()
    {
        if (!LocalCamera.Instance.TryGet(out var localCamera))
            return;
        for (var i = _mainCameras.Count - 1; i >= 0; i--)
        {
            if (_mainCameras[i] != null)
                continue;
            _mainCameras.RemoveAt(i);
            if (i < _mainRects.Count)
                _mainRects.RemoveAt(i);
        }
        foreach (var camera in localCamera.GetComponentsInChildren<Camera>(true))
        {
            if (camera == null || _mainCameras.Contains(camera))
                continue;
            _mainCameras.Add(camera);
            _mainRects.Add(new Rect(0f, 0f, 1f, 1f));
        }
    }

    static void ApplySplitRects()
    {
        var full = new Rect(0f, 0f, 1f, 1f);
        var left = new Rect(0f, 0f, 0.5f, 1f);
        var right = new Rect(0.5f, 0f, 0.5f, 1f);
        var rightReady = false;
        if (_camera2Root != null)
        {
            foreach (var camera in _camera2Root.GetComponentsInChildren<Camera>(true))
            {
                if (camera == null || !camera.enabled)
                    continue;
                camera.rect = right;
                rightReady = true;
            }
        }
        var main = rightReady ? left : full;
        foreach (var camera in _mainCameras)
        {
            if (camera != null)
                camera.rect = main;
        }
    }

    static void RestoreCameras()
    {
        var full = new Rect(0f, 0f, 1f, 1f);
        for (var i = 0; i < _mainCameras.Count; i++)
        {
            if (_mainCameras[i] != null)
                _mainCameras[i].rect = full;
        }
        if (LocalCamera.Instance.TryGet(out var localCamera))
        {
            foreach (var camera in localCamera.GetComponentsInChildren<Camera>(true))
            {
                if (camera != null)
                    camera.rect = full;
            }
        }
        _mainCameras.Clear();
        _mainRects.Clear();
        if (_camera2Root != null)
            Destroy(_camera2Root);
        _camera2Root = null;
        _splitCameraSourceId = 0;
    }

    static void AssignPlayer1Devices()
    {
        if (InputSystem.actions == null)
            return;
        InputDevice[] list = null;
        if (Gamepad.all.Count >= 2)
            list = new InputDevice[] { Gamepad.all[0] };
        else if (Keyboard.current != null && Mouse.current != null)
            list = new InputDevice[] { Keyboard.current, Mouse.current };
        if (list == null)
            return;
        var devices = new ReadOnlyArray<InputDevice>(list);
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
        if (Gamepad.all.Count >= 2)
            return "Two controllers: the first drives player 1, the second drives player 2.";
        if (Gamepad.all.Count > 0)
            return "Gamepad: stick move/look, A jump, LB sprint, RT attack, X interact, Y throw, LT bite";
        return "Keys: IJKL move, UO/YH look, N jump, RShift sprint, M attack, , interact, . throw, B bite, G+IJKL emote";
    }

    static bool IsPlayer2(Component component)
    {
        return _player2 != null && component != null && component.GetComponentInParent<PlayerController>(true) == _player2;
    }

    static bool SkipPlayer2Input(PlayerInput __instance)
    {
        return !IsPlayer2(__instance);
    }

    static void BeginPlayer2Camera(PlayerCamera __instance)
    {
        _drivingPlayer2 = IsPlayer2(__instance);
        _spectateSource = false;
        _spectateSourceKeepsView = false;
        if (!Player2Spectating() || __instance == null)
            return;
        var owner = __instance.GetComponentInParent<PlayerController>(true);
        if (owner == null || owner != _player2SpectateTarget)
            return;
        _spectateSource = true;
        _spectateSourceKeepsView = owner == FindPrimaryPlayer();
    }

    static void EndPlayer2Camera(PlayerCamera __instance)
    {
        if (Player2Spectating() && __instance != null && _camera2Root != null && __instance.GetComponentInParent<PlayerController>(true) == _player2SpectateTarget)
        {
            var fov = 60f;
            foreach (var camera in _mainCameras)
            {
                if (camera != null && camera.enabled)
                {
                    fov = camera.fieldOfView;
                    break;
                }
            }
            PoseCamera(_camera2Root, __instance.transform.position, __instance.transform.rotation, fov);
        }
        _drivingPlayer2 = false;
        _spectateSource = false;
        _spectateSourceKeepsView = false;
    }

    static void Player2ThrowStart(Inventory __instance)
    {
        _throwCameraSwapped = false;
        if (!IsPlayer2(__instance) || _camera2Root == null || !LocalCamera.Instance.TryGet(out var localCamera))
            return;
        _throwCameraPosition = localCamera.Position;
        _throwCameraRotation = localCamera.Rotation;
        localCamera.Position = _camera2Root.transform.position;
        localCamera.Rotation = _camera2Root.transform.rotation;
        _throwCameraSwapped = true;
    }

    static void Player2ThrowEnd()
    {
        if (!_throwCameraSwapped)
            return;
        _throwCameraSwapped = false;
        if (LocalCamera.Instance.TryGet(out var localCamera))
        {
            localCamera.Position = _throwCameraPosition;
            localCamera.Rotation = _throwCameraRotation;
        }
    }

    static bool HighlightForCouchPlayer(RefNotify<NetworkObject> focused, ref bool __result)
    {
        if (_player2 == null || !focused.TryGet(out var playerObject))
            return true;
        if (playerObject.TryGetComponent<PlayerController>(out var player) && player == _player2)
        {
            __result = true;
            return false;
        }
        return true;
    }

    static bool Player2CameraRay(PlayerController __instance, ref Vector3 start, ref Vector3 dir)
    {
        if (!IsPlayer2(__instance))
            return true;
        var playerCamera = __instance.GetComponentInChildren<PlayerCamera>(true);
        var camera = playerCamera != null ? playerCamera.transform : _camera2Root != null ? _camera2Root.transform : null;
        if (camera == null)
            return true;
        dir = camera.forward;
        start = camera.position;
        start += dir * Vector3.Dot(__instance.transform.position - start, dir);
        return false;
    }

    static void EnsureNameStyle()
    {
        if (_playerNameStyle != null)
            return;
        _playerNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
    }

    static void DrawPlayer2Name()
    {
        EnsureNameStyle();
        _playerNameStyle.alignment = TextAnchor.LowerLeft;
        var label = CouchPlayerName();
        var area = new Rect(Screen.width * 0.5f + 28f, Screen.height - 72f, Screen.width * 0.5f - 56f, 40f);
        _playerNameStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(area.x + 2f, area.y + 2f, area.width, area.height), label, _playerNameStyle);
        _playerNameStyle.normal.textColor = Color.white;
        GUI.Label(area, label, _playerNameStyle);
    }

    static void LinkPlayer2Name()
    {
        if (_player2 == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;
        var focus = _player2.GetComponent<LocalPlayerFocusTarget>() ?? _player2.GetComponentInChildren<LocalPlayerFocusTarget>(true);
        if (focus == null || focus.ControllerClientId == _player2.OwnerClientId)
            return;
        focus.SetController(_player2.OwnerClientId);
    }

    static string CouchPlayerName()
    {
        var primary = FindPrimaryPlayer();
        if (primary != null)
        {
            foreach (var online in OnlinePlayer.All)
            {
                if (online == null || online.OwnerClientId != primary.OwnerClientId)
                    continue;
                if (online.PlayerName.TryGet(out var value) && !string.IsNullOrEmpty(value))
                    return value + "2";
            }
        }
        var steamName = OnlineLocalPlayer.GetLocalPlayerSteamName();
        return (string.IsNullOrEmpty(steamName) ? "Player" : steamName) + "2";
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
        if (_spectateSource && _camera2Root != null)
        {
            PoseCamera(_camera2Root, position, rotation, fov);
            return _spectateSourceKeepsView;
        }
        if (!_drivingPlayer2 || _camera2Root == null)
            return true;
        if (Player2Spectating() && _player2SpectateTarget != _player2)
            return false;
        PoseCamera(_camera2Root, position, rotation, fov);
        if (SpectatingPlayer(_player2) && LocalCamera.Instance.TryGet(out var localCamera))
        {
            localCamera.transform.SetPositionAndRotation(position, rotation);
            foreach (var camera in _mainCameras)
            {
                if (camera != null)
                    camera.fieldOfView = fov;
            }
        }
        return false;
    }

    static void PoseCamera(GameObject root, Vector3 position, Quaternion rotation, float fov)
    {
        if (root == null)
            return;
        root.transform.SetPositionAndRotation(position, rotation);
        foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            camera.fieldOfView = fov;
    }

    static bool OnlyPrimaryPlayerSpectates(PlayerDeathSpectator __instance, bool spectating)
    {
        if (!_split || _player2 == null || __instance == null)
            return true;
        var owner = __instance.Health != null ? __instance.Health.GetComponentInParent<PlayerController>(true) : __instance.GetComponentInParent<PlayerController>(true);
        if (owner != _player2)
            return true;
        _player2Spectating = spectating;
        _player2SpectateTarget = spectating ? NextSpectateTarget(null) : null;
        return false;
    }

    static bool Player2Spectating()
    {
        return _split && _player2Spectating && _player2 != null && _player2SpectateTarget != null;
    }

    static void UpdatePlayer2DeathSpectate()
    {
        if (_player2 == null || !_split)
            return;
        if (!_player2.IsDead)
        {
            _player2DeathTime = -1f;
            if (_player2Spectating)
            {
                _player2Spectating = false;
                _player2SpectateTarget = null;
            }
            return;
        }
        if (_player2DeathTime < 0f)
            _player2DeathTime = Time.unscaledTime;
        var delay = 2f;
        var spectator = _player2.GetComponentInChildren<PlayerDeathSpectator>(true);
        if (spectator != null)
            delay = spectator.SpectatorDelay;
        if (Time.unscaledTime - _player2DeathTime < delay)
            return;
        if (_player2SpectateTarget == null)
            _player2SpectateTarget = _player2;
        _player2Spectating = _player2SpectateTarget != null;
    }

    static PlayerController NextSpectateTarget(PlayerController current)
    {
        var players = new System.Collections.Generic.List<PlayerController>();
        if (_player2 != null)
            players.Add(_player2);
        foreach (var focus in LocalPlayerFocusTarget.All.Items)
        {
            if (focus == null)
                continue;
            var player = focus.GetComponent<PlayerController>() ?? focus.GetComponentInParent<PlayerController>(true);
            if (player == null || players.Contains(player))
                continue;
            players.Add(player);
        }
        if (players.Count == 0)
            return null;
        var index = current == null ? -1 : players.IndexOf(current);
        return players[(index + 1) % players.Count];
    }

    static void SamplePlayer2Spectate()
    {
        if (!Player2Spectating() || _camera2Root == null || _player2SpectateTarget == null)
            return;
        var camera = _player2SpectateTarget.GetComponentInChildren<PlayerCamera>(true);
        if (camera != null && camera.isActiveAndEnabled)
            return;
        Vector3 position;
        Quaternion rotation;
        if (camera != null && (camera.transform.position - _player2SpectateTarget.transform.position).sqrMagnitude < 36f)
        {
            position = camera.transform.position;
            rotation = camera.transform.rotation;
        }
        else
        {
            rotation = Quaternion.Euler(25f, _player2SpectateTarget.transform.eulerAngles.y, 0f);
            var focus = _player2SpectateTarget.transform.position + Vector3.up * 1.4f;
            position = focus - rotation * Vector3.forward * 2.4f;
        }
        var view = _camera2Root.GetComponentInChildren<Camera>();
        PoseCamera(_camera2Root, position, rotation, view != null ? view.fieldOfView : 60f);
    }

    static void ShowPlayer2SpectatorUi()
    {
        if (_player2 == null)
            return;
        var show = Player2Spectating();
        var label = show ? SpectateTargetName(_player2SpectateTarget) : null;
        foreach (var ui in _player2.GetComponentsInChildren<SpectatorUI>(true))
        {
            var toggle = ui.GetComponent<LocalPlayerFocusToggle>();
            if (toggle == null)
                toggle = ui.GetComponentInParent<LocalPlayerFocusToggle>(true);
            if (show && !ui.gameObject.activeInHierarchy)
            {
                if (toggle != null)
                    toggle.SetState(true, true, true);
                var node = ui.transform;
                while (node != null && node.GetComponent<PlayerController>() == null)
                {
                    if (!node.gameObject.activeSelf)
                        node.gameObject.SetActive(true);
                    node = node.parent;
                }
            }
            else if (!show && ui.gameObject.activeSelf && toggle != null)
            {
                toggle.SetState(true, true, false);
            }
            if (show && ui.PlayerNameText != null)
                ui.PlayerNameText.text = label;
        }
        if (!SpectatingPlayer(_player2))
            return;
        var couchName = CouchPlayerName();
        foreach (var ui in UnityEngine.Object.FindObjectsByType<SpectatorUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (ui == null || ui.PlayerNameText == null || !ui.gameObject.activeInHierarchy)
                continue;
            if (ui.GetComponentInParent<PlayerController>(true) == _player2)
                continue;
            ui.PlayerNameText.text = couchName;
        }
    }

    static void UseCouchSpectatorName(SpectatorUI __instance)
    {
        if (__instance == null || __instance.PlayerNameText == null || _player2 == null)
            return;
        var owner = __instance.GetComponentInParent<PlayerController>(true);
        var watchingPlayer2 = owner == _player2 ? Player2Spectating() && _player2SpectateTarget == _player2 : SpectatingPlayer(_player2);
        if (watchingPlayer2)
            __instance.PlayerNameText.text = CouchPlayerName();
    }

    static string SpectateTargetName(PlayerController player)
    {
        if (player == null)
            return "";
        if (player == _player2)
            return CouchPlayerName();
        var focus = player.GetComponent<LocalPlayerFocusTarget>();
        if (focus != null && focus.Controller.TryGet(out var online) && online != null && online.PlayerName.TryGet(out var value) && !string.IsNullOrEmpty(value))
            return value;
        var steamName = OnlineLocalPlayer.GetLocalPlayerSteamName();
        return string.IsNullOrEmpty(steamName) ? "Player" : steamName;
    }

    static bool SpectatingPlayer(PlayerController player)
    {
        if (!_split || player == null || !LocalPlayer.Instance.TryGet(out var local) || local == null || !local.IsSpectating || local.FocusTarget == null)
            return false;
        return local.FocusTarget.GetComponentInParent<PlayerController>(true) == player;
    }

    static void ShowPlayer2Controls(LocalPlayerFocusToggle __instance, ref bool controlled, ref bool focused, ref bool spectated)
    {
        if (_split && IsNameplateToggle(__instance))
        {
            if (__instance.Activation == LocalPlayerFocusToggle.ActivationOption.NotFocused)
                focused = false;
            if (__instance.Activation == LocalPlayerFocusToggle.ActivationOption.NotControlled)
                controlled = false;
        }
        if (_split && __instance.Activation == LocalPlayerFocusToggle.ActivationOption.Spectated)
        {
            var owner = __instance.GetComponentInParent<PlayerController>(true);
            if (owner == _player2)
                spectated = Player2Spectating();
            else if (SpectatingPlayer(_player2) && owner == FindPrimaryPlayer())
                spectated = true;
            else if (Player2Spectating() && owner != null && owner == _player2SpectateTarget)
                spectated = false;
        }
        if (_split && SpectatingPlayer(_player2) && __instance.name == "PlayerHud" && __instance.GetComponentInParent<PlayerController>(true) == FindPrimaryPlayer())
            focused = true;
        if (!IsPlayer2(__instance))
            return;
        if (__instance.GetComponent<PlayerInput>() != null)
            controlled = true;
        if (__instance.GetComponent<PlayerCamera>() != null || __instance.name == "PlayerHud")
            focused = true;
    }

    static bool IsNameplateToggle(LocalPlayerFocusToggle toggle)
    {
        if (toggle.GetComponent<PlayerController>() != null || toggle.GetComponent<PlayerInput>() != null || toggle.GetComponent<PlayerCamera>() != null)
            return false;
        return toggle.GetComponent<PlayerNameplateUI>() != null || toggle.GetComponentInChildren<PlayerNameplateUI>(true) != null;
    }

    static PointInput ReadPoint(Gamepad pad, Vector2 move)
    {
        var point = PointInput.None;
        if (ReadGamepad(pad, "Point") > 0.5f)
        {
            point |= PointInput.Pointing;
            if (move.x > 0.5f)
                point |= PointInput.Right;
            if (move.x < -0.5f)
                point |= PointInput.Left;
            if (move.y > 0.5f)
                point |= PointInput.Up;
            if (move.y < -0.5f)
                point |= PointInput.Down;
        }
        if (ReadGamepad(pad, "PointRight") > 0.5f)
            point |= PointInput.Pointing | PointInput.Right;
        if (ReadGamepad(pad, "PointLeft") > 0.5f)
            point |= PointInput.Pointing | PointInput.Left;
        if (ReadGamepad(pad, "PointUp") > 0.5f)
            point |= PointInput.Pointing | PointInput.Up;
        if (ReadGamepad(pad, "PointDown") > 0.5f)
            point |= PointInput.Pointing | PointInput.Down;
        return point;
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
        _promptPadCount = -1;
    }

    static void ApplyPromptDevices()
    {
        _promptPadCount = Gamepad.all.Count;
        _promptsForGamepad = _promptPadCount > 0;
        var primary = FindPrimaryPlayer();
        if (primary != null)
            SetPromptDevice(primary.gameObject, _promptPadCount >= 2 ? "Gamepad" : "KeyboardAndMouse");
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
        if (action == "Point")
            return "G";
        if (action == "PointUp")
            return "I";
        if (action == "PointDown")
            return "K";
        if (action == "PointLeft")
            return "J";
        if (action == "PointRight")
            return "L";
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
