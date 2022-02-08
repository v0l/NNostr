using LinqKit;
using NBitcoin.Secp256k1;

namespace NNostr.Client
{
    public static class NostrExtensions
    {
        public static string ToJson(this NostrEvent nostrEvent, bool withoutId)
        {
            return
                $"[{(withoutId? 0: nostrEvent.Id)},\"{nostrEvent.PublicKey}\",{nostrEvent.CreatedAt?.ToUnixTimeSeconds()},{nostrEvent.Kind},[{string.Join(',', nostrEvent.Tags.Select(tag => tag.ToString()))}],\"{nostrEvent.Content}\"]";
        }

        public static string ComputeId(this NostrEvent nostrEvent)
        {
            return nostrEvent.ToJson(true).ComputeSha256Hash().ToHex();
        }

        public static string ComputeSignature(this NostrEvent nostrEvent, ECPrivKey priv)
        {
            return nostrEvent.ToJson(true).ComputeSignature(priv);
        }

        public static void ComputeIdAndSign(this NostrEvent nostrEvent, ECPrivKey priv, bool handlenip4 = true)
        {
            if (handlenip4 && nostrEvent.Kind == 4)
            {
                nostrEvent.EncryptNip04Event(priv);
            }
            nostrEvent.Id = nostrEvent.ComputeId();
            nostrEvent.Signature = nostrEvent.ComputeSignature(priv);
        }

        public static bool Verify(this NostrEvent nostrEvent)
        {
            var hash = nostrEvent.ToJson(true).ComputeSha256Hash();
            if (hash.ToHex() != nostrEvent.Id)
            {
                return false;
            }

            var pub = nostrEvent.GetPublicKey();
            if (!SecpSchnorrSignature.TryCreate(nostrEvent.Signature.DecodHexData(), out var sig))
            {
                return false;
            }

            return pub.SigVerifyBIP340(sig, hash);
        }

        public static ECXOnlyPubKey GetPublicKey(this NostrEvent nostrEvent)
        {
            return ParsePubKey(nostrEvent.PublicKey);
        }

        public static ECPrivKey ParseKey(string key)
        {
            return ECPrivKey.Create(key.DecodHexData());
        }

        public static ECXOnlyPubKey ParsePubKey(string key)
        {
            return Context.Instance.CreateXOnlyPubKey(key.DecodHexData());
        }
        
        public static string ToHex(this ECPrivKey key)
        {
            Span<byte> output = new Span<byte>(new byte[32]);
            key.WriteToSpan(output);
            return output.ToHex();
        }
        
        public static string ToHex(this ECXOnlyPubKey key)
        {
            return key.ToBytes().ToHex();
        }
        
        
        public static IQueryable<NostrEvent> Filter(this IQueryable<NostrEvent> events, bool includeDeleted = false,
            params NostrSubscriptionFilter[] filters)
        {
            IQueryable<NostrEvent> result = null;
            foreach (var filter in filters)
            {
                var filterQuery = events;
                if (!includeDeleted)
                {
                    filterQuery = filterQuery.Where(e => !e.Deleted);
                }

                if (filter.Ids?.Any() is true)
                {
                    filterQuery = filterQuery.Where(filter.Ids.Aggregate(PredicateBuilder.New<NostrEvent>(),
                        (current, temp) => current.Or(p => p.Id.StartsWith(temp))));
                }

                if (filter.Kinds?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e => filter.Kinds.Contains(e.Kind));
                }

                if (filter.Since != null)
                {
                    filterQuery = filterQuery.Where(e => e.CreatedAt > filter.Since);
                }

                if (filter.Until != null)
                {
                    filterQuery = filterQuery.Where(e => e.CreatedAt < filter.Until);
                }

                var authors = filter.Authors?.Where(s => !string.IsNullOrEmpty(s))?.ToArray();
                if (authors?.Any() is true)
                {
                    filterQuery = filterQuery.Where(authors.Aggregate(PredicateBuilder.New<NostrEvent>(),
                        (current, temp) => current.Or(p => p.PublicKey.StartsWith(temp))));
                }

                if (filter.EventId?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "e" && filter.EventId.Contains(tag.Data[0])));
                }

                if (filter.PublicKey?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "p" && filter.PublicKey.Contains(tag.Data[0])));
                }

                var tagFilters = filter.GetAdditionalTagFilters();
                filterQuery = tagFilters.Where(tagFilter => tagFilter.Value.Any()).Aggregate(filterQuery,
                    (current, tagFilter) => current.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == tagFilter.Key && tagFilter.Value.Equals(tag.Data[1]))));

                result = result is null ? filterQuery : result.Union(filterQuery);
            }

            return result;
        }

        public static IEnumerable<NostrEvent> Filter(this IEnumerable<NostrEvent> events, bool includeDeleted = false,
            params NostrSubscriptionFilter[] filters)
        {
            IEnumerable<NostrEvent> result = null;
            foreach (var filter in filters)
            {
                var filterQuery = events;
                if (!includeDeleted)
                {
                    filterQuery = filterQuery.Where(e => !e.Deleted);
                }

                if (filter.Ids?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e => filter.Ids.Any(s => e.Id.StartsWith(s)));
                }

                if (filter.Kinds?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e => filter.Kinds.Contains(e.Kind));
                }

                if (filter.Since != null)
                {
                    filterQuery = filterQuery.Where(e => e.CreatedAt > filter.Since);
                }

                if (filter.Until != null)
                {
                    filterQuery = filterQuery.Where(e => e.CreatedAt < filter.Until);
                }

                var authors = filter.Authors?.Where(s => !string.IsNullOrEmpty(s))?.ToArray();
                if (authors?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e => authors.Any(s => e.PublicKey.StartsWith(s)));
                }

                if (filter.EventId?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "e" && filter.EventId.Contains(tag.Data[0])));
                }

                if (filter.PublicKey?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "p" && filter.PublicKey.Contains(tag.Data[0])));
                }

                var tagFilters = filter.GetAdditionalTagFilters();
                filterQuery = tagFilters.Where(tagFilter => tagFilter.Value.Any()).Aggregate(filterQuery,
                    (current, tagFilter) => current.Where(e =>
                        e.Tags.Any(tag =>
                            tag.TagIdentifier == tagFilter.Key && tagFilter.Value.Contains(tag.Data[0]))));

                result = result is null ? filterQuery : result.Union(filterQuery);
            }

            return result;
        }
    }
}