using System;
using System.Collections.Generic;
using Jotunn.Managers;
using Jotunn;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherAltar
{
    public static class AltarMenu
    {
        private const float PanelWidth = 480f;
        private const float PanelHeight = 400f;
        private const float MaxInteractDistance = 5f;
        private const float RefreshInterval = 0.25f;

        private static GameObject _panel;
        private static GameObject _currentWeatherText;
        private static GameObject _priceText;
        private static GameObject _statusText;
        private static GameObject _errorText;
        private static GameObject _offerButtonObject;
        private static readonly GameObject[] ChoiceButtons = new GameObject[4];

        private static WeatherAltarPiece _altar;
        private static WeatherKind _selected = WeatherKind.None;
        private static bool _inputBlocked;
        private static bool _awaitingServer;
        private static float _refreshTimer;

        public static bool IsOpen => _panel != null && _panel.activeSelf;

        public static void Open(WeatherAltarPiece altar)
        {
            if (IsOpen)
            {
                return;
            }

            if (GUIManager.Instance == null)
            {
                Jotunn.Logger.LogWarning("Weather Altar: GUIManager not ready.");
                return;
            }

            if (!GUIManager.CustomGUIFront)
            {
                Jotunn.Logger.LogWarning("Weather Altar: CustomGUIFront not available.");
                return;
            }

            _altar = altar;
            _awaitingServer = false;
            _panel = GUIManager.Instance.CreateWoodpanel(
                parent: GUIManager.CustomGUIFront.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0f, 0f),
                width: PanelWidth,
                height: PanelHeight,
                draggable: false);
            _panel.SetActive(true);

            GUIManager.Instance.CreateText(
                text: Localization.instance.Localize("$piece_mous_weatheraltar"),
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -30f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 28,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: PanelWidth - 40f,
                height: 36f,
                addContentSizeFitter: false);

            _currentWeatherText = GUIManager.Instance.CreateText(
                text: "Weather: natural",
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -70f),
                font: GUIManager.Instance.AveriaSerif,
                fontSize: 18,
                color: Color.white,
                outline: false,
                outlineColor: Color.black,
                width: PanelWidth - 60f,
                height: 24f,
                addContentSizeFitter: false);

            _priceText = GUIManager.Instance.CreateText(
                text: "",
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -100f),
                font: GUIManager.Instance.AveriaSerif,
                fontSize: 18,
                color: Color.white,
                outline: false,
                outlineColor: Color.black,
                width: PanelWidth - 60f,
                height: 24f,
                addContentSizeFitter: false);

            CreateChoiceButton(WeatherKind.Clear, "Clear", -140f);
            CreateChoiceButton(WeatherKind.Rain, "Rain", -180f);
            CreateChoiceButton(WeatherKind.Storm, "Storm", -220f);

            _errorText = GUIManager.Instance.CreateText(
                text: "",
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(0f, 110f),
                font: GUIManager.Instance.AveriaSerif,
                fontSize: 15,
                color: new Color(1f, 0.55f, 0.35f),
                outline: false,
                outlineColor: Color.black,
                width: PanelWidth - 60f,
                height: 28f,
                addContentSizeFitter: false);

            _statusText = GUIManager.Instance.CreateText(
                text: "",
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(0f, 80f),
                font: GUIManager.Instance.AveriaSerif,
                fontSize: 16,
                color: Color.white,
                outline: false,
                outlineColor: Color.black,
                width: PanelWidth - 60f,
                height: 20f,
                addContentSizeFitter: false);

            _offerButtonObject = GUIManager.Instance.CreateButton(
                text: "Offer",
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(-90f, 50f),
                width: 150f,
                height: 40f);
            _offerButtonObject.GetComponent<Button>().onClick.AddListener(OnOfferClicked);

            var cancelButton = GUIManager.Instance.CreateButton(
                text: "Cancel",
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(90f, 50f),
                width: 150f,
                height: 40f);
            cancelButton.GetComponent<Button>().onClick.AddListener(Close);

            var infoText = GUIManager.Instance.CreateText(
                text: Localization.instance.Localize("$mous_weatheraltar_hint"),
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(0f, 12f),
                font: GUIManager.Instance.AveriaSerif,
                fontSize: 13,
                color: new Color(0.9f, 0.85f, 0.75f),
                outline: false,
                outlineColor: Color.black,
                width: PanelWidth - 60f,
                height: 18f,
                addContentSizeFitter: false);

            _selected = WeatherService.IsSupported((byte)WeatherKind.Clear) ? WeatherKind.Clear : FirstSupported();
            if (_selected == WeatherKind.None)
            {
                SetErrorText(Localization.instance.Localize("$mous_weatheraltar_unavailable"));
            }

            GUIManager.BlockInput(true);
            _inputBlocked = true;
            if (!WeatherService.EverReceivedState && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                OfferingRpc.RequestState();
            }

            RefreshLabels();
        }

        private static void CreateChoiceButton(WeatherKind kind, string label, float y)
        {
            var buttonObject = GUIManager.Instance.CreateButton(
                text: label,
                parent: _panel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, y),
                width: 200f,
                height: 32f);
            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => Select(kind));
            ChoiceButtons[(int)kind] = buttonObject;
        }

        private static WeatherKind FirstSupported()
        {
            for (byte kind = (byte)WeatherKind.Clear; kind <= (byte)WeatherKind.Storm; kind++)
            {
                if (WeatherService.IsSupported(kind))
                {
                    return (WeatherKind)kind;
                }
            }
            return WeatherKind.None;
        }

        private static void Select(WeatherKind kind)
        {
            if (WeatherService.IsSupported((byte)kind))
            {
                _selected = kind;
                _awaitingServer = false;
                RefreshLabels();
            }
        }

        private static void OnOfferClicked()
        {
            if (_altar == null || _selected == WeatherKind.None || _awaitingServer)
            {
                return;
            }

            if (WeatherAltarPiece.GetDistance(_altar.GetZDOID()) > MaxInteractDistance)
            {
                SetErrorText(Localization.instance.Localize("$mous_weatheraltar_toofar"));
                return;
            }

            OfferingRpc.RequestOffering(_altar, _selected);
            _awaitingServer = true;
            RefreshLabels();
        }

        public static void OnFailure(OfferingFailure failure)
        {
            _awaitingServer = false;
            if (IsOpen)
            {
                SetErrorText(failure);
            }
        }

        public static void OnReceipt(bool accepted)
        {
            _awaitingServer = false;
            if (IsOpen && accepted)
            {
                Close();
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$mous_weatheraltar_accepted");
            }
        }

        private static void SetErrorText(OfferingFailure failure)
        {
            switch (failure)
            {
                case OfferingFailure.NotEnoughCoins:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_needcoins", AltarSettings.CoinCost.Value.ToString()));
                    break;
                case OfferingFailure.NotEnoughHealth:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_needhealth", (AltarSettings.HealthCost.Value + 1f).ToString("0")));
                    break;
                case OfferingFailure.NoAccess:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_noaccess"));
                    break;
                case OfferingFailure.TooFar:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_toofar"));
                    break;
                case OfferingFailure.Unavailable:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_unavailable"));
                    break;
                case OfferingFailure.PriceChanged:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_pricechanged"));
                    RefreshLabels();
                    break;
                case OfferingFailure.Busy:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_busy"));
                    break;
                case OfferingFailure.PaymentFailed:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_paymentfailed"));
                    break;
                default:
                    SetErrorText(Localization.instance.Localize("$mous_weatheraltar_invalid"));
                    break;
            }
        }

        private static void SetErrorText(string message)
        {
            if (_errorText != null)
            {
                var text = _errorText.GetComponent<Text>();
                if (text != null)
                {
                    text.text = message;
                }
            }
        }

        public static void Close()
        {
            if (_panel == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_panel);
            _panel = null;
            _altar = null;
            _awaitingServer = false;
            if (_inputBlocked)
            {
                GUIManager.BlockInput(false);
                _inputBlocked = false;
            }
        }

        public static void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer >= RefreshInterval)
            {
                _refreshTimer = 0f;
                RefreshLabels();
            }

            if (_altar == null || _altar.gameObject == null)
            {
                Close();
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead())
            {
                Close();
                return;
            }

            if (_altar != null && _altar.gameObject != null)
            {
                float distance = WeatherAltarPiece.GetDistance(_altar.GetZDOID());
                if (distance > MaxInteractDistance)
                {
                    Close();
                    return;
                }
            }
        }

        public static void RefreshLabels()
        {
            if (_panel == null)
            {
                return;
            }

            RefreshChoiceLabels();
            RefreshPriceLabel();
            RefreshCurrentWeatherLabel();
            RefreshStatusLabel();
        }

        private static void RefreshChoiceLabels()
        {
            for (byte kind = (byte)WeatherKind.Clear; kind <= (byte)WeatherKind.Storm; kind++)
            {
                var buttonObject = ChoiceButtons[kind];
                if (buttonObject == null)
                {
                    continue;
                }

                bool supported = WeatherService.IsSupported(kind);
                var button = buttonObject.GetComponent<Button>();
                button.interactable = supported;
                var colors = button.colors;
                colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                button.colors = colors;

                var text = buttonObject.GetComponentInChildren<Text>();
                if (text != null)
                {
                    string label = (WeatherKind)kind == WeatherKind.Clear ? "Clear" : (WeatherKind)kind == WeatherKind.Rain ? "Rain" : "Storm";
                    bool isSelected = (WeatherKind)kind == _selected;
                    if (supported)
                    {
                        text.text = isSelected ? "» " + label + " «" : label;
                        text.color = isSelected ? GUIManager.Instance.ValheimOrange : Color.white;
                    }
                    else
                    {
                        text.text = label + " (unavailable)";
                        text.color = new Color(0.55f, 0.55f, 0.55f, 0.7f);
                    }
                }

                if (button.image != null)
                {
                    button.image.color = supported && (WeatherKind)kind == _selected
                        ? new Color(1f, 0.72f, 0.35f)
                        : Color.white;
                }
            }
        }

        private static void RefreshPriceLabel()
        {
            if (_priceText == null)
            {
                return;
            }

            int coinCost = AltarSettings.CoinCost.Value;
            float healthCost = AltarSettings.HealthCost.Value;
            int duration = AltarSettings.DurationSeconds.Value;
            var text = _priceText.GetComponent<Text>();
            if (text != null)
            {
                text.text = $"{coinCost} Coins + {healthCost:0} HP for {duration}s";
            }
        }

        private static void RefreshCurrentWeatherLabel()
        {
            if (_currentWeatherText == null)
            {
                return;
            }

            var text = _currentWeatherText.GetComponent<Text>();
            if (text == null)
            {
                return;
            }

            if (!WeatherService.StateKnown)
            {
                text.text = "Weather: waiting for server...";
                return;
            }

            if (WeatherService.HasActive)
            {
                float remaining = WeatherService.RemainingSeconds();
                int minutes = (int)(remaining / 60f);
                int seconds = Mathf.CeilToInt(remaining % 60f);
                text.text = $"Weather: {WeatherService.Kind} ({minutes}:{seconds:00})";
            }
            else
            {
                text.text = "Weather: natural";
            }
        }

        private static void RefreshStatusLabel()
        {
            if (_statusText == null)
            {
                return;
            }

            var text = _statusText.GetComponent<Text>();
            if (text == null)
            {
                return;
            }

            if (_awaitingServer)
            {
                text.text = "Awaiting server...";
            }
            else
            {
                text.text = "";
            }
        }

        public static void Shutdown()
        {
            Close();
        }

    }
}