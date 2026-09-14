using System;
using System.Collections;
using System.Collections.Generic;
using Jotunn;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace WeatherAltar
{
    public enum OfferingFailure : byte
    {
        None = 0,
        Unavailable = 1,
        InvalidRequest = 2,
        TooFar = 3,
        NoAccess = 4,
        NotEnoughCoins = 5,
        NotEnoughHealth = 6,
        PriceChanged = 7,
        Busy = 8,
        PaymentFailed = 9,
    }

    public static class OfferingRpc
    {
        private const long NoPeer = 0L;

        private const string RequestStateName = "RequestState";
        private const string OfferName = "Offer";
        private const string QuoteName = "Quote";
        private const string PaymentName = "Payment";
        private const string StateName = "State";
        private const string RejectName = "Reject";

        private static CustomRPC _requestState;
        private static CustomRPC _offer;
        private static CustomRPC _quote;
        private static CustomRPC _payment;
        private static CustomRPC _state;
        private static CustomRPC _reject;

        private sealed class ServerRequest
        {
            public long RequestId;
            public ZDOID AltarId;
            public WeatherKind Kind;
            public int CoinCost;
            public float HealthCost;
            public int DurationSeconds;
        }

        private sealed class CompletedRequest
        {
            public long RequestId;
        }

        private static readonly Dictionary<long, ServerRequest> PendingByPeer = new Dictionary<long, ServerRequest>();
        private static readonly Dictionary<long, CompletedRequest> CompletedByPeer = new Dictionary<long, CompletedRequest>();

        // client-side pending request
        private static long _nextRequestId = 1;
        private static long _pendingRequestId;
        private static ZDOID _pendingAltarId = ZDOID.None;
        private static bool _outcomeRecorded;
        private static bool _paid;
        private static OfferingFailure _failure = OfferingFailure.None;

        public static void Register()
        {
            NetworkManager nm = NetworkManager.Instance;
            _requestState = nm.AddRPC(RequestStateName, OnRequestStateServer, OnRequestStateClient);
            _offer = nm.AddRPC(OfferName, OnOfferServer, OnOfferClient);
            _quote = nm.AddRPC(QuoteName, OnQuoteServer, OnQuoteClient);
            _payment = nm.AddRPC(PaymentName, OnPaymentServer, OnPaymentClient);
            _state = nm.AddRPC(StateName, OnStateServer, OnStateClient);
            _reject = nm.AddRPC(RejectName, OnRejectServer, OnRejectClient);
        }

        public static void Shutdown()
        {
            PendingByPeer.Clear();
            CompletedByPeer.Clear();
            _pendingRequestId = 0L;
            _outcomeRecorded = false;
            _paid = false;
            _failure = OfferingFailure.None;
        }

        // --- packet helpers ------------------------------------------------------
        // Envelope: every packet starts with a single long worldUid. The routed
        // RPC already identifies the sender; targeted payloads identify the
        // recipient by their own fields where needed.

        private static long ServerTarget()
        {
            if (ZNet.instance == null)
            {
                return 0L;
            }

            if (ZNet.instance.IsServer())
            {
                return ZNet.GetUID();
            }

            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            return serverPeer != null ? serverPeer.m_uid : 0L;
        }

        private static bool IsLocal(long target) => ZNet.instance != null && target == ZNet.GetUID();

        private static bool IsServerOrigin(long sender)
        {
            if (sender == ServerTarget() || sender == ZNet.GetUID())
            {
                return true;
            }

            Jotunn.Logger.LogWarning($"Weather Altar: ignored packet from non-server peer {sender} (server is {ServerTarget()}).");
            return false;
        }

        private static ZPackage Rewind(ZPackage package)
        {
            var copy = new ZPackage(package.GetArray());
            copy.SetPos(0);
            return copy;
        }

        private static bool TryReadEnvelope(ZPackage package, out long worldUid)
        {
            worldUid = 0L;
            if (package == null || ZNet.instance == null)
            {
                return false;
            }

            try
            {
                worldUid = package.ReadLong();
                if (worldUid == ZNet.instance.GetWorldUID())
                {
                    return true;
                }

                Jotunn.Logger.LogWarning($"Weather Altar: ignored packet for world {worldUid} (this world is {ZNet.instance.GetWorldUID()}).");
                return false;
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Weather Altar: ignored malformed packet: {e.Message}");
                return false;
            }
        }

        private static void SendEnvelopeToServer(CustomRPC rpc, Action<ZPackage> writeBody)
        {
            if (ZNet.instance == null)
            {
                return;
            }

            var package = new ZPackage();
            package.Write(ZNet.instance.GetWorldUID());
            writeBody?.Invoke(package);
            long target = ServerTarget();
            if (IsLocal(target))
            {
                // Host/solo path: invoke the same server handler directly.
                InvokeServerHandler(rpc, ZNet.GetUID(), Rewind(package));
            }
            else
            {
                rpc.SendPackage(target, package);
            }
        }

        private static void InvokeServerHandler(CustomRPC rpc, long sender, ZPackage package)
        {
            switch (rpc.Name)
            {
                case RequestStateName: HandleRequestStateServer(sender, package); break;
                case OfferName: HandleOfferServer(sender, package); break;
                case PaymentName: HandlePaymentServer(sender, package); break;
            }
        }

        private static void InvokeClientHandler(CustomRPC rpc, long sender, ZPackage package)
        {
            switch (rpc.Name)
            {
                case QuoteName: HandleQuoteClient(sender, package); break;
                case RejectName: HandleRejectClient(sender, package); break;
                case StateName: HandleStateClient(sender, package); break;
            }
        }

        private static void SendToClient(long recipient, CustomRPC rpc, ZPackage package)
        {
            if (IsLocal(recipient))
            {
                InvokeClientHandler(rpc, recipient, Rewind(package));
                return;
            }
            rpc.SendPackage(recipient, package);
        }

        // --- client request API -------------------------------------------------

        public static void RequestState()
        {
            if (ZNet.instance == null)
            {
                return;
            }

            SendEnvelopeToServer(_requestState, null);
        }

        public static void RequestOffering(WeatherAltarPiece altar, WeatherKind kind)
        {
            if (ZNet.instance == null)
            {
                return;
            }

            if (_pendingRequestId != 0L && !_outcomeRecorded)
            {
                Jotunn.Logger.LogWarning("Weather Altar: a request is already pending.");
                return;
            }

            _pendingRequestId = _nextRequestId++;
            _pendingAltarId = altar.GetZDOID();
            _outcomeRecorded = false;
            _paid = false;
            _failure = OfferingFailure.None;

            SendEnvelopeToServer(_offer, package =>
            {
                package.Write(_pendingRequestId);
                package.Write(_pendingAltarId);
                package.Write((byte)kind);
                package.Write(AltarSettings.CoinCost.Value);
                package.Write(AltarSettings.HealthCost.Value);
                package.Write(AltarSettings.DurationSeconds.Value);
            });
        }

        // --- server handlers (synchronous logic; coroutine wrappers below) ------

        private static void HandleRequestStateServer(long sender, ZPackage package)
        {
            if (!TryReadEnvelope(package, out _))
            {
                return;
            }

            SendStateSnapshot(sender, NoPeer, 0L);
        }

        private static void HandleOfferServer(long sender, ZPackage package)
        {
            if (!TryReadEnvelope(package, out _))
            {
                return;
            }

            long requestId;
            ZDOID altarId;
            byte kindByte;
            int displayedCoinCost;
            float displayedHealthCost;
            int displayedDuration;
            try
            {
                requestId = package.ReadLong();
                altarId = package.ReadZDOID();
                kindByte = package.ReadByte();
                displayedCoinCost = package.ReadInt();
                displayedHealthCost = package.ReadSingle();
                displayedDuration = package.ReadInt();
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Weather Altar: failed to decode Offer: {e.Message}");
                return;
            }

            if (float.IsNaN(displayedHealthCost) || float.IsInfinity(displayedHealthCost))
            {
                RejectRequest(sender, requestId, OfferingFailure.InvalidRequest);
                return;
            }

            ServerRequest pending;
            if (PendingByPeer.TryGetValue(sender, out pending))
            {
                if (pending.RequestId == requestId)
                {
                    // Same pending request: resend the existing quote, never create a second transaction.
                    SendQuote(sender, pending);
                }
                else
                {
                    RejectRequest(sender, requestId, OfferingFailure.Busy);
                }
                return;
            }

            WeatherKind kind;
            if (!WeatherService.TryGetSupportedKind(kindByte, out kind))
            {
                RejectRequest(sender, requestId, OfferingFailure.Unavailable);
                return;
            }

            ZDO altarZdo = ZDOMan.instance?.GetZDO(altarId);
            if (altarZdo == null || altarZdo.GetPrefab() != WeatherAltarPiece.PrefabHash)
            {
                RejectRequest(sender, requestId, OfferingFailure.InvalidRequest);
                return;
            }

            Vector3 altarPosition = altarZdo.GetPosition();
            ZDO playerZdo = GetPlayerZdo(sender);
            if (playerZdo == null || playerZdo.GetOwner() != sender ||
                Vector3.Distance(playerZdo.GetPosition(), altarPosition) > 7f)
            {
                RejectRequest(sender, requestId, OfferingFailure.TooFar);
                return;
            }

            if (displayedCoinCost != AltarSettings.CoinCost.Value ||
                Math.Abs(displayedHealthCost - AltarSettings.HealthCost.Value) > 0.01f ||
                displayedDuration != AltarSettings.DurationSeconds.Value)
            {
                RejectRequest(sender, requestId, OfferingFailure.PriceChanged);
                return;
            }

            PendingByPeer[sender] = new ServerRequest
            {
                RequestId = requestId,
                AltarId = altarId,
                Kind = kind,
                CoinCost = AltarSettings.CoinCost.Value,
                HealthCost = AltarSettings.HealthCost.Value,
                DurationSeconds = AltarSettings.DurationSeconds.Value,
            };
            SendQuote(sender, PendingByPeer[sender]);
        }

        private static ZDO GetPlayerZdo(long senderUid)
        {
            if (senderUid == ZNet.GetUID())
            {
                Player player = Player.m_localPlayer;
                ZDOID zdoid = player != null ? player.GetZDOID() : ZDOID.None;
                return zdoid != ZDOID.None ? ZDOMan.instance?.GetZDO(zdoid) : null;
            }

            ZNetPeer peer = ZNet.instance?.GetPeer(senderUid);
            if (peer == null)
            {
                return null;
            }

            return ZDOMan.instance?.GetZDO(peer.m_characterID);
        }

        private static void HandlePaymentServer(long sender, ZPackage package)
        {
            if (!TryReadEnvelope(package, out _))
            {
                return;
            }

            long requestId;
            bool paid;
            byte failureByte;
            try
            {
                requestId = package.ReadLong();
                paid = package.ReadBool();
                failureByte = package.ReadByte();
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Weather Altar: failed to decode Payment: {e.Message}");
                return;
            }

            ServerRequest request;
            if (PendingByPeer.TryGetValue(sender, out request) && request.RequestId == requestId)
            {
                PendingByPeer.Remove(sender);
            }
            else if (CompletedByPeer.TryGetValue(sender, out var completed) && completed.RequestId == requestId)
            {
                // Duplicate completed payment: resend the receipt without recharging.
                SendStateSnapshot(sender, sender, requestId);
                return;
            }
            else
            {
                RejectRequest(sender, requestId, OfferingFailure.InvalidRequest);
                return;
            }

            if (paid && failureByte == (byte)OfferingFailure.None)
            {
                WeatherService.Commit(request.Kind, request.DurationSeconds);
                CompletedByPeer[sender] = new CompletedRequest { RequestId = requestId };
                BroadcastStateToClients(sender, requestId);
            }
            else
            {
                OfferingFailure failure = Enum.IsDefined(typeof(OfferingFailure), failureByte)
                    ? (OfferingFailure)failureByte
                    : OfferingFailure.PaymentFailed;
                RejectRequest(sender, requestId, failure);
            }
        }

        // --- state broadcasting ---------------------------------------------------

        private static void SendStateSnapshot(long recipient, long receiptPeer, long receiptRequest)
        {
            var package = new ZPackage();
            package.Write(ZNet.instance.GetWorldUID());
            package.Write(WeatherService.Revision);
            package.Write((byte)WeatherService.Kind);
            package.Write(WeatherService.RemainingSeconds());
            package.Write(WeatherService.SupportedMask);
            package.Write(receiptPeer);
            package.Write(receiptRequest);
            SendToClient(recipient, _state, package);
        }

        private static void BroadcastStateToClients(long receiptPeer, long receiptRequest)
        {
            SendStateSnapshot(ZNet.GetUID(), receiptPeer, receiptRequest);

            var package = new ZPackage();
            package.Write(ZNet.instance.GetWorldUID());
            package.Write(WeatherService.Revision);
            package.Write((byte)WeatherService.Kind);
            package.Write(WeatherService.RemainingSeconds());
            package.Write(WeatherService.SupportedMask);
            package.Write(receiptPeer);
            package.Write(receiptRequest);
            _state.SendPackage(ZRoutedRpc.Everybody, package);
        }

        public static void BroadcastExpiry()
        {
            BroadcastStateToClients(NoPeer, 0L);
        }

        private static void SendQuote(long recipient, ServerRequest request)
        {
            var package = new ZPackage();
            package.Write(ZNet.instance.GetWorldUID());
            package.Write(request.RequestId);
            package.Write(request.AltarId);
            package.Write((byte)request.Kind);
            package.Write(request.CoinCost);
            package.Write(request.HealthCost);
            package.Write(request.DurationSeconds);
            SendToClient(recipient, _quote, package);
        }

        private static void RejectRequest(long recipient, long requestId, OfferingFailure failure)
        {
            var package = new ZPackage();
            package.Write(ZNet.instance.GetWorldUID());
            package.Write(requestId);
            package.Write((byte)failure);
            SendToClient(recipient, _reject, package);
        }

        // --- client handlers (synchronous logic) -----------------------------------

        private static void HandleQuoteClient(long sender, ZPackage package)
        {
            if (!IsServerOrigin(sender) || !TryReadEnvelope(package, out _))
            {
                return;
            }

            long requestId;
            ZDOID altarId;
            byte kindByte;
            int coinCost;
            float healthCost;
            int durationSeconds;
            try
            {
                requestId = package.ReadLong();
                altarId = package.ReadZDOID();
                kindByte = package.ReadByte();
                coinCost = package.ReadInt();
                healthCost = package.ReadSingle();
                durationSeconds = package.ReadInt();
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Weather Altar: failed to decode Quote: {e.Message}");
                return;
            }

            if (requestId != _pendingRequestId)
            {
                return;
            }

            if (_outcomeRecorded)
            {
                // Duplicate quote: resend the recorded outcome, never pay twice.
                SendPayment(requestId, _paid, _failure);
                return;
            }

            WeatherKind kind;
            if (!WeatherService.TryGetSupportedKind(kindByte, out kind) || coinCost <= 0 ||
                healthCost < 0f || float.IsNaN(healthCost) || float.IsInfinity(healthCost) || durationSeconds <= 0)
            {
                RecordOutcome(false, OfferingFailure.InvalidRequest);
                SendPayment(requestId, false, OfferingFailure.InvalidRequest);
                return;
            }

            OfferingFailure localFailure = ValidateLocalPayment();
            if (localFailure != OfferingFailure.None)
            {
                RecordOutcome(false, localFailure);
                SendPayment(requestId, false, localFailure);
                return;
            }

            PayLocally(coinCost, healthCost);
            RecordOutcome(true, OfferingFailure.None);
            SendPayment(requestId, true, OfferingFailure.None);
        }

        private static OfferingFailure ValidateLocalPayment()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead())
            {
                return OfferingFailure.InvalidRequest;
            }

            if (_pendingAltarId == ZDOID.None)
            {
                return OfferingFailure.InvalidRequest;
            }

            if (WeatherAltarPiece.GetDistance(_pendingAltarId) > 5f)
            {
                return OfferingFailure.TooFar;
            }

            if (!WeatherAltarPiece.CheckAccess(_pendingAltarId))
            {
                return OfferingFailure.NoAccess;
            }

            int coins = CountCoins();
            if (coins < AltarSettings.CoinCost.Value)
            {
                return OfferingFailure.NotEnoughCoins;
            }

            float healthCost = AltarSettings.HealthCost.Value;
            if (player.GetHealth() - healthCost < 1f)
            {
                return OfferingFailure.NotEnoughHealth;
            }

            return OfferingFailure.None;
        }

        private static int CountCoins()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return 0;
            }

            string coinName = GetCoinName();
            return coinName == null ? 0 : player.GetInventory().CountItems(coinName, -1, true);
        }

        private static void PayLocally(int coinCost, float healthCost)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            string coinName = GetCoinName();
            if (coinName != null)
            {
                player.GetInventory().RemoveItem(coinName, coinCost, -1, true);
            }

            player.SetHealth(Mathf.Max(1f, player.GetHealth() - healthCost));
            Hud.instance?.DamageFlash();
        }

        private static string GetCoinName()
        {
            GameObject prefab = ObjectDB.instance?.GetItemPrefab("Coins");
            var itemDrop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
            return itemDrop == null ? null : itemDrop.m_itemData.m_shared.m_name;
        }

        private static void RecordOutcome(bool paid, OfferingFailure failure)
        {
            _paid = paid;
            _failure = failure;
            _outcomeRecorded = true;
        }

        private static void SendPayment(long requestId, bool paid, OfferingFailure failure)
        {
            SendEnvelopeToServer(_payment, package =>
            {
                package.Write(requestId);
                package.Write(paid);
                package.Write((byte)failure);
            });
        }

        private static void HandleStateClient(long sender, ZPackage package)
        {
            if (!IsServerOrigin(sender) || !TryReadEnvelope(package, out _))
            {
                return;
            }

            long revision;
            byte kindByte;
            float remainingSeconds;
            byte supportedMask;
            long receiptPeer;
            long receiptRequest;
            try
            {
                revision = package.ReadLong();
                kindByte = package.ReadByte();
                remainingSeconds = package.ReadSingle();
                supportedMask = package.ReadByte();
                receiptPeer = package.ReadLong();
                receiptRequest = package.ReadLong();
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Weather Altar: failed to decode State: {e.Message}");
                return;
            }

            // The client has normally already recorded its own outcome (it paid before
            // reporting), so the receipt must not depend on _outcomeRecorded.
            bool isReceipt = _pendingRequestId != 0L && receiptPeer == ZNet.GetUID() && receiptRequest == _pendingRequestId;
            if (isReceipt)
            {
                RecordOutcome(true, OfferingFailure.None);
            }

            if (!WeatherService.TryGetSupportedKind(kindByte, out WeatherKind kind))
            {
                kind = WeatherKind.None;
            }

            if (revision >= WeatherService.Revision || !WeatherService.EverReceivedState)
            {
                WeatherService.ApplySnapshot(revision, kind, remainingSeconds);
                WeatherService.SupportedMask = supportedMask;
            }

            if (isReceipt)
            {
                _pendingRequestId = 0L;
                _pendingAltarId = ZDOID.None;
                AltarMenu.OnReceipt(true);
            }
        }

        private static void HandleRejectClient(long sender, ZPackage package)
        {
            if (!IsServerOrigin(sender) || !TryReadEnvelope(package, out _))
            {
                return;
            }

            long requestId;
            byte failureByte;
            try
            {
                requestId = package.ReadLong();
                failureByte = package.ReadByte();
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Weather Altar: failed to decode Reject: {e.Message}");
                return;
            }

            OfferingFailure failure = Enum.IsDefined(typeof(OfferingFailure), failureByte)
                ? (OfferingFailure)failureByte
                : OfferingFailure.InvalidRequest;

            // Rejections also arrive after a local failure was already recorded and reported.
            if (_pendingRequestId != 0L && requestId == _pendingRequestId)
            {
                RecordOutcome(false, failure);
                _pendingRequestId = 0L;
                _pendingAltarId = ZDOID.None;
                AltarMenu.OnFailure(failure);
            }
        }

        // --- coroutine wrappers for Jotunn registration ---------------------------

        private static IEnumerator OnRequestStateServer(long sender, ZPackage package) { HandleRequestStateServer(sender, package); yield break; }
        private static IEnumerator OnOfferServer(long sender, ZPackage package) { HandleOfferServer(sender, package); yield break; }
        private static IEnumerator OnQuoteServer(long sender, ZPackage package) { yield break; }
        private static IEnumerator OnPaymentServer(long sender, ZPackage package) { HandlePaymentServer(sender, package); yield break; }
        private static IEnumerator OnStateServer(long sender, ZPackage package) { yield break; }
        private static IEnumerator OnRejectServer(long sender, ZPackage package) { yield break; }

        private static IEnumerator OnRequestStateClient(long sender, ZPackage package) { yield break; }
        private static IEnumerator OnOfferClient(long sender, ZPackage package) { yield break; }
        private static IEnumerator OnQuoteClient(long sender, ZPackage package) { HandleQuoteClient(sender, package); yield break; }
        private static IEnumerator OnPaymentClient(long sender, ZPackage package) { yield break; }
        private static IEnumerator OnStateClient(long sender, ZPackage package) { HandleStateClient(sender, package); yield break; }
        private static IEnumerator OnRejectClient(long sender, ZPackage package) { HandleRejectClient(sender, package); yield break; }

    }
}