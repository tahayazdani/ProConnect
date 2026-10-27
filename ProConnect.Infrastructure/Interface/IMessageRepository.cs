using ProConnect.Domain.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IMessageRepository
    {
        /// <summary>
        /// Whether `userid` is actually one of the two participants (userid_a/userid_b) of `matchId` and
        /// that match still exists - called by MessageService before every send/read so one user can't
        /// send or read messages for a match they aren't part of (mirrors MatchRepository.Unmatch's own
        /// ownership check).
        /// </summary>
        Task<bool> IsParticipant(Guid userid, Guid matchId);

        /// <summary>
        /// BUG-21 fix: combines the IsParticipant check and the insert into a single transaction guarded by
        /// a pg_advisory_xact_lock keyed on matchId, so a concurrent MatchRepository.Unmatch (which takes
        /// the identical lock before its delete) can no longer land in between the check and the insert and
        /// cause an FK-violation 500 (reproduced in BUGS_FOUND.md BUG-21 / G-24). Returns null if the sender
        /// is not a participant of matchId (including a matchId that was just unmatched) - caller
        /// (MessageService) maps that to "Match not found." Superseded the old plain SendMessage, which
        /// trusted a separate, unlocked IsParticipant call made moments earlier by the caller.
        /// </summary>
        Task<Message> SendMessageIfParticipant(Guid matchId, Guid senderUserId, string content);

        /// <summary>
        /// A match's message history, most-recent-first (same ordering convention as
        /// IMatchRepository.GetMatches), DB-level offset/limit so a long-running conversation's full
        /// history is never materialized just to serve one page - see MessageService.GetMessages.
        /// </summary>
        Task<IEnumerable<Message>> GetMessages(Guid matchId, int offset, int limit);

        /// <summary>
        /// Total message count for `matchId`, without loading/ordering/paging any rows - lets
        /// MessageService report total_count/has_more the same way MatchRepository.CountMatches does.
        /// </summary>
        Task<int> CountMessages(Guid matchId);

        /// <summary>
        /// Marks every not-yet-read message in `matchId` that was sent *to* `readerUserId` (i.e. sent by
        /// the other participant) as read, stamping read_at_utc. A bulk, single round-trip update
        /// (ExecuteUpdateAsync) rather than loading rows into memory first - see MessageRepository.
        /// Returns the number of rows actually updated, so MessageService knows whether to push a
        /// MessagesRead notification (re-opening an already-read conversation returns 0, no push needed).
        /// </summary>
        Task<int> MarkAsRead(Guid matchId, Guid readerUserId);
    }
}
