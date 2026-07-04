#nullable enable
using System.Collections.Generic;
using Convai.RestAPI;
using Newtonsoft.Json;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Prepares and serializes room-connect requests so every transport sends the same wire contract.
    /// </summary>
    internal static class RoomConnectionRequestTransportSerializer
    {
        private static readonly JsonSerializerSettings ConnectRequestJsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        internal static void PrepareForTransport(
            RoomConnectionRequest request,
            ConvaiRestClientOptions options)
        {
            ValidateRequest(request);
            NormalizeIdentityFields(request);
            PopulateInvocationMetadata(request, options);
        }

        internal static string SerializeForTransport(
            RoomConnectionRequest request,
            ConvaiRestClientOptions options)
        {
            PrepareForTransport(request, options);
            return JsonConvert.SerializeObject(request, ConnectRequestJsonSettings);
        }

        internal static void PopulateInvocationMetadata(
            RoomConnectionRequest request,
            ConvaiRestClientOptions options)
        {
            request.InvocationMetadata ??= new RoomInvocationMetadata();

            if (string.IsNullOrWhiteSpace(request.InvocationMetadata.Source))
            {
                request.InvocationMetadata.Source = options.InvocationSource;
            }

            if (string.IsNullOrWhiteSpace(request.InvocationMetadata.ClientVersion))
            {
                request.InvocationMetadata.ClientVersion = options.ClientVersion;
            }
        }

        internal static void ValidateRequest(RoomConnectionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CharacterId))
            {
                throw new ConvaiRestException(
                    "Character ID is required",
                    ConvaiRestErrorCategory.BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.CoreServiceUrl))
            {
                throw new ConvaiRestException(
                    "Core service URL is required",
                    ConvaiRestErrorCategory.BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.Transport))
            {
                throw new ConvaiRestException(
                    "Transport type is required",
                    ConvaiRestErrorCategory.BadRequest);
            }
        }

        private static void NormalizeIdentityFields(RoomConnectionRequest request)
        {
            request.EndUserId = string.IsNullOrWhiteSpace(request.EndUserId)
                ? null
                : request.EndUserId.Trim();
            request.EndUserMetadata = NormalizeEndUserMetadata(request.EndUserMetadata);
        }

        private static IReadOnlyDictionary<string, object>? NormalizeEndUserMetadata(
            IReadOnlyDictionary<string, object>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return null;

            var normalized = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> entry in metadata)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                normalized[entry.Key.Trim()] = entry.Value;
            }

            return normalized.Count == 0 ? null : normalized;
        }
    }
}
