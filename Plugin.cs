using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Rewired;
using Rewired.Platforms;
using UnityEngine;

namespace KellysKNOBS
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("NuclearOption.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.kellys.nuclearoption.knobs";
        public const string PluginName = "Kelly's KNOBS";
        public const string PluginVersion = "1.0.0";

        private static readonly string[] GamepadNameMarkers =
        {
            "xbox",
            "xinput",
            "dualshock",
            "dualsense",
            "steam controller",
            "switch pro",
            "nintendo",
            "wireless controller",
            "8bitdo",
            "gamesir",
            "gulikit",
            "gamepad"
        };

        private static readonly string[] FlightControllerNameMarkers =
        {
            "joystick",
            "flight",
            "hotas",
            "throttle",
            "yoke",
            "rudder",
            "flight pedals",
            "winctrl",
            "winwing",
            "vkb",
            "virpil",
            "vpc",
            "saitek",
            "gladiator",
            "gunfighter"
        };

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<int> _minimumButtonCount;
        private ConfigEntry<bool> _directInputFallback;
        private ConfigEntry<bool> _autoAssign;
        private ConfigEntry<float> _statusSeconds;

        private bool _directInputAttempted;
        private bool _checkScheduled;
        private bool _checkInProgress;
        private string _statusText = string.Empty;
        private StatusKind _statusKind = StatusKind.Info;
        private float _statusUntil;

        private enum StatusKind
        {
            Info,
            Success,
            Warning,
            Error
        }

        private sealed class ControllerGroup
        {
            public string Name;
            public List<Joystick> Devices;

            public int TotalButtons
            {
                get { return Devices.Sum(device => device.buttonCount); }
            }

            public int MaxButtons
            {
                get { return Devices.Max(device => device.buttonCount); }
            }

            public bool LooksLikeSplitMode
            {
                get
                {
                    if (Devices.Count < 2 || Devices.Any(device => device.buttonCount > 32))
                        return false;

                    bool hasAxisDevice = Devices.Any(device => device.axisCount > 0);
                    bool hasButtonOnlyDevice = Devices.Any(device => device.axisCount == 0);

                    // WinWing-style split mode normally has one axis-bearing device and
                    // one or more button-only satellites. Three or more identical
                    // 32-button endpoints are also overwhelmingly likely to be a split.
                    return (hasAxisDevice && hasButtonOnlyDevice) || Devices.Count >= 3;
                }
            }
        }

        private void Awake()
        {
            _enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable automatic high-button controller detection and status reporting. Optional actions retain their own settings.");

            _minimumButtonCount = Config.Bind(
                "General",
                "MinimumButtonCount",
                33,
                "A controller must expose at least this many buttons to be considered high-button-count.");

            _directInputFallback = Config.Bind(
                "Compatibility",
                "TryDirectInputWhenExactly32Buttons",
                false,
                "Opt in to switching Rewired globally to DirectInput when a flight controller stops at exactly 32 buttons. This can change controller identities and bindings.");

            _autoAssign = Config.Bind(
                "Compatibility",
                "AutoAssignHighButtonControllers",
                false,
                "Opt in to assigning detected high-button controllers to player 0. Detection and reporting work without this.");

            _statusSeconds = Config.Bind(
                "Display",
                "StatusSeconds",
                15f,
                "How long the startup status is displayed. Set to 0 to disable it.");

            if (!_enabled.Value)
            {
                Logger.LogInfo("Disabled by configuration.");
                return;
            }

            StartCoroutine(InitializeWhenReady());
        }

        private IEnumerator InitializeWhenReady()
        {
            const float timeoutSeconds = 20f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!ReInput.isReady && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!ReInput.isReady)
            {
                SetStatus(
                    StatusKind.Error,
                    "Controller fix: Rewired did not initialize; see BepInEx log.");
                Logger.LogError("Rewired was not ready after " + timeoutSeconds + " seconds.");
                yield break;
            }

            ReInput.ControllerConnectedEvent += OnControllerChanged;
            ReInput.ControllerDisconnectedEvent += OnControllerChanged;

            yield return CheckAndRepair();
        }

        private void OnDestroy()
        {
            if (!ReInput.isReady)
                return;

            ReInput.ControllerConnectedEvent -= OnControllerChanged;
            ReInput.ControllerDisconnectedEvent -= OnControllerChanged;
        }

        private void OnControllerChanged(ControllerStatusChangedEventArgs args)
        {
            if (_checkScheduled || _checkInProgress)
                return;

            StartCoroutine(RecheckAfterDeviceChange());
        }

        private IEnumerator RecheckAfterDeviceChange()
        {
            _checkScheduled = true;
            yield return new WaitForSecondsRealtime(1f);
            yield return CheckAndRepair();
            _checkScheduled = false;
        }

        private IEnumerator CheckAndRepair()
        {
            if (_checkInProgress)
                yield break;

            _checkInProgress = true;

            List<ControllerGroup> groups = FindControllerGroups();
            LogInventory(groups);

            ControllerGroup splitGroup = groups.FirstOrDefault(group => group.LooksLikeSplitMode);
            if (splitGroup != null)
            {
                ReportSplitMode(splitGroup);
                _checkInProgress = false;
                yield break;
            }

            List<Joystick> ready = FindReadyControllers(groups);
            List<Joystick> truncatedCandidates = groups
                .Where(group => group.Devices.Count == 1)
                .Select(group => group.Devices[0])
                .Where(device => device.buttonCount == 32 && LooksLikeFlightController(device))
                .ToList();

            WindowsStandalonePrimaryInputSource inputSource =
                ReInput.configuration.windowsStandalonePrimaryInputSource;

            if (truncatedCandidates.Count == 0)
            {
                if (ready.Count > 0)
                {
                    AssignToPlayerZero(ready);
                    ReportSuccess(ready);
                }
                else
                {
                    SetStatus(
                        StatusKind.Info,
                        "Controller fix: no controller with more than 32 buttons detected.");
                    Logger.LogInfo("No high-button or suspicious 32-button joystick was detected.");
                }

                _checkInProgress = false;
                yield break;
            }

            if (!_directInputFallback.Value)
            {
                string disabledFallbackNames = JoinDeviceNames(truncatedCandidates);
                Logger.LogInfo(
                    disabledFallbackNames + " exposes exactly 32 buttons. The optional DirectInput " +
                    "fallback is disabled, so no input settings were changed.");

                if (ready.Count > 0)
                {
                    AssignToPlayerZero(ready);
                    ReportSuccess(ready);
                }
                else
                {
                    SetStatus(
                        StatusKind.Info,
                        disabledFallbackNames +
                        " — 32 buttons detected. Enable the DirectInput fallback only if more are expected.");
                }

                _checkInProgress = false;
                yield break;
            }

            if (_directInputAttempted ||
                inputSource == WindowsStandalonePrimaryInputSource.DirectInput)
            {
                ReportStillLimited(truncatedCandidates, inputSource);
                _checkInProgress = false;
                yield break;
            }

            _directInputAttempted = true;
            string candidateNames = JoinDeviceNames(truncatedCandidates);
            SetStatus(
                StatusKind.Info,
                "Controller fix: " + candidateNames +
                " stops at 32 buttons; trying DirectInput...");
            Logger.LogWarning(
                candidateNames + " exposed exactly 32 buttons through " + inputSource +
                ". Switching Rewired to DirectInput.");

            try
            {
                ReInput.configuration.windowsStandalonePrimaryInputSource =
                    WindowsStandalonePrimaryInputSource.DirectInput;
            }
            catch (Exception exception)
            {
                SetStatus(
                    StatusKind.Error,
                    "Controller fix: DirectInput switch failed; see BepInEx log.");
                Logger.LogError("Could not switch Rewired to DirectInput: " + exception);
                _checkInProgress = false;
                yield break;
            }

            yield return new WaitForSecondsRealtime(2f);

            groups = FindControllerGroups();
            LogInventory(groups);

            splitGroup = groups.FirstOrDefault(group => group.LooksLikeSplitMode);
            if (splitGroup != null)
            {
                ReportSplitMode(splitGroup);
                _checkInProgress = false;
                yield break;
            }

            ready = FindReadyControllers(groups);
            truncatedCandidates = groups
                .Where(group => group.Devices.Count == 1)
                .Select(group => group.Devices[0])
                .Where(device => device.buttonCount == 32 && LooksLikeFlightController(device))
                .ToList();

            if (truncatedCandidates.Count > 0)
            {
                ReportStillLimited(
                    truncatedCandidates,
                    WindowsStandalonePrimaryInputSource.DirectInput);
            }
            else if (ready.Count > 0)
            {
                AssignToPlayerZero(ready);
                ReportSuccess(ready);
            }
            else
            {
                SetStatus(
                    StatusKind.Info,
                    "Controller fix: no high-button controller found after DirectInput scan.");
                Logger.LogWarning("No high-button controller was found after switching to DirectInput.");
            }

            _checkInProgress = false;
        }

        private List<ControllerGroup> FindControllerGroups()
        {
            return ReInput.controllers.Joysticks
                .Where(device => device != null)
                .GroupBy(GetDisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(
                    group => new ControllerGroup
                    {
                        Name = group.Key,
                        Devices = group.OrderBy(device => device.id).ToList()
                    })
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<Joystick> FindReadyControllers(IEnumerable<ControllerGroup> groups)
        {
            int minimum = Math.Max(33, _minimumButtonCount.Value);
            return groups
                .SelectMany(group => group.Devices)
                .Where(device => device.buttonCount >= minimum)
                .OrderBy(device => GetDisplayName(device), StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.id)
                .ToList();
        }

        private static string GetDisplayName(Joystick device)
        {
            if (!string.IsNullOrWhiteSpace(device.name))
                return device.name.Trim();
            if (!string.IsNullOrWhiteSpace(device.hardwareName))
                return device.hardwareName.Trim();
            if (!string.IsNullOrWhiteSpace(device.hardwareIdentifier))
                return device.hardwareIdentifier.Trim();
            return "Unnamed joystick #" + device.id;
        }

        private static bool LooksLikeGamepad(Joystick device)
        {
            string identity = GetIdentityText(device);

            if (GamepadNameMarkers.Any(identity.Contains))
                return true;

            return device.Templates.Any(
                template => template != null &&
                template.name != null &&
                template.name.IndexOf("gamepad", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool LooksLikeFlightController(Joystick device)
        {
            if (LooksLikeGamepad(device))
                return false;

            string identity = GetIdentityText(device);
            if (FlightControllerNameMarkers.Any(identity.Contains))
                return true;

            bool hasFlightTemplate = device.Templates.Any(
                template =>
                {
                    if (template == null || template.name == null)
                        return false;

                    string name = template.name.ToLowerInvariant();
                    return name.Contains("flight") ||
                           name.Contains("hotas") ||
                           name.Contains("joystick") ||
                           name.Contains("throttle") ||
                           name.Contains("yoke");
                });

            // Unknown flight controls often lack a template and a descriptive
            // product name. Three or more axes plus exactly 32 buttons is a
            // useful fallback signal after known gamepads have been excluded.
            return hasFlightTemplate || device.axisCount >= 3;
        }

        private static string GetIdentityText(Joystick device)
        {
            return string.Join(
                " ",
                new[]
                {
                    device.name ?? string.Empty,
                    device.hardwareName ?? string.Empty,
                    device.hardwareIdentifier ?? string.Empty
                }).ToLowerInvariant();
        }

        private void AssignToPlayerZero(IEnumerable<Joystick> devices)
        {
            if (!_autoAssign.Value)
                return;

            try
            {
                Player player = ReInput.players.GetPlayer(0);
                if (player == null)
                    return;

                foreach (Joystick device in devices)
                {
                    if (player.controllers.ContainsController(device))
                        continue;

                    player.controllers.AddController(device, false);
                    Logger.LogInfo(
                        "Assigned \"" + GetDisplayName(device) +
                        "\" (id " + device.id + ") to player 0.");
                }
            }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    "Could not auto-assign high-button controller(s): " + exception.Message);
            }
        }

        private void ReportSplitMode(ControllerGroup group)
        {
            SetStatus(
                StatusKind.Warning,
                group.Name + ": " + group.Devices.Count +
                " split devices detected (" + group.TotalButtons +
                " buttons). Select its native/single-device mode.");
            Logger.LogWarning(
                "\"" + group.Name + "\" is presented as " + group.Devices.Count +
                " separate HID joysticks. Select the manufacturer's native, single-device, " +
                "or 128-button mode, then reconnect it or restart the game.");
        }

        private void ReportSuccess(IList<Joystick> devices)
        {
            WindowsStandalonePrimaryInputSource inputSource =
                ReInput.configuration.windowsStandalonePrimaryInputSource;

            string details = string.Join(
                "; ",
                devices.Select(
                    device => GetDisplayName(device) + " — " +
                    device.buttonCount + " buttons").ToArray());

            SetStatus(
                StatusKind.Success,
                devices.Count == 1
                    ? details + " ready (" + inputSource + ")."
                    : devices.Count + " high-button controllers ready: " +
                      details + " (" + inputSource + ").");

            foreach (Joystick device in devices)
            {
                Logger.LogInfo(
                    "High-button controller ready: \"" + GetDisplayName(device) +
                    "\", id=" + device.id +
                    ", axes=" + device.axisCount +
                    ", buttons=" + device.buttonCount +
                    ", inputSource=" + inputSource + ".");
            }
        }

        private void ReportStillLimited(
            IList<Joystick> devices,
            WindowsStandalonePrimaryInputSource inputSource)
        {
            string names = JoinDeviceNames(devices);
            SetStatus(
                StatusKind.Warning,
                names + ": still 32 buttons through " + inputSource +
                ". Check native mode or disable the fallback.");
            Logger.LogWarning(
                names + " remains limited to 32 buttons through " + inputSource +
                ". Check the manufacturer's native/high-button mode and disable Steam Input. " +
                "The DirectInput fallback can be disabled in the plugin configuration.");
        }

        private static string JoinDeviceNames(IEnumerable<Joystick> devices)
        {
            return string.Join(
                ", ",
                devices
                    .Select(GetDisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        private void LogInventory(IEnumerable<ControllerGroup> groups)
        {
            WindowsStandalonePrimaryInputSource inputSource =
                ReInput.configuration.windowsStandalonePrimaryInputSource;

            List<ControllerGroup> list = groups.ToList();
            Logger.LogInfo(
                "Rewired input source: " + inputSource +
                ". Joystick group count: " + list.Count + ".");

            foreach (ControllerGroup group in list)
            {
                foreach (Joystick device in group.Devices)
                {
                    Logger.LogInfo(
                        "Joystick: id=" + device.id +
                        ", name=\"" + GetDisplayName(device) +
                        "\", axes=" + device.axisCount +
                        ", buttons=" + device.buttonCount +
                        ", instanceGuid=" + device.deviceInstanceGuid + ".");
                }
            }
        }

        private void SetStatus(StatusKind kind, string text)
        {
            _statusKind = kind;
            _statusText = text;
            _statusUntil = Time.realtimeSinceStartup + Math.Max(0f, _statusSeconds.Value);
        }

        private void OnGUI()
        {
            if (_statusSeconds == null ||
                _statusSeconds.Value <= 0f ||
                string.IsNullOrEmpty(_statusText) ||
                Time.realtimeSinceStartup > _statusUntil)
            {
                return;
            }

            Color color;
            switch (_statusKind)
            {
                case StatusKind.Success:
                    color = new Color(0.25f, 0.85f, 0.35f, 1f);
                    break;
                case StatusKind.Warning:
                    color = new Color(1f, 0.75f, 0.2f, 1f);
                    break;
                case StatusKind.Error:
                    color = new Color(1f, 0.35f, 0.3f, 1f);
                    break;
                default:
                    color = Color.white;
                    break;
            }

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = color }
            };

            const float width = 920f;
            const float height = 68f;
            float actualWidth = Math.Min(width, Screen.width - 24f);
            float x = Math.Max(12f, (Screen.width - actualWidth) * 0.5f);
            GUI.Box(new Rect(x, 12f, actualWidth, height), _statusText, style);
        }
    }
}
