using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn;
using UnityEngine;

namespace WeatherAltar
{
    public class WeatherAltarPiece : MonoBehaviour, Interactable, Hoverable
    {
        public const string PrefabName = "Mous_WeatherAltar";
        private const string BasePrefabName = "guard_stone";

        public static int PrefabHash { get; private set; } = -1;

        private float _hoverOffset;

        private static System.Action _vanillaPrefabsHandler;

        private static float _templateHoverOffset;
        private static bool _registered;
        public static bool LocalServerRefuseRequests { get; private set; }

        public static void Register()
        {
            _vanillaPrefabsHandler = () => OnVanillaPrefabsAvailable();
            PrefabManager.OnVanillaPrefabsAvailable += _vanillaPrefabsHandler;
        }

        private static void OnVanillaPrefabsAvailable()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= _vanillaPrefabsHandler;
            _vanillaPrefabsHandler = null;

            var config = new PieceConfig
            {
                Name = "$piece_mous_weatheraltar",
                Description = "$piece_mous_weatheraltar_desc",
                PieceTable = "Hammer",
                AllowedInDungeons = false,
            };

            var customPiece = new CustomPiece(PrefabName, BasePrefabName, config);
            GameObject prefab = customPiece.PiecePrefab;
            if (prefab == null)
            {
                Jotunn.Logger.LogError("Weather Altar: failed to clone guard_stone. Offerings are disabled.");
                LocalServerRefuseRequests = true;
                return;
            }

            var privateArea = prefab.GetComponent<PrivateArea>();
            if (privateArea == null)
            {
                Jotunn.Logger.LogError("Weather Altar: cloned prefab lacks PrivateArea. Offerings are disabled.");
                LocalServerRefuseRequests = true;
                return;
            }

            var piece = prefab.GetComponent<Piece>();
            var wearNTear = prefab.GetComponent<WearNTear>();
            var zNetView = prefab.GetComponent<ZNetView>();
            if (piece == null || wearNTear == null || zNetView == null)
            {
                Jotunn.Logger.LogError("Weather Altar: cloned prefab lacks Piece/WearNTear/ZNetView. Offerings are disabled.");
                LocalServerRefuseRequests = true;
                return;
            }

            PrefabHash = PrefabName.GetStableHashCode();
            _templateHoverOffset = privateArea.m_hoverOffset;

            // Ward-only visual objects referenced by PrivateArea; disable on the clone before removing the component.
            if (privateArea.m_enabledEffect != null)
            {
                privateArea.m_enabledEffect.SetActive(false);
            }
            if (privateArea.m_areaMarker != null && privateArea.m_areaMarker.gameObject != null)
            {
                privateArea.m_areaMarker.gameObject.SetActive(false);
            }
            if (privateArea.m_connectEffect != null)
            {
                privateArea.m_connectEffect.SetActive(false);
            }
            if (privateArea.m_inRangeEffect != null)
            {
                privateArea.m_inRangeEffect.SetActive(false);
            }

            Object.DestroyImmediate(privateArea);

            var altar = prefab.AddComponent<WeatherAltarPiece>();
            altar._hoverOffset = _templateHoverOffset;

            PieceManager.Instance.AddPiece(customPiece);
            _registered = true;

            LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_mous_weatheraltar", "Weather Altar" },
                { "piece_mous_weatheraltar_desc", "Offer coins and blood to change the weather for everyone." },
                { "mous_weatheraltar_prompt", "Make an offering" },
                { "mous_weatheraltar_hint", "Affects outdoor weather worldwide. Events and interiors take priority." },
                { "mous_weatheraltar_noaccess", "You do not have access." },
                { "mous_weatheraltar_toofar", "Too far away." },
                { "mous_weatheraltar_unavailable", "This weather is unavailable." },
                { "mous_weatheraltar_needcoins", "Not enough coins ({0} needed)." },
                { "mous_weatheraltar_needhealth", "You must have at least {0} HP." },
                { "mous_weatheraltar_pricechanged", "Price changed - please confirm again." },
                { "mous_weatheraltar_busy", "Another offering is already in progress." },
                { "mous_weatheraltar_paymentfailed", "Offering failed." },
                { "mous_weatheraltar_invalid", "Offering rejected." },
                { "mous_weatheraltar_accepted", "Offering accepted." },
            });

            Jotunn.Logger.LogInfo("Weather Altar: registered piece from guard_stone clone.");
        }

        private ZNetView _nview;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
        }

        public ZDOID GetZDOID()
        {
            return _nview?.GetZDO()?.m_uid ?? ZDOID.None;
        }

        public static float GetDistance(ZDOID altarId)
        {
            Player player = Player.m_localPlayer;
            ZDO zdo = ZDOMan.instance?.GetZDO(altarId);
            if (player == null || zdo == null)
            {
                return float.MaxValue;
            }
            return Vector3.Distance(player.transform.position, zdo.GetPosition());
        }

        public static bool CheckAccess(ZDOID altarId)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(altarId);
            if (zdo == null)
            {
                return false;
            }
            return PrivateArea.CheckAccess(zdo.GetPosition());
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
            {
                return false;
            }

            Player player = user as Player;
            if (player == null || player != Player.m_localPlayer)
            {
                return false;
            }

            if (!_registered || LocalServerRefuseRequests || _nview == null || !_nview.IsValid())
            {
                return false;
            }

            ZDO zdo = _nview.GetZDO();
            if (zdo == null || zdo.GetPrefab() != PrefabHash)
            {
                return false;
            }

            if (Vector3.Distance(player.transform.position, zdo.GetPosition()) > player.m_maxInteractDistance)
            {
                return false;
            }

            if (!PrivateArea.CheckAccess(zdo.GetPosition()))
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$mous_weatheraltar_noaccess");
                return true;
            }

            if (!WeatherService.StateKnown)
            {
                OfferingRpc.RequestState();
                return true;
            }

            AltarMenu.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverName() => Localization.instance.Localize("$piece_mous_weatheraltar");

        public string GetHoverText()
        {
            return Localization.instance.Localize($"$piece_mous_weatheraltar\n[<color=yellow><b>$KEY_Use</b></color>] $mous_weatheraltar_prompt");
        }

        public float GetHoverOffset() => _hoverOffset;
    }
}