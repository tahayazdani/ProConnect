using ProConnect.Domain.Response;
using System;
using System.Threading.Tasks;

namespace ProConnect.Application.Interface
{
    /// <summary>
    /// Seam between MessageService and the real-time push transport. Implemented by
    /// ProConnect.WebApi/Hubs/SignalRMessageNotifier.cs, which wraps a SignalR IHubContext&lt;MessageHub&gt;
    /// - the Application layer depends on this abstraction, not on SignalR/WebApi directly, so swapping or
    /// removing the transport later is a DI registration change, not a rewrite (same reasoning as
    /// IProfileProvider for the profile-scraper vendor seam).
    ///
    /// This is additive to the existing REST endpoints, not a replacement - MessageController's
    /// GET/POST still send and page history exactly as before; a notifier call just pushes a live
    /// "something changed" event to whichever clients have joined the match's SignalR group.
    ///
    /// Best-effort only. A push failure must never fail the REST call that already durably persisted the
    /// underlying write - callers (MessageService) are expected to swallow/log exceptions from these
    /// methods rather than let them propagate, the same "swallow after logging" reasoning documented for
    /// Cache/RedisCache.Set's handling of Redis outages. The message/read-state is already the source of
    /// truth in Postgres and visible on the next GET regardless of whether the live push succeeds.
    /// </summary>
    public interface IMessageNotifier
    {
        /// <summary>Pushes a newly-sent message to every client currently joined to its match's group.</summary>
        Task NotifyNewMessage(MessageResponse message);

        /// <summary>
        /// Pushes a "messages were read" event to a match's group - `readCount` is the number of
        /// messages MessageRepository.MarkAsRead actually flipped, which the caller should already have
        /// checked is > 0 before calling this (no point notifying when a re-open changed nothing).
        /// </summary>
        Task NotifyMessagesRead(Guid matchId, Guid readerUserId, int readCount);
    }
}
